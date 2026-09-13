namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 全局玩法 / 经济规则（客户端先行，后续可与 Excel / 服务端配置对齐）。
    /// 改数值只改这里，不要在业务代码里写死。
    /// </summary>
    public static class GameRuleConfig
    {
        // ---------- 体力 ----------
        /// <summary>体力上限</summary>
        public static int EnergyMax = 30;

        /// <summary>进入一局消耗的体力（进关扣，不是通关扣）</summary>
        public static int EnergyCostPerLevel = 5;

        /// <summary>自然恢复 1 点体力所需秒数</summary>
        public static int EnergyRegenSeconds = 300;

        // ---------- 地图 / 关卡 ----------
        /// <summary>每张地图关卡数</summary>
        public static int LevelsPerMap = 10;

        /// <summary>本图通关多少关（有星）后解锁下一张地图</summary>
        public static int MapUnlockNeedClears = 10;

        /// <summary>打下一关需要前一关至少多少星</summary>
        public static int MinStarsToUnlockNextLevel = 1;

        // ---------- 奖励 ----------
        /// <summary>每星金币奖励</summary>
        public static int GoldPerStar = 50;
    }
}
