# Excel 配置源（唯一导表输入）

与 `Assets` 同级。工具：**Tools → Excel Config Compiler**。

建议配置（`ExcelConfigSettings.asset`）：

| 项 | 值 |
|----|-----|
| Excel 源目录 | `Excel`（工程根相对，Assets 外） |
| 代码输出 | `Assets/Scripts/HotUpdate/Config/Generated` |
| 二进制输出 | `Assets/Bundles/Config` |
| 命名空间 | `HotUpdate.Config` |

## 表格式

第 1 行字段名，第 2 行类型，第 3 行起数据。Worksheet 名 = 生成类型名。

## 当前表

### Level.xlsx

每图 10 关（`LevelsPerMap = 10`）。`Id = MapId * 100 + LevelId`。

字段：Id, MapId, LevelId, MaxSteps, StepsFor3Stars, StepsFor2Stars, BoardWidth, BoardHeight, Goal, GoalValue

### Item.xlsx（道具）

| Id | Name | Effect | 说明 |
|----|------|--------|------|
| 1 | 锤子 | ClearOne | 消一格 |
| 2 | 横消 | ClearRow | 消一行 |
| 3 | 竖消 | ClearCol | 消一列 |
| 4 | 九宫格炸弹 | Bomb3x3 | 3×3 |
| 5 | 洗牌 | Shuffle | 重排 |
| 6 | 加五步 | ExtraSteps | Param=步数 |

### CheckInReward.xlsx（签到）

按 `Day` 1–7。`Energy` 体力；最多 3 个道具槽 `ItemIdN` + `ItemCountN`（0 表示空）。

### GameRules.xlsx（全局规则）

单行配置（取 `Id=1`）。与服务端 `config/GameRules.json` 对齐。

| 字段 | 类型 | 说明 | 当前值 |
|------|------|------|--------|
| Id | int | 配置行 Id | 1 |
| EnergyMax | int | 体力上限 | 30 |
| EnergyCostPerPlay | int | 单场扣除体力 | 1 |
| EnergyRegenSeconds | int | 恢复 1 点所需秒数 | 300 |
| LevelsPerMap | int | 每张地图关卡数 | 10 |
| MapUnlockClearCount | int | 当前图通关数 ≥ N 解锁下一图 | 5 |
| GoldPerStar | int | 通关金币 = 星数 × 此值 | 50 |
| MinStarsToUnlockNextLevel | int | 打下一关需要前一关至少星数 | 1 |
| ValidateLevelExists | bool | 通关时校验关卡是否在表内 | true |
| ValidateStepsAgainstConfig | bool | 通关时校验步数是否超 MaxSteps | true |
| Desc | string | 备注 | — |

运行时：`LevelConfigTable` / `ItemConfigTable` / `CheckInRewardConfigTable` / `GameRuleConfig`（由 `ConfigLoader` 加载 `GameRules.bytes` 后覆盖默认值）。
