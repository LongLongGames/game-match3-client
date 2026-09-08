using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.AppFlow
{
    public interface IAppFlow
    {
        AppState State { get; }
        UniTask StartAsync(CancellationToken ct);
        UniTask GotoAsync(AppState next, CancellationToken ct = default);
    }
}
