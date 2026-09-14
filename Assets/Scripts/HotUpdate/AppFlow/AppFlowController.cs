using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HotUpdate.Auth;
using HotUpdate.Services;
using HotUpdate.UI;
using HotUpdate.Gameplay;
using HotUpdate.Config;
using HotUpdate.Network;

namespace HotUpdate.AppFlow
{
    /// <summary>
    /// 全局流程：CheckUpdate →（校验 Token）→ Login / Home → Game → Home
    /// 401：HTTP 层触发 → 强制登出回 Login。
    /// </summary>
    public class AppFlowController : IAppFlow
    {
        public AppState State { get; private set; } = AppState.Boot;

        readonly IVersionService _version;
        readonly IAuthService _auth;
        readonly IUIService _ui;
        readonly IMatch3Service _match3;
        readonly ILeaderboardService _leaderboard;
        readonly IPlayerService _player;
        readonly IHttpClient _http;

        bool _handlersBound;
        bool _unauthorizedHandling;
        int _pendingMapId = 1;
        int _pendingLevelId = 1;

        public AppFlowController(
            IVersionService version,
            IAuthService auth,
            IUIService ui,
            IMatch3Service match3,
            ILeaderboardService leaderboard,
            IPlayerService player,
            IHttpClient http)
        {
            _version = version;
            _auth = auth;
            _ui = ui;
            _match3 = match3;
            _leaderboard = leaderboard;
            _player = player;
            _http = http;
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            // 全局 401 → 登出回登录（防抖，避免并发请求连跳多次）
            _http.Unauthorized += OnUnauthorized;

            BindUiHandlers();
            Debug.Log("[AppFlow] Start");
            await GotoAsync(AppState.CheckUpdate, ct);
        }

        void OnUnauthorized()
        {
            if (_unauthorizedHandling) return;
            if (State == AppState.Login || State == AppState.Boot || State == AppState.CheckUpdate)
                return;

            _unauthorizedHandling = true;
            Debug.LogWarning("[AppFlow] 401 → force Logout + Login");
            _auth.Logout();
            GotoAsync(AppState.Login).ContinueWith(() => { _unauthorizedHandling = false; }).Forget();
        }

        void BindUiHandlers()
        {
            if (_handlersBound) return;
            _handlersBound = true;

            _ui.SetLoginHandler(async (user, pass) =>
            {
                var (ok, err) = await _auth.LoginAsync(user, pass);
                if (ok)
                {
                    // 仅登录成功时拉 profile + state，之后回 Home 用缓存
                    await LoadPlayerDataAsync();
                    await GotoAsync(AppState.Home);
                    return;
                }

                Debug.LogWarning("Login fail: " + err);
                var msg = FormatLoginError(err);
                // 网络/服务器问题用 Dialog，一般错误用 Toast
                if (IsNetworkLikeError(err))
                    await _ui.ShowDialogAsync(msg, title: "登录失败");
                else
                    _ui.ShowToast(msg);
            });

            _ui.SetOfflineEnterHandler(async () =>
            {
                await GotoAsync(AppState.Home);
            });

            _ui.SetStartGameHandler(async () =>
            {
                if (!_player.TryGetNextPlayableLevel(out var mapId, out var levelId))
                {
                    if (_player.Energy < HotUpdate.Gameplay.GameRuleConfig.EnergyCostPerLevel)
                        await _ui.ShowErrorAsync("体力不足，请稍后再试");
                    else
                        await _ui.ShowErrorAsync("关卡未解锁");
                    return;
                }

                // 服务端进关扣体力，成功才跳转
                var (enterOk, enterErr) = await _player.EnterLevelAsync(mapId, levelId);
                if (!enterOk)
                {
                    await _ui.ShowErrorAsync(string.IsNullOrEmpty(enterErr) ? "进关失败" : enterErr);
                    return;
                }

                _pendingMapId = mapId;
                _pendingLevelId = levelId;
                Debug.Log($"[AppFlow] StartNext map={_pendingMapId} level={_pendingLevelId}");
                await GotoAsync(AppState.Game);
            });

            _ui.SetStartLevelHandler(async (mapId, levelId) =>
            {
                // 本地预检；真正扣体力以服务端 /level/enter 为准
                if (_player.Energy < HotUpdate.Gameplay.GameRuleConfig.EnergyCostPerLevel)
                {
                    await _ui.ShowErrorAsync("体力不足，请稍后再试");
                    return;
                }

                if (levelId > 1)
                {
                    var levels = _player.State?.levels;
                    bool unlocked = false;
                    var minStars = HotUpdate.Gameplay.GameRuleConfig.MinStarsToUnlockNextLevel;
                    if (levels != null)
                    {
                        foreach (var item in levels)
                        {
                            if (item != null && item.level_id == levelId - 1 && item.stars >= minStars)
                            {
                                unlocked = true;
                                break;
                            }
                        }
                    }
                    if (!unlocked)
                    {
                        await _ui.ShowErrorAsync("关卡未解锁");
                        return;
                    }
                }

                var (enterOk, enterErr) = await _player.EnterLevelAsync(mapId, levelId);
                if (!enterOk)
                {
                    await _ui.ShowErrorAsync(string.IsNullOrEmpty(enterErr) ? "进关失败" : enterErr);
                    return;
                }

                _pendingMapId = mapId;
                _pendingLevelId = levelId;
                Debug.Log($"[AppFlow] StartLevel click map={_pendingMapId} level={_pendingLevelId}");
                await GotoAsync(AppState.Game);
            });

            _ui.SetLogoutHandler(() =>
            {
                _auth.Logout();
                GotoAsync(AppState.Login).Forget();
            });
        }

        public async UniTask GotoAsync(AppState next, CancellationToken ct = default)
        {
            var prev = State;
            State = next;
            Debug.Log($"[AppFlow] {prev} -> {next}");

            try
            {
                switch (next)
                {
                    case AppState.CheckUpdate:
                        await OnCheckUpdateAsync(ct);
                        break;
                    case AppState.Login:
                        await OnLoginAsync(ct);
                        break;
                    case AppState.Home:
                        await OnHomeAsync(ct);
                        break;
                    case AppState.Game:
                        await OnGameAsync(ct);
                        break;
                    case AppState.Result:
                        await GotoAsync(AppState.Home, ct);
                        break;
                    case AppState.Error:
                        await _ui.ShowErrorAsync("发生错误，请重试", ct);
                        await GotoAsync(AppState.Login, ct);
                        break;
                }
            }
            catch (UnauthorizedException)
            {
                // HTTP 已触发 OnUnauthorized；此处兜底保证停在 Login
                Debug.LogWarning("[AppFlow] UnauthorizedException in Goto");
                if (State != AppState.Login)
                {
                    _auth.Logout();
                    await GotoAsync(AppState.Login, ct);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[AppFlow] Canceled");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                State = AppState.Error;
                await _ui.ShowErrorAsync(e.Message, ct);
            }
        }

        async UniTask OnCheckUpdateAsync(CancellationToken ct)
        {
            await _ui.ShowPanelAsync(UIPanel.CheckUpdate, ct);
            var ok = await _version.CheckAndUpdateAsync(ct);
            if (!ok)
            {
                await GotoAsync(AppState.Error, ct);
                return;
            }

            try
            {
                await ConfigLoader.LoadDefaultAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("[AppFlow] 配置加载失败，关卡将缺少表数据");
            }

            // 关键：不能只看本地有没有 token 字符串
            _auth.TryRestoreToken();
            if (_auth.IsLoggedIn)
            {
                var valid = await _auth.ValidateSessionAsync(ct);
                if (valid)
                {
                    // 会话恢复成功：拉一次全量，之后回 Home 不再刷
                    await LoadPlayerDataAsync(ct);
                    await GotoAsync(AppState.Home, ct);
                    return;
                }
                // 无效已 Logout，或网络失败 → 走登录
                Debug.Log("[AppFlow] session invalid or unreachable → Login");
            }

            await GotoAsync(AppState.Login, ct);
        }

        async UniTask OnLoginAsync(CancellationToken ct)
        {
            await _ui.ShowPanelAsync(UIPanel.Login, ct);
        }

        /// <summary>登录/会话恢复时拉 profile + state；回 Home 不要调。</summary>
        async UniTask LoadPlayerDataAsync(CancellationToken ct = default)
        {
            await _player.RefreshProfileAsync(ct);
            var mapId = _player.CurrentMapId > 0 ? _player.CurrentMapId : 1;
            await _player.RefreshStateAsync(mapId: mapId, ct);
        }

        async UniTask OnHomeAsync(CancellationToken ct)
        {
            // 只用内存缓存刷 UI；进关/通关已用接口返回体更新本地
            int mapId = _player.CurrentMapId > 0 ? _player.CurrentMapId : 1;

            int nextLevelId = 1;
            if (_player.TryGetNextPlayableLevel(out var nextMap, out var nextLv))
            {
                nextLevelId = nextMap == mapId ? nextLv : nextLv;
            }
            else
            {
                nextLevelId = LevelConfigTable.LevelsPerMap;
            }

            _ui.SetHomeStatus(
                _player.Energy,
                _player.EnergyMax,
                _player.Gold,
                _player.UnlockedMap,
                _player.State?.cleared_on_map ?? 0,
                _player.State?.levels_per_map ?? LevelConfigTable.LevelsPerMap,
                nextLevelId);

            _ui.SetHomeLevels(mapId, _player.State?.levels);

            await _ui.ShowPanelAsync(UIPanel.Home, ct);
        }

        async UniTask OnGameAsync(CancellationToken ct)
        {
            var mapId = _pendingMapId;
            var levelId = _pendingLevelId;
            Debug.Log($"[AppFlow] OnGameAsync map={mapId} level={levelId}");

            var cfg = LevelConfigTable.Get(mapId, levelId);

            _ui.SetGameStatus(cfg.MapId, cfg.LevelId, cfg.MaxSteps);
            await _ui.ShowPanelAsync(UIPanel.Game, ct);

            var result = await _match3.PlayAsync(cfg, ct);

            // 结算弹窗（仍在 Game 页上）
            await _ui.ShowLevelResultAsync(
                result.Success,
                result.Stars,
                result.Score,
                result.Steps,
                ct);

            if (result.Success)
            {
                Debug.Log($"[AppFlow] clear map={result.MapId} lv={result.LevelId} stars={result.Stars} steps={result.Steps} score={result.Score}");
                await _player.ClearLevelAsync(
                    result.MapId, result.LevelId, result.Stars, result.Steps, result.Score, ct);
                try
                {
                    await _leaderboard.SubmitScoreAsync(result.Score, ct);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[AppFlow] submit score fail: " + e.Message);
                }
            }

            // 回主页后要弹「是否进入」的目标关
            int promptMap = mapId;
            int promptLevel = levelId;
            if (result.Success)
            {
                // 成功：指向当前可打的最高/下一关
                if (_player.TryGetNextPlayableLevel(out var nm, out var nl))
                {
                    promptMap = nm;
                    promptLevel = nl;
                }
                else
                {
                    // 体力不足或已通关本图：仍提示下一关号（尽量 +1）
                    promptLevel = Mathf.Min(levelId + 1, LevelConfigTable.LevelsPerMap);
                    promptMap = mapId;
                }
            }
            else
            {
                // 失败：当前关重打
                promptMap = mapId;
                promptLevel = levelId;
            }

            await GotoAsync(AppState.Home, ct);
            // 主页已刷完，高亮并弹确认
            _ui.PromptEnterLevel(promptMap, promptLevel);
        }

        static bool IsNetworkLikeError(string err)
        {
            if (string.IsNullOrEmpty(err)) return true;
            var e = err.ToLowerInvariant();
            return e.Contains("connect")
                || e.Contains("timeout")
                || e.Contains("connection")
                || e.Contains("refused")
                || e.Contains("unreachable")
                || e.Contains("name resolution")
                || e.Contains("socket")
                || e.Contains("http")
                || e.Contains("network")
                || e.Contains("host")
                || e.Contains("ssl")
                || e.Contains("403")
                || e.Contains("502")
                || e.Contains("503")
                || e.Contains("504");
        }

        static string FormatLoginError(string err)
        {
            if (string.IsNullOrEmpty(err))
                return "登录失败，请稍后重试。";
            if (IsNetworkLikeError(err))
                return "无法连接服务器，请确认服务已启动或检查网络。\n\n详情：" + err;
            return "登录失败：" + err;
        }

    }
}
