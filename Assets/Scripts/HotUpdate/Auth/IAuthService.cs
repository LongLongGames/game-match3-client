using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.Auth
{
    public interface IAuthService
    {
        bool IsLoggedIn { get; }

        UniTask<(bool ok, string error)> LoginAsync(string username, string password, CancellationToken ct = default);

        /// <summary>从本地恢复 Token 到内存（不校验有效性）。</summary>
        void TryRestoreToken();

        /// <summary>
        /// 有本地 Token 时向服务端校验是否仍有效。
        /// 有效返回 true；401/无效会 Logout 并返回 false。
        /// 网络错误时不强制登出，返回 false 让流程走登录更安全，也可按需改成 true。
        /// </summary>
        UniTask<bool> ValidateSessionAsync(CancellationToken ct = default);

        void Logout();
    }
}
