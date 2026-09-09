# Excel 配置源（唯一导表输入）

- 本目录与 `Assets` 同级，放所有 `.xlsx`。
- 在 Unity 菜单 **Match3 → 导表 → Export Config (Excel → JSON)** 执行导表。
- 输出：
  - 客户端：`Assets/Bundles/Config/*.json`
  - 服务端：同级目录 `../game-match3-server/config/*.json`

## 约定

1. 工作表名（Sheet tab）必须与 `GameSheetContainer` 中的属性名一致（当前：`Level`）。
2. 第一行是表头，列名与 `SheetRow` 属性名一致；`Id` 必填。
3. 以 `$` 开头的列视为注释，会被忽略。
4. 忽略以 `~$` 开头的临时文件。

## 示例

已提供 `Level.xlsx`，对应 `HotUpdate.Config.LevelSheet`。
