using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace HotUpdate.Network
{
    /// <summary>
    /// UniTask 版 HTTP 封装。自动附加 JWT。
    /// </summary>
    public class HttpClientService : IHttpClient
    {
        public string AccessToken { get; set; }

        public async UniTask<string> GetAsync(string url, bool auth = false, CancellationToken ct = default)
        {
            using var req = UnityWebRequest.Get(url);
            ApplyAuth(req, auth);
            return await SendAsync(req, ct);
        }

        public async UniTask<string> PostJsonAsync(string url, string json, bool auth = false, CancellationToken ct = default)
        {
            var body = Encoding.UTF8.GetBytes(json ?? "{}");
            using var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyAuth(req, auth);
            return await SendAsync(req, ct);
        }

        public async UniTask<string> PutJsonAsync(string url, string json, bool auth = true, CancellationToken ct = default)
        {
            var body = Encoding.UTF8.GetBytes(json ?? "{}");
            using var req = new UnityWebRequest(url, "PUT");
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyAuth(req, auth);
            return await SendAsync(req, ct);
        }

        void ApplyAuth(UnityWebRequest req, bool auth)
        {
            if (auth && !string.IsNullOrEmpty(AccessToken))
                req.SetRequestHeader("Authorization", "Bearer " + AccessToken);
        }

        async UniTask<string> SendAsync(UnityWebRequest req, CancellationToken ct)
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

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"{req.responseCode} {req.error}\n{req.downloadHandler?.text}");

            return req.downloadHandler.text;
        }
    }
}
