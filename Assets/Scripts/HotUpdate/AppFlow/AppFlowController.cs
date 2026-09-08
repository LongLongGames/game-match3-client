using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HotUpdate.Auth;
using HotUpdate.Services;
using HotUpdate.UI;
using HotUpdate.Gameplay;

namespace HotUpdate.AppFlow
{
    /// <summary>
    /// 全局流程控制。UI 通过 Handler 回调跳转，不互相构造注入。
    /// 演示完整链路：CheckUpdate → Login → Home → Game → 提交分数 → Home
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

            _ui.SetStartGameHandler(async () =>
            {
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
            await _ui.ShowPanelAsync(UIPanel.Home, ct);
        }

        async UniTask OnGameAsync(CancellationToken ct)
        {
            await _ui.ShowPanelAsync(UIPanel.Game, ct);
            var result = await _match3.PlayAsync(ct);
            if (result.Success)
            {
                Debug.Log($"[AppFlow] Score={result.Score}");
                await _leaderboard.SubmitScoreAsync(result.Score, ct);
            }
            await GotoAsync(AppState.Home, ct);
        }
    }
}
