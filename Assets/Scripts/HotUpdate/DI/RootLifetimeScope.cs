using VContainer;
using VContainer.Unity;
using HotUpdate.Network;
using HotUpdate.Auth;
using HotUpdate.Services;
using HotUpdate.Gameplay;
using HotUpdate.UI;
using HotUpdate.AppFlow;

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

            // Gameplay
            builder.Register<IMatch3Service, Match3Service>(Lifetime.Singleton);

            // UI（不依赖 IAppFlow）
            builder.Register<IUIService, UIService>(Lifetime.Singleton);

            // Flow
            builder.Register<IAppFlow, AppFlowController>(Lifetime.Singleton);
        }
    }
}
