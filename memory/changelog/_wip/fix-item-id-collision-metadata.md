---
date: 2026-07-16
branch: fix/item-id-collision-metadata
merge_commit: pending
type: fix
scope: well404.UnturnedMods.Shared, well404.Vault, well404.Shop
guideline_changed: false
---

# 修复创意工坊重复物品 ID 导致的名称与稀有度串项

## 做了什么

- 共享物品目录先按数字 ID 聚合 OpenMod 暴露的所有资源，再以 Unturned
  `Assets.find(EAssetType.ITEM, id)` 的实际解析结果选择名称、翻译、稀有度、类型、分类和耐久语义。
- 补充 `Schinese.dat` 大小写变体，使 Linux 环境也能读取部分创意工坊简体中文文件。
- 同步更新 Vault 与 Shop 文档，说明重复旧式数字 ID 的处理规则。

## 为什么

- `China Assets` 创意工坊包把基础工作台、Hyper Defense Toolkit 和 Portable Kitchen 都声明为
  ID `60208`。原实现按物品目录枚举顺序保留第一项，导致游戏实际解析为基础工作台时，Vault
  却显示 Hyper Defense Toolkit 和传说稀有度。

## 影响的文件 / 范围

- `src/Shared/UnturnedMods.Shared/Items/LocalizedItemCatalog.cs`
- `src/plugins/well404.Vault/README.md`
- `docs/well404.Vault.md`
- `src/plugins/well404.Shop/README.md`
- `docs/well404.Shop.md`

## 注意事项 / 后续

- 无需迁移或修改 Vault SQLite；存储格式仍是游戏原生的 16 位物品 ID。
- Release 全解决方案构建通过，0 warning、0 error；按用户测试节奏未运行测试套件或服务器。
