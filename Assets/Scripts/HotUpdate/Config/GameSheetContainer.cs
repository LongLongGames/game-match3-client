using Cathei.BakingSheet;
using Microsoft.Extensions.Logging;

namespace HotUpdate.Config
{
    /// <summary>
    /// 客户端/服务端共用的表容器。
    /// 属性名必须与 Excel 工作表名（或 JSON 文件名）一致。
    /// 新增表时在此增加对应 Sheet 属性，并在 Excel 中增加同名 Sheet。
    /// </summary>
    public class GameSheetContainer : SheetContainerBase
    {
        public GameSheetContainer(ILogger logger) : base(logger)
        {
        }

        public LevelSheet Level { get; private set; }
    }
}
