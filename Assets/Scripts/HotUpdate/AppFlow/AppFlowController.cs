using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HotUpdate.Auth;
using HotUpdate.Services;
using HotUpdate.UI;
using HotUpdate.Gameplay;
using HotUpdate.Config;

namespace HotUpdate.AppFlow
{
    /// <summary>
    /// 全局流程：CheckUpdate → Login → Home(状态) → Game(通关上报) → Home
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

        bool _handlersBound;
        int _pendingMapId = 1;
        int _pendingLevelId = 1;

        public AppFlowController(
            IVersionService version,
            IAuthService auth,
            IUIService ui,
            IMatch3Service match3,
            ILeaderboardService leaderboard,
            IPlayerService player)
        {
            _version = version;
            _auth = auth;
            _ui = ui;
            _match3 = match3;
            _leaderboard = leaderboard;
            _player = player;
        }

        public async UniTask StartAsync(CancellationToken ct)
        {
            BindUiHandlers();
            Debug.Log("[AppFlow] Start");
            await GotoAsync(AppState.CheckUpdate, ct);
        }

        void BindUiHandlers()
        {
            if (_handlersBound) return;
            _handlersBound = true;

            _ui.SetLoginHandler(async (user, pass) =>
            {
                var (ok, err) = await _auth.LoginAsync(user, pass);
                if (ok)
                    await GotoAsync(AppState.Home);
                else
                    Debug.LogWarning("Login fail: " + err);
            });

            _ui.SetOfflineEnterHandler(async () =>
            {
                await GotoAsync(AppState.Home);
            });

            // 「开始下一关」：走自动选关逻辑
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

                if (!_player.TrySpendEnergyForEnter())
                {
                    await _ui.ShowErrorAsync("体力不足，请稍后再试");
                    return;
                }

                _pendingMapId = mapId;
                _pendingLevelId = levelId;
                Debug.Log($"[AppFlow] StartNext map={_pendingMapId} level={_pendingLevelId}");
                await GotoAsync(AppState.Game);
            });

            // 点击具体关卡按钮：严格使用传入的 mapId / levelId
            _ui.SetStartLevelHandler(async (mapId, levelId) =>
            {
                if (_player.Energy < HotUpdate.Gameplay.GameRuleConfig.EnergyCostPerLevel)
                {
                    await _ui.ShowErrorAsync("体力不足，请稍后再试");
                    return;
                }

                // 再校验一次解锁（UI 已拦，这里双保险）
                if (levelId > 1)
                {
                    var levels = _player.State?.levels;
                    bool ok = false;
                    var minStars = HotUpdate.Gameplay.GameRuleConfig.MinStarsToUnlockNextLevel;
                    if (levels != null)
                    {
                        foreach (var item in levels)
                        {
                            if (item != null && item.level_id == levelId - 1 && item.stars >= minStars)
                            {
                                ok = true;
                                break;
                            }
                        }
                    }
                    if (!ok)
                    {
                        await _ui.ShowErrorAsync("关卡未解锁");
                        return;
                    }
                }

                // 进关扣体力（客户端先行）
                if (!_player.TrySpendEnergyForEnter())
                {
                    await _ui.ShowErrorAsync("体力不足，请稍后再试");
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

            _auth.TryRestoreToken();
            if (_auth.IsLoggedIn)
                await GotoAsync(AppState.Home, ct);
            else
                await GotoAsync(AppState.Login, ct);
        }

        async UniTask OnLoginAsync(CancellationToken ct)
        {
            await _ui.ShowPanelAsync(UIPanel.Login, ct);
        }

        async UniTask OnHomeAsync(CancellationToken ct)
        {
            await _player.RefreshProfileAsync(ct);

            int mapId = _player.CurrentMapId > 0 ? _player.CurrentMapId : 1;
            await _player.RefreshStateAsync(mapId: mapId, ct);

            // 下一可玩关卡号（用于底部「第N关」按钮）
            int nextLevelId = 1;
            if (_player.TryGetNextPlayableLevel(out var nextMap, out var nextLv))
            {
                // 若下一关在别的地图，底部按钮仍显示本图进度逻辑下的下一关号
                nextLevelId = nextMap == mapId ? nextLv : nextLv;
            }
            else
            {
                // 全通或体力不足：仍显示当前地图最后一关号作参考
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
            // 严格用 pending，保证和点击的关卡一致
            var mapId = _pendingMapId;
            var levelId = _pendingLevelId;
            Debug.Log($"[AppFlow] OnGameAsync map={mapId} level={levelId}");

            var cfg = LevelConfigTable.Get(mapId, levelId);

            // 先设状态再显示，避免闪一下错误关卡号
            _ui.SetGameStatus(cfg.MapId, cfg.LevelId, cfg.MaxSteps);
            await _ui.ShowPanelAsync(UIPanel.Game, ct);

            var result = await _match3.PlayAsync(cfg, ct);
            if (result.Success)
            {
                Debug.Log($"[AppFlow] clear map={result.MapId} lv={result.LevelId} stars={result.Stars} steps={result.Steps} score={result.Score}");
                await _player.ClearLevelAsync(
                    result.MapId, result.LevelId, result.Stars, result.Steps, result.Score, ct);
                await _leaderboard.SubmitScoreAsync(result.Score, ct);
            }

            await GotoAsync(AppState.Home, ct);
        }
    }
}
