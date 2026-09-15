using VContainer;
using VContainer.Unity;
using HotUpdate.Network;
using HotUpdate.Auth;
using HotUpdate.Services;
using HotUpdate.Gameplay;
using HotUpdate.UI;
using HotUpdate.AppFlow;
using HotUpdate.DebugTools;

namespace HotUpdate
{
    /// <summary>
    /// 可选：挂在场景里用的 LifetimeScope。
    /// 代码启动优先走 AppEntry 的手动 ContainerBuilder。
    /// </summary>
    public class RootLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            Install(builder);
        }

        /// <summary>
        /// 统一注册，LifetimeScope 与手动 Build 共用。
        /// </summary>
        public static void Install(IContainerBuilder builder)
        {
            // Network
            builder.Register<ApiConfig>(Lifetime.Singleton);
            builder.Register<IHttpClient, HttpClientService>(Lifetime.Singleton);

            // Services
            builder.Register<IAuthService, AuthService>(Lifetime.Singleton);
            builder.Register<IPlayerService, PlayerService>(Lifetime.Singleton);
            builder.Register<ILeaderboardService, LeaderboardService>(Lifetime.Singleton);
            builder.Register<IVersionService, VersionService>(Lifetime.Singleton);
            builder.Register<IResService, ResService>(Lifetime.Singleton);
            builder.Register<IAudioManager, AudioManager>(Lifetime.Singleton);

            // UI：UIToolkit 壳 + Match3 HUD
            builder.Register<UIService>(Lifetime.Singleton);
            builder.Register<IUIService>(r => r.Resolve<UIService>(), Lifetime.Singleton);
            builder.Register<IMatch3Hud>(r => r.Resolve<UIService>(), Lifetime.Singleton);

            // 棋盘：SpriteRenderer 视图（不用 UGUI/UIToolkit 格子）
            builder.Register<IMatch3View, Match3SpriteView>(Lifetime.Singleton);

            // Gameplay
            builder.Register<IMatch3Service, Match3Service>(Lifetime.Singleton);

            // Flow
            builder.Register<IAppFlow, AppFlowController>(Lifetime.Singleton);

            // Input Test
            builder.RegisterEntryPoint<DebugConsole>();
            // 旧快捷键保留可选
            // builder.RegisterEntryPoint<DebugInputSystem>();
        }
    }
}
