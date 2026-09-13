using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace HotUpdate.Network
{
    /// <summary>
    /// UniTask 版 HTTP 封装。自动附加 JWT；401 时触发 Unauthorized 并抛 UnauthorizedException。
    /// </summary>
    public class HttpClientService : IHttpClient
    {
        public string AccessToken { get; set; }

        public event Action Unauthorized;

        public async UniTask<string> GetAsync(string url, bool auth = false, CancellationToken ct = default)
        {
            using var req = UnityWebRequest.Get(url);
            ApplyAuth(req, auth);
            return await SendAsync(req, auth, ct);
        }

        public async UniTask<string> PostJsonAsync(string url, string json, bool auth = false, CancellationToken ct = default)
        {
            var body = Encoding.UTF8.GetBytes(json ?? "{}");
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyAuth(req, auth);
            return await SendAsync(req, auth, ct);
        }

        public async UniTask<string> PutJsonAsync(string url, string json, bool auth = true, CancellationToken ct = default)
        {
            var body = Encoding.UTF8.GetBytes(json ?? "{}");
            using var req = new UnityWebRequest(url, "PUT");
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyAuth(req, auth);
            return await SendAsync(req, auth, ct);
        }

        void ApplyAuth(UnityWebRequest req, bool auth)
        {
            if (auth && !string.IsNullOrEmpty(AccessToken))
                req.SetRequestHeader("Authorization", "Bearer " + AccessToken);
        }

        async UniTask<string> SendAsync(UnityWebRequest req, bool auth, CancellationToken ct)
        {
            try
            {
                await req.SendWebRequest().WithCancellation(ct);
            }
            catch (OperationCanceledException)
            {
                req.Abort();
                throw;
            }

            // 鉴权请求 401：通知上层并抛专用异常，避免继续当「已登录」用
            if (auth && req.responseCode == 401)
            {
                Debug.LogWarning($"[Http] 401 Unauthorized: {req.url}");
                try { Unauthorized?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }

                throw new UnauthorizedException(
                    req.responseCode,
                    $"401 Unauthorized\n{req.downloadHandler?.text}");
            }

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"{req.responseCode} {req.error}\n{req.downloadHandler?.text}");

            return req.downloadHandler.text;
        }
    }
}
