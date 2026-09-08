using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.UI
{
    public interface IUIService
    {
        UniTask ShowPanelAsync(UIPanel panel, CancellationToken ct = default);
        UniTask ShowErrorAsync(string message, CancellationToken ct = default);

        void SetLoginHandler(Func<string, string, UniTask> handler);
        void SetOfflineEnterHandler(Func<UniTask> handler);
        void SetStartGameHandler(Func<UniTask> handler);
        void SetLogoutHandler(Action handler);
    }
}
