using HotUpdate.Config;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 全局玩法 / 经济规则。
    /// 默认值与服务端 GameRules.json 对齐；启动时由 ConfigLoader 从 GameRules.bytes 覆盖。
    /// 业务代码只读这里的静态字段，不要写死数字。
    /// </summary>
    public static class GameRuleConfig
    {
        // ---------- 体力 ----------
        /// <summary>体力上限</summary>
        public static int EnergyMax = 30;

        /// <summary>进入一局消耗的体力（进关扣）</summary>
        public static int EnergyCostPerLevel = 1;

        /// <summary>自然恢复 1 点体力所需秒数</summary>
        public static int EnergyRegenSeconds = 300;

        // ---------- 地图 / 关卡 ----------
        /// <summary>每张地图关卡数</summary>
        public static int LevelsPerMap = 10;

        /// <summary>本图通关多少关（有星）后解锁下一张地图</summary>
        public static int MapUnlockNeedClears = 5;

        /// <summary>打下一关需要前一关至少多少星</summary>
        public static int MinStarsToUnlockNextLevel = 1;

        // ---------- 奖励 ----------
        /// <summary>每星金币奖励</summary>
        public static int GoldPerStar = 50;

        // ---------- 校验（与服务端一致，客户端可做本地预检） ----------
        public static bool ValidateLevelExists = true;
        public static bool ValidateStepsAgainstConfig = true;

        static bool _initialized;
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// 用 ExcelConfigCompiler 生成的 GameRules 行填充。应在进 Home/Game 之前调用一次。
        /// 约定：取 Id=1 作为当前生效规则；表为空时保留类内默认值。
        /// </summary>
        public static void Initialize(GameRules[] rows)
        {
            _initialized = false;
            if (rows == null || rows.Length == 0)
            {
                Debug.LogWarning("[GameRuleConfig] GameRules 行为空，使用代码默认值");
                return;
            }

            GameRules row = default;
            bool found = false;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Id == 1)
                {
                    row = rows[i];
                    found = true;
                    break;
                }
            }
            if (!found)
                row = rows[0];

            if (row.EnergyMax > 0) EnergyMax = row.EnergyMax;
            if (row.EnergyCostPerPlay > 0) EnergyCostPerLevel = row.EnergyCostPerPlay;
            if (row.EnergyRegenSeconds > 0) EnergyRegenSeconds = row.EnergyRegenSeconds;
            if (row.LevelsPerMap > 0) LevelsPerMap = row.LevelsPerMap;
            if (row.MapUnlockClearCount > 0) MapUnlockNeedClears = row.MapUnlockClearCount;
            if (row.GoldPerStar > 0) GoldPerStar = row.GoldPerStar;
            if (row.MinStarsToUnlockNextLevel > 0) MinStarsToUnlockNextLevel = row.MinStarsToUnlockNextLevel;
            ValidateLevelExists = row.ValidateLevelExists;
            ValidateStepsAgainstConfig = row.ValidateStepsAgainstConfig;

            _initialized = true;
            Debug.Log(
                $"[GameRuleConfig] loaded EnergyMax={EnergyMax} Cost={EnergyCostPerLevel} " +
                $"Regen={EnergyRegenSeconds}s LevelsPerMap={LevelsPerMap} " +
                $"MapUnlock={MapUnlockNeedClears} GoldPerStar={GoldPerStar}");
        }
    }
}
