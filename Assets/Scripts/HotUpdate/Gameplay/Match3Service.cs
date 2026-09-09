using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// Match3 占位：模拟步数与星级，走通「通关上报」链路。
    /// 真实项目在此实现棋盘、消除、掉落。
    /// </summary>
    public class Match3Service : IMatch3Service
    {
        public async UniTask<Match3Result> PlayAsync(LevelConfig cfg, CancellationToken ct = default)
        {
            if (cfg == null)
                cfg = LevelConfigTable.Get(1, 1);

            Debug.Log($"[Match3] Play map={cfg.MapId} level={cfg.LevelId} maxSteps={cfg.MaxSteps}");
            await UniTask.Delay(1200, cancellationToken: ct);

            // mock：随机消耗若干步并通关
            var steps = Random.Range(
                Mathf.Max(1, cfg.StepsFor3Stars - 2),
                cfg.MaxSteps + 1);
            var stars = LevelConfigTable.CalcStars(cfg, steps, cleared: true);
            var score = cfg.GoalValue + stars * 500 + Random.Range(0, 400);

            Debug.Log($"[Match3] End stars={stars} steps={steps} score={score}");
            return new Match3Result
            {
                Success = true,
                MapId = cfg.MapId,
                LevelId = cfg.LevelId,
                Stars = stars,
                Steps = steps,
                Score = score
            };
        }
    }
}
