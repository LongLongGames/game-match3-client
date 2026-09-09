using System;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 客户端本地关卡静态配置（弱联网：配置不下发，服务器只存进度）。
    /// 后续可换成 ScriptableObject / 热更 JSON。
    /// </summary>
    [Serializable]
    public class LevelConfig
    {
        public int MapId;
        public int LevelId;
        /// <summary>本关允许的最大步数（用于 mock 星级）</summary>
        public int MaxSteps;
        /// <summary>3 星步数阈值</summary>
        public int StepsFor3Stars;
        /// <summary>2 星步数阈值</summary>
        public int StepsFor2Stars;
        public int BoardWidth = 8;
        public int BoardHeight = 8;
        public string Goal = "score";
        public int GoalValue = 1000;
    }

    public static class LevelConfigTable
    {
        public const int LevelsPerMap = 20;

        /// <summary>
        /// 按 map/level 生成占位配置。真实项目改为读表。
        /// </summary>
        public static LevelConfig Get(int mapId, int levelId)
        {
            // 难度随关卡略增
            var maxSteps = 30 - Math.Min(levelId, 15);
            return new LevelConfig
            {
                MapId = mapId,
                LevelId = levelId,
                MaxSteps = Math.Max(12, maxSteps),
                StepsFor3Stars = Math.Max(8, maxSteps - 8),
                StepsFor2Stars = Math.Max(10, maxSteps - 4),
                BoardWidth = 8,
                BoardHeight = 8,
                Goal = "score",
                GoalValue = 800 + levelId * 100 + mapId * 200
            };
        }

        /// <summary>根据步数估算星级（mock / 客户端自判，服务器仍可再校验）</summary>
        public static int CalcStars(LevelConfig cfg, int steps, bool cleared)
        {
            if (!cleared) return 0;
            if (steps <= cfg.StepsFor3Stars) return 3;
            if (steps <= cfg.StepsFor2Stars) return 2;
            return 1;
        }
    }
}
