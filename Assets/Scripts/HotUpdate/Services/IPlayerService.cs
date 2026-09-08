using System.Threading;
using Cysharp.Threading.Tasks;
using HotUpdate.Network;

namespace HotUpdate.Services
{
    public interface IPlayerService
    {
        PlayerProfile Profile { get; }
        UniTask RefreshProfileAsync(CancellationToken ct = default);
    }
}
