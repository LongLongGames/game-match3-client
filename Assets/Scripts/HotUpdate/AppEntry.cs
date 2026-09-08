using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;
using HotUpdate.AppFlow;

namespace HotUpdate
{
    /// <summary>
    /// 热更入口。由 GameBootstrap 反射调用。
    /// 使用手动 ContainerBuilder，避开运行时 AddComponent&lt;LifetimeScope&gt; 的时序问题。
    /// </summary>
    public static class AppEntry
    {
        static IObjectResolver _container;

        public static async UniTask StartAsync()
        {
            Debug.Log("[HotUpdate] AppEntry.StartAsync");

            try
            {
                var builder = new ContainerBuilder();
                RootLifetimeScope.Install(builder);
                _container = builder.Build();

                var app = _container.Resolve<IAppFlow>();
                Debug.Log("[HotUpdate] Container built, starting AppFlow");
                await app.StartAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError("[HotUpdate] AppEntry failed: " + e);
                throw;
            }
        }

        public static T Resolve<T>()
        {
            if (_container == null)
                throw new InvalidOperationException("Container not built yet");
            return _container.Resolve<T>();
        }
    }
}
