using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.Services
{
    public interface ILeaderboardService
    {
        UniTask SubmitScoreAsync(int score, CancellationToken ct = default);
    }
}
