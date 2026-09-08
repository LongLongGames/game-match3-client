using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HotUpdate.Network;
using HotUpdate.Services;

namespace HotUpdate.Auth
{
    public class AuthService : IAuthService
    {
        readonly IHttpClient _http;
        readonly ApiConfig _config;
        readonly IVersionService _version; // 复用本地存储

        public bool IsLoggedIn => !string.IsNullOrEmpty(_http.AccessToken);

        public AuthService(IHttpClient http, ApiConfig config, IVersionService version)
        {
            _http = http;
            _config = config;
            _version = version;
        }

        public async UniTask<(bool ok, string error)> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            var body = new LoginRequest
            {
                provider = "official",
                app_id = _config.AppId,
                device_id = SystemInfo.deviceUniqueIdentifier,
                auth_payload = new LoginAuthPayload
                {
                    username = username,
                    password = password
                }
            };

            var json = JsonUtility.ToJson(body);
            var url = _config.MpBaseUrl + "/api/v1/auth/login";

            try
            {
                var text = await _http.PostJsonAsync(url, json, auth: false, ct);
                var resp = JsonUtility.FromJson<LoginResponse>(text);
                if (resp == null || string.IsNullOrEmpty(resp.access_token))
                    return (false, "login failed: empty token");

                _http.AccessToken = resp.access_token;
                _version.SetAccessToken(resp.access_token);
                return (true, null);
            }
            catch (Exception e)
            {
                return (false, e.Message);
            }
        }

        public void TryRestoreToken()
        {
            var t = _version.GetAccessToken();
            if (!string.IsNullOrEmpty(t))
                _http.AccessToken = t;
        }

        public void Logout()
        {
            _http.AccessToken = null;
            _version.ClearToken();
        }
    }
}
