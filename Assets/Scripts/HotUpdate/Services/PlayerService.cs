using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HotUpdate.Network;

namespace HotUpdate.Services
{
    public class PlayerService : IPlayerService
    {
        readonly IHttpClient _http;
        readonly ApiConfig _config;

        public PlayerProfile Profile { get; private set; } = new PlayerProfile { nickname = "Player", level = 1 };

        public PlayerService(IHttpClient http, ApiConfig config)
        {
            _http = http;
            _config = config;
        }

        public async UniTask RefreshProfileAsync(CancellationToken ct = default)
        {
            try
            {
                var url = $"{_config.GameBaseUrl}/api/v1/player/me";
                var text = await _http.GetAsync(url, auth: true, ct);
                var p = JsonUtility.FromJson<PlayerProfile>(text);
                if (p != null) Profile = p;
            }
            catch
            {
                // 单机演示允许失败，使用默认
                Debug.LogWarning("[Player] RefreshProfile failed, use local profile");
            }
        }
    }
}
