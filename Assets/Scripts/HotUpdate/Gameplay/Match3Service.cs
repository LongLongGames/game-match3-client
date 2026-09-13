using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 真实 Match3：驱动棋盘逻辑，通过 IMatch3View 与 UI 交互。
    /// 不再自动 mock 通关。
    /// </summary>
    public class Match3Service : IMatch3Service
    {
        readonly IMatch3View _view;

        public Match3Service(IMatch3View view)
        {
            _view = view;
        }

        public async UniTask<Match3Result> PlayAsync(LevelConfig cfg, CancellationToken ct = default)
        {
            if (cfg == null)
                cfg = LevelConfigTable.Get(1, 1);

            // 默认按传统 9×9；配置表有值则尊重配置
            var w = cfg.BoardWidth > 0 ? cfg.BoardWidth : 9;
            var h = cfg.BoardHeight > 0 ? cfg.BoardHeight : 9;
            // 用户要求先按传统 9x9：若表里还是旧 8 也强制 9（可后续只改 Excel）
            if (w == 8 && h == 8) { w = 9; h = 9; }

            Debug.Log($"[Match3] Play map={cfg.MapId} level={cfg.LevelId} board={w}x{h} maxSteps={cfg.MaxSteps} goal={cfg.Goal}:{cfg.GoalValue}");

            var board = new Match3Board(w, h, cfg.MaxSteps, cfg.Goal, cfg.GoalValue);

            if (_view == null)
            {
                Debug.LogError("[Match3] IMatch3View 未注入，无法交互。回退失败结果。");
                return Fail(cfg);
            }

            Match3Result result;
            try
            {
                result = await _view.RunAsync(board, cfg, ct);
            }
            catch (OperationCanceledException)
            {
                return Fail(cfg);
            }

            result.MapId = cfg.MapId;
            result.LevelId = cfg.LevelId;
            if (result.Success)
                result.Stars = LevelConfigTable.CalcStars(cfg, result.Steps, cleared: true);
            else
                result.Stars = 0;

            Debug.Log($"[Match3] End success={result.Success} stars={result.Stars} steps={result.Steps} score={result.Score}");
            return result;
        }

        static Match3Result Fail(LevelConfig cfg) => new Match3Result
        {
            Success = false,
            MapId = cfg.MapId,
            LevelId = cfg.LevelId,
            Stars = 0,
            Steps = 0,
            Score = 0
        };
    }

    /// <summary>
    /// 棋盘视图接口：由 Match3SpriteView（SpriteRenderer）实现，非 UIToolkit。
    /// </summary>
    public interface IMatch3View
    {
        UniTask<Match3Result> RunAsync(Match3Board board, LevelConfig cfg, CancellationToken ct);
    }
}
