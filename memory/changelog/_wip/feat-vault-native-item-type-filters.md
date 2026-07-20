---
date: 2026-07-19
branch: feat/team-vault
merge_commit: pending
type: feature
scope: well404.UnturnedMods.Shared, well404.Vault
guideline_changed: false
---

# Vault 使用 EItemType 两级物品筛选

## 做了什么

- 物品大类现在只由 `ItemAsset.type` 决定，移除 Supply 的 Ammo/FillTargetItem 配方覆盖。
- Vault 玩家页把原有单行类别替换为两行联动筛选：第一行是既有大类，第二行是当前范围内实际存在的
  原生 `EItemType`。
- 原生类型筛选拥有独立会话状态；切换大类时自动回到全部类型，未知未来类型显示原始枚举文本。
- 保留既有稀有度、排序、身上容器、个人/小队与背包/仓库筛选行为。

## 为什么

- 游戏物品只有一个原生 `EItemType`。配方分类和操作不属于物品分类，不能用来改写物品类型。
- 单行大类方便快速浏览，但会隐藏同一大类内的准确类型；第二行可以在不引入猜测标签的前提下继续细分。

## 影响范围

- `src/Shared/UnturnedMods.Shared/Items/LocalizedItemCatalog.cs`
- `src/plugins/well404.Vault/player-ui.js`
- `src/plugins/well404.Vault/player-ui.css`
- Vault 相关测试与文档

## 验证

- JavaScript 语法检查通过。
- 全解决方案 Debug 构建通过，0 warning、0 error。
- 全解决方案 Release 测试通过，197/197；测试中发现并修复了展示层辅助函数在纯 .NET 测试里误加载
  `Assembly-CSharp` 的隔离问题，Vault 复测 55/55。
- Shared 1.4.0、Vault 2.5.0、WebPanel 1.5.1 在本地专用测试服冷启动成功，插件注册正常、地图加载
  100%，无插件异常或程序集加载错误。
- 使用 8 条临时原版物品验证玩家 API：大类计数与 `EItemType` 计数一致；枪械/近战在武器大类下分别
  显示为 `GUN`/`MELEE`，ID 43 与 68 均保持材料大类并显示原生 `SUPPLY`。
- 真实 Chrome 验证两行筛选联动、`GUN`/`MELEE` 切换、自动刷新与整页重载状态保持；1440、390、
  330 像素宽度无页面横向溢出，控制台错误、网络失败与 HTTP 错误均为 0。
- 临时数据库已用测试前备份恢复，物品数与已用容量均为 0；测试服、Chrome 和测试端口均已关闭。
