# well404.Shop

> WebPanel 中的玩家商店和管理员目录已迁移到 Shop 自建 UI：分组六列紧凑商品网格、快速数量、
> 库存、备注和拖动排序保持在插件内演进，简单插件仍可使用 WebPanel 描述符界面。

> Unturned / OpenMod 物品商店插件 —— 玩家用货币买卖物品,支持分组目录、批量售卖和按权限组的购买折扣。

`well404.Shop` 是 **well404 OpenMod 插件家族** 的商店模块。它通过全局 `IEconomyProvider`
结算交易,因此**依赖 [well404.Economy](https://www.nuget.org/packages/well404.Economy/)**
(安装本插件时会自动一并安装)。

## 功能

- 🛒 配置驱动的物品目录,支持双向定价和权限折扣
- 🗂️ 自定义商品分组、备注与顺序;旧配置自动生成 `default` 分组并补齐顺序
- 🌐 Web 管理面板按玩家视图分组预览,商品六列紧凑布局并支持组内拖拽排序
- 🎮 玩家网页商店显示实时库存;购买/出售弹窗提供 1、5、10、全部和自定义数量
- ⚡ 每个分组带紧凑的批量售卖按钮和二次确认;超量出售自动按实际持有量成交
- 📦 数量按背包中的独立物品实例计算；弹匣/弹药箱内部弹药量不会被误算成多件商品

## 安装

```
openmod install well404.Shop
```

> 本插件硬依赖 well404.Economy,`openmod install` 会自动解析并一并安装它。
> 安装或升级 DLL 后必须完整重启服务器；不要用 `openmod reload` 替换二进制。

## 命令

| 命令 | 别名 | 说明 |
| --- | --- | --- |
| `/buy <id> [数量]` | | 按**游戏物品 ID**购买;省略数量时默认为 1 |
| `/sell <id> [数量]` | | 按**游戏物品 ID**出售;省略数量时默认为 1 |
| `/shop` | `/market` | 列出全部商品及买卖价 |

## 配置 (config.yaml)

商品使用 `items` 配置，按游戏物品 ID 买卖，名称由游戏资源自动解析。
若服务器的原版或创意工坊物品目录旁安装了 `sChinese.dat` 等简体中文本地化文件，
网页语言为中文时，目录与检索会把中文名作为主标题，并在下一行用灰色小字显示英文名；
网页语言为英文时只显示英文名。未安装翻译时自动回退英文，不需要额外配置。
创意工坊存在重复数字物品 ID 时，商品名称与物品元数据以 Unturned 实际加载的资产为准，
不会因为目录枚举到另一个同 ID 资源而串名。

```yaml
discounts:
  enabled: false        # 是否启用按权限折扣
  tiers:                # 权限字符串 -> 购买价乘数(0 < m <= 1);玩家取其所持权限中最低(最优)乘数
    well404.shop.vip: 0.9
    well404.shop.mvp: 0.8

groups:
  - id: "default"
    name: "default"

items:                  # 出厂目录；名称由游戏按 ID 自动解析
  - { itemId: 14,  buyPrice: 10,   sellPrice: 4,   group: "default", note: "", order: 1 } # Bottled Water
  - { itemId: 13,  buyPrice: 15,   sellPrice: 6,   group: "default", note: "", order: 2 } # Canned Beans
  - { itemId: 116, buyPrice: 1000, sellPrice: 400, group: "default", note: "", order: 3 } # PDW
  - { itemId: 6,   buyPrice: 100,  sellPrice: 40,  group: "default", note: "", order: 4 } # Military Magazine
  - { itemId: 43,  buyPrice: 200,  sellPrice: 80,  group: "default", note: "", order: 5 } # Low Caliber Military Ammunition Crate
  - { itemId: 95,  buyPrice: 20,   sellPrice: 8,   group: "default", note: "", order: 6 } # Bandage
  - { itemId: 391, buyPrice: 30,   sellPrice: 12,  group: "default", note: "", order: 7 } # Vitamins
  - { itemId: 68,  buyPrice: 50,   sellPrice: 20,  group: "default", note: "", order: 8 } # Metal Sheet
```

折扣权限(如 `well404.shop.vip`)由服务器管理员自行授予权限组即可,无需在插件内声明。

## Web 管理面板

安装 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 后,Shop 会自动注册「商店」模块。管理页的商品目录直接采用与玩家页一致的分组标签和六列紧凑卡片，不再把“分组”和“添加商品”拆成独立的大页面。目录标题右侧可以新增或编辑当前分组；组名留空时自动使用组 ID。卡片可在当前分组内拖动排序，编辑弹窗可修改分组、备注与价格，玩家面板严格使用相同顺序。

目录下方的“添加物品”输入框可按游戏物品名称或数字 ID 实时检索，结果区同时显示物品 ID；点“添加”后在弹窗设置买价、卖价、分组和备注。

玩家输入 `/menu shop` 后可按二级分组标签浏览商品。卡片及交易弹窗都会显示当前库存;点购买/出售后可直接选择 1、5、10、全部,也可输入自定义数量。「全部购买」按当前余额计算最大数量;出售数量超过库存时自动缩减为实际持有量。

每个分组标签右侧有紧凑的「售卖本组全部」按钮,执行前必须二次确认。它批量回收该分组中 `sellPrice > 0` 的商品。

旧配置首次加载管理模块时会写回 `groups: default`,把缺少 `group` 的商品归入 `default`,并补齐空备注和稳定的全局 `order`。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店,分组买卖物品,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的实用指令：home/tp/warp/gift/sleep/back，经济收费可选 |

完整文档、配置示例与本地调试说明见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
