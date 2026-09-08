using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.Auth
{
    public interface IAuthService
    {
        bool IsLoggedIn { get; }
        UniTask<(bool ok, string error)> LoginAsync(string username, string password, CancellationToken ct = default);
        void TryRestoreToken();
        void Logout();
    }
}
