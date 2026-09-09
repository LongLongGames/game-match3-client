using Cathei.BakingSheet;

namespace HotUpdate.Config
{
    /// <summary>
    /// 关卡表。Excel 工作表名需为 Level（与属性名一致）。
    /// Id 建议用 "{MapId}_{LevelId}"。
    /// </summary>
    public class LevelSheet : Sheet<LevelSheet.Row>
    {
        public class Row : SheetRow
        {
            public int MapId { get; private set; }
            public int LevelId { get; private set; }
            public int MaxSteps { get; private set; }
            public int StepsFor3Stars { get; private set; }
            public int StepsFor2Stars { get; private set; }
            public int BoardWidth { get; private set; } = 8;
            public int BoardHeight { get; private set; } = 8;
            public string Goal { get; private set; } = "score";
            public int GoalValue { get; private set; }
        }
    }
}
