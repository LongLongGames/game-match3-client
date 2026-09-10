# Excel 配置源（唯一导表输入）

本目录与 `Assets` 同级，放所有 `.xlsx`。

## 工具

只使用 **Excel Config Compiler**（不再使用 BakingSheet / MessagePack / NuGet 导表链）：

- Unity 菜单：**Tools → Excel Config Compiler**
- 设置建议：
  - Excel 源目录：`Excel`（工程根）
  - 代码输出：`Assets/Scripts/HotUpdate/Config/Generated`
  - 二进制输出：`Assets/Bundles/Config`（或 Resources/Tables）
  - 命名空间：`HotUpdate.Config`

导表后生成 `Level.cs` + `Level.bytes`。运行时由 `ConfigLoader` 加载 `.bytes`。

## Excel 约定（ECC）

| 行 | 内容 |
|----|------|
| 第 1 行 | 字段名（PascalCase） |
| 第 2 行 | 类型（`int` / `string` / …） |
| 第 3 行起 | 数据 |

- **Worksheet 名** = 生成类型名（当前：`Level`）
- 第一列名为 `Id` 且类型为 `int` 时，生成 `LevelTable.Get(id)` 主键索引
- 忽略以 `~$` 开头的临时文件

## 当前表

`Level.xlsx`：关卡配置（`MapId` + `LevelId` 业务键；`Id` = MapId*100+LevelId）。
