using System;
using System.Collections.Generic;
using HotUpdate.Config;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 关卡静态配置（运行时从 ExcelConfigCompiler 二进制表填充）。
    /// </summary>
    [Serializable]
    public class LevelConfig
    {
        public int MapId;
        public int LevelId;
        public int MaxSteps;
        public int StepsFor3Stars;
        public int StepsFor2Stars;
        public int BoardWidth = 8;
        public int BoardHeight = 8;
        public string Goal = "score";
        public int GoalValue = 1000;
    }

    /// <summary>
    /// 关卡表：由 ConfigLoader 在启动时 Initialize，之后 Get 只查内存。
    /// </summary>
    public static class LevelConfigTable
    {
        public const int LevelsPerMap = 20;

        static readonly Dictionary<(int mapId, int levelId), LevelConfig> _byKey = new();
        static bool _initialized;

        public static bool IsInitialized => _initialized;
        public static int Count => _byKey.Count;

        /// <summary>
        /// 用 ExcelConfigCompiler 生成的 Level 行填充。应在进 Home/Game 之前调用一次。
        /// </summary>
        public static void Initialize(Level[] rows)
        {
            _byKey.Clear();
            _initialized = false;

            if (rows == null || rows.Length == 0)
            {
                Debug.LogError("[LevelConfigTable] Level 行为空，关卡表未加载");
                return;
            }

            foreach (var row in rows)
            {
                var cfg = new LevelConfig
                {
                    MapId = row.MapId,
                    LevelId = row.LevelId,
                    MaxSteps = row.MaxSteps,
                    StepsFor3Stars = row.StepsFor3Stars,
                    StepsFor2Stars = row.StepsFor2Stars,
                    BoardWidth = row.BoardWidth > 0 ? row.BoardWidth : 8,
                    BoardHeight = row.BoardHeight > 0 ? row.BoardHeight : 8,
                    Goal = string.IsNullOrEmpty(row.Goal) ? "score" : row.Goal,
                    GoalValue = row.GoalValue
                };
                _byKey[(cfg.MapId, cfg.LevelId)] = cfg;
            }

            _initialized = _byKey.Count > 0;
            Debug.Log($"[LevelConfigTable] loaded {_byKey.Count} levels");
        }

        /// <summary>
        /// 按 map/level 取配置。未初始化或缺失时打错误日志并返回安全占位（避免空引用崩流程）。
        /// </summary>
        public static LevelConfig Get(int mapId, int levelId)
        {
            if (_byKey.TryGetValue((mapId, levelId), out var cfg))
                return cfg;

            Debug.LogError(
                $"[LevelConfigTable] 缺少关卡配置 map={mapId} level={levelId} " +
                $"(initialized={_initialized}, count={_byKey.Count})，返回占位配置");
            return new LevelConfig
            {
                MapId = mapId,
                LevelId = levelId,
                MaxSteps = 30,
                StepsFor3Stars = 15,
                StepsFor2Stars = 22,
                BoardWidth = 8,
                BoardHeight = 8,
                Goal = "score",
                GoalValue = 1000
            };
        }

        public static bool TryGet(int mapId, int levelId, out LevelConfig cfg)
            => _byKey.TryGetValue((mapId, levelId), out cfg);

        public static int CalcStars(LevelConfig cfg, int steps, bool cleared)
        {
            if (!cleared || cfg == null) return 0;
            if (steps <= cfg.StepsFor3Stars) return 3;
            if (steps <= cfg.StepsFor2Stars) return 2;
            return 1;
        }
    }
}
