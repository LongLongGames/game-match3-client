using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.Network
{
    public interface IHttpClient
    {
        string AccessToken { get; set; }

        /// <summary>
        /// 任意鉴权请求收到 401 时触发（在抛出 UnauthorizedException 之前）。
        /// 由 AppFlow 注册：清 Token + 跳登录。
        /// </summary>
        event Action Unauthorized;

        UniTask<string> GetAsync(string url, bool auth = false, CancellationToken ct = default);
        UniTask<string> PostJsonAsync(string url, string json, bool auth = false, CancellationToken ct = default);
        UniTask<string> PutJsonAsync(string url, string json, bool auth = true, CancellationToken ct = default);
    }
}
