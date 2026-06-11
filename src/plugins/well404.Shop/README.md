# well404.Shop

> Unturned / OpenMod 物品商店插件 —— 玩家用货币买卖物品与自定义组合包,支持按权限组的购买折扣。

`well404.Shop` 是 **well404 OpenMod 插件家族** 的商店模块。它通过全局 `IEconomyProvider`
结算交易,因此**依赖 [well404.Economy](https://www.nuget.org/packages/well404.Economy/)**
(安装本插件时会自动一并安装)。

## 功能

- 🛒 配置驱动的商品目录:单物品或自定义组合包(bundle)
- 💵 买入 / 卖出双向定价(价格设为 0 即表示不可买 / 不可卖)
- 🏷️ 基于权限的购买折扣分级(如 VIP 9 折、MVP 8 折,默认关闭)
- 🌐 可选 Web 管理面板:可视化编辑商品、按名称/ID 搜索游戏物品(配合 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/))

## 安装

```
openmod install well404.Shop
```

> 本插件硬依赖 well404.Economy,`openmod install` 会自动解析并一并安装它。
> 重启服务器或执行 `openmod reload` 后生效。

## 命令

| 命令 | 别名 | 说明 |
| --- | --- | --- |
| `/buy <id> [数量]` | | 从商店购买物品 / 组合包 |
| `/sell <id> [数量]` | | 把物品 / 组合包卖给商店 |
| `/shop` | `/market` | 列出全部商品及买卖价 |

## 配置 (config.yaml)

```yaml
discounts:
  enabled: false        # 是否启用按权限折扣
  tiers:                # 权限字符串 -> 购买价乘数(0 < m <= 1);玩家取其所持权限中最低(最优)乘数
    well404.shop.vip: 0.9
    well404.shop.mvp: 0.8
items:
  - id: medkit          # /buy /sell 使用的商店 ID
    name: "Medkit"
    type: item          # item(单物品)| bundle(组合包)
    itemId: 13          # Unturned 物品资源 ID(type: item)
    amount: 1           # 每购买单位的物品数量
    buyPrice: 50        # 买入价(0 = 不可买)
    sellPrice: 20       # 卖出价(0 = 不可卖)
  - id: starter
    name: "Starter Kit"
    type: bundle        # 组合包
    contents:
      - itemId: 13
        amount: 2
      - itemId: 81
        amount: 1
    buyPrice: 100
    sellPrice: 0
```

折扣权限(如 `well404.shop.vip`)由服务器管理员自行授予权限组即可,无需在插件内声明。

## Web 管理面板

安装 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 后,Shop 会自动注册「商店」模块:增删改商品目录、搜索游戏物品资源以快速填入物品 ID、配置折扣分级。未安装面板时插件照常通过命令工作。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店,买卖物品 / 组合包,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |

完整文档、配置示例与本地调试说明见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
