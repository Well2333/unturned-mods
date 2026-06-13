# well404.Shop

> Unturned / OpenMod 物品商店插件 —— 玩家用货币买卖物品与自定义组合包,支持按权限组的购买折扣。

`well404.Shop` 是 **well404 OpenMod 插件家族** 的商店模块。它通过全局 `IEconomyProvider`
结算交易,因此**依赖 [well404.Economy](https://www.nuget.org/packages/well404.Economy/)**
(安装本插件时会自动一并安装)。

## 功能

- 🛒 配置驱动的商品目录:**普通商品**(按游戏物品 ID 买卖,名称自动解析)与**礼包**(自定义命名组合包)分开管理
- 💵 买入 / 卖出双向定价(价格设为 0 即表示不可买 / 不可卖)
- 🏷️ 基于权限的购买折扣分级(如 VIP 9 折、MVP 8 折,默认关闭)
- 🌐 可选 Web 管理面板:普通商品 / 礼包分块编辑、按名称/ID 搜索游戏物品并**一键加入商店**(配合 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/))
- 🎮 可选玩家网页商店:玩家 `/menu` 即可在浏览器里(分区列表)浏览商品并买卖(沿用同一折扣/结算逻辑)

## 安装

```
openmod install well404.Shop
```

> 本插件硬依赖 well404.Economy,`openmod install` 会自动解析并一并安装它。
> 重启服务器或执行 `openmod reload` 后生效。

## 命令

| 命令 | 别名 | 说明 |
| --- | --- | --- |
| `/buy <id> [数量]` | | 购买:普通商品填**物品 ID**,礼包填**礼包 id** |
| `/sell <id> [数量]` | | 出售:普通商品填**物品 ID**,礼包填**礼包 id** |
| `/shop` | `/market` | 列出全部商品及买卖价 |

## 配置 (config.yaml)

商品分两类:**普通商品**(`items`,按游戏物品 ID 买卖,名称自动解析,只需买卖价)与**礼包**(`bundles`,
命名组合包,有自己的 id 与内容)。

```yaml
discounts:
  enabled: false        # 是否启用按权限折扣
  tiers:                # 权限字符串 -> 购买价乘数(0 < m <= 1);玩家取其所持权限中最低(最优)乘数
    well404.shop.vip: 0.9
    well404.shop.mvp: 0.8

items:                  # 普通商品:用游戏物品 ID 买卖,显示名自动解析
  - itemId: 15          # 游戏物品 ID;/buy 15 /sell 15 即用它
    buyPrice: 100       # 买入价(0 = 不可买)
    sellPrice: 40       # 卖出价(0 = 不可卖)

bundles:                # 礼包:命名组合包,用自己的 id 买卖
  - id: "starter"       # 礼包 id(避免起成纯数字,以免与物品 ID 混淆)
    name: "Starter Kit"
    buyPrice: 100
    sellPrice: 0        # 0 = 不可整包出售
    contents:
      - itemId: 15
        amount: 2
      - itemId: 81
        amount: 1
```

折扣权限(如 `well404.shop.vip`)由服务器管理员自行授予权限组即可,无需在插件内声明。

## Web 管理面板

安装 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 后,Shop 会自动注册「商店」模块,
**普通商品与礼包分块管理**:普通商品(物品 ID + 买卖价)、礼包(id + 名称 + 内容 + 买卖价)、搜索游戏物品并
**一键加入商店**、配置折扣分级。未安装面板时插件照常通过命令工作。

此外还会注册一个**面向玩家**的商店菜单:玩家在游戏内输入 `/menu`(或 `/menu shop`)即可在浏览器里(分区
列表)浏览全部商品、点按钮购买或出售,沿用与 `/buy`、`/sell` 完全一致的折扣、扣费与失败退款逻辑。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店,买卖物品 / 组合包,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的实用指令：home/tp/warp/gift/sleep/back，经济收费可选 |

完整文档、配置示例与本地调试说明见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
