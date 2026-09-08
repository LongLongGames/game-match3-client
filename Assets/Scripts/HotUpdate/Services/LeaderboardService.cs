using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HotUpdate.Network;

namespace HotUpdate.Services
{
    public class LeaderboardService : ILeaderboardService
    {
        readonly IHttpClient _http;
        readonly ApiConfig _config;
        readonly IPlayerService _player;

        public LeaderboardService(IHttpClient http, ApiConfig config, IPlayerService player)
        {
            _http = http;
            _config = config;
            _player = player;
        }

        public async UniTask SubmitScoreAsync(int score, CancellationToken ct = default)
        {
            var body = new ScoreRequest
            {
                game_id = _config.GameId,
                board_id = _config.BoardId,
                score = score,
                nickname = _player.Profile?.nickname ?? "Player"
            };

            var json = JsonUtility.ToJson(body);
            var url = $"{_config.GameBaseUrl}/api/v1/leaderboard/score";

            try
            {
                var text = await _http.PostJsonAsync(url, json, auth: true, ct);
                Debug.Log($"[Leaderboard] Submit OK: {text}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Leaderboard] Submit failed (offline ok): {e.Message}");
            }
        }
    }
}
