using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 极简 Match3 占位。真实项目在此实现 2D Sprite 棋盘、消除、掉落、计分。
    /// 当前模拟一局结束后返回随机分数，方便走通「提交分数」链路。
    /// </summary>
    public class Match3Service : IMatch3Service
    {
        public async UniTask<Match3Result> PlayAsync(CancellationToken ct = default)
        {
            Debug.Log("[Match3] Play start (mock)");
            // 模拟游玩时间
            await UniTask.Delay(1500, cancellationToken: ct);

            var score = Random.Range(1000, 9999);
            Debug.Log($"[Match3] Play end, score={score}");
            return new Match3Result { Success = true, Score = score };
        }
    }
}
