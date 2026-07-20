# EItemType 两级物品筛选研究

> 状态：2026-07-19 已按研究结论实现。本文只使用 Unturned 原生 `EItemType`，不根据物品名称、
> 文件夹、配方或用途猜测标签。此前的“物品多标签”研究已因前提错误撤回。

## 可靠的数据来源

`ItemAsset.type` 是游戏给物品设置的唯一原生类型，类型由 `EItemType` 枚举限定。每件物品只有一个
`EItemType`，原版和创意工坊物品共用这套字段。

配方的 `CategoryTagRef`、`Operation`、输入和产出不属于物品类型。本筛选不会读取这些字段，尤其
不会再把带 Ammo 或 `FillTargetItem` 配方的 `Supply` 改成弹药。

## 两级结构

第一行继续使用项目已经存在的大类，只负责缩短导航；第二行显示准确的原生类型。大类不是新的
游戏标签，也不会改变物品的原生类型。

| 现有大类 | 包含的原生 EItemType |
| --- | --- |
| 弹药 | `MAGAZINE` |
| 食物与饮品 | `FOOD`, `WATER` |
| 药品 | `MEDICAL` |
| 武器 | `GUN`, `MELEE`, `THROWABLE`, `CHARGE`, `DETONATOR` |
| 材料 | `SUPPLY` |
| 工具 | `TOOL`, `FISHER`, `FUEL`, `REFILL`, `FILTER`, `MAP`, `KEY`, `VEHICLE_LOCKPICK_TOOL`, `VEHICLE_PAINT_TOOL`, `VEHICLE_REPAIR_TOOL` |
| 服装 | `SHIRT`, `PANTS`, `VEST`, `HAT`, `BACKPACK`, `MASK`, `GLASSES` |
| 配件 | `OPTIC`, `SIGHT`, `GRIP`, `TACTICAL`, `BARREL` |
| 建筑 | `BARRICADE`, `STRUCTURE`, `STORAGE`, `TRAP`, `SENTRY`, `GENERATOR`, `BEACON`, `FARM`, `GROWER`, `OIL_PUMP` |
| 载具用品 | `TIRE`, `TANK` |
| 其他 | `CLOUD`, `BOX`, `ARREST_START`, `ARREST_END`, `LIBRARY`, `COMPASS`，以及未来未知类型 |

这张表保持修改前的大类归属，不在本次工作中重新解释物品用途。小类名称和值始终来自
`ItemAsset.type`。

## 界面行为

- 第一行“大类”显示当前背包或仓库中实际存在的大类和件数。
- 第二行“原生类型”只显示第一行范围内实际存在的 `EItemType` 和件数。
- 中文界面在常见类型译名后同时显示原始枚举值；不明确的类型直接显示枚举值，避免翻译成错误用途。
- 选择“全部”大类时，第二行可以直接筛选当前列表里的所有原生类型。
- 选择新的大类时，小类自动回到“全部类型”，避免旧小类把新大类内容全部隐藏。
- 大类和小类选择都保存在当前浏览器会话中；切换个人/小队或背包/仓库时一起重置。
- 未知的新 `EItemType` 会显示原始枚举文本并落入“其他”，不会从物品列表消失。

## 与现有功能的关系

稀有度颜色和排序、总占用格数排序、物品 ID/数量/名称排序、背包来源容器开关、个人与小队仓库
切换均保持原样。本次只替换物品类别筛选，不重新研究或修改这些功能。
