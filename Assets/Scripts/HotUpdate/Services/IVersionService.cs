using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.Services
{
    public interface IVersionService
    {
        UniTask<bool> CheckAndUpdateAsync(CancellationToken ct = default);
        string GetAccessToken();
        void SetAccessToken(string token);
        void ClearToken();
    }
}
