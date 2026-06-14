# well404.Vault

> Unturned / OpenMod 私人仓库插件 —— 玩家把背包里的物品存入 / 取出个人仓库,容量按背包格子数计算。

`well404.Vault` 是 **well404 OpenMod 插件家族** 的私人仓库模块。它**无硬依赖**,可独立运行;
装了 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 时还会提供网页存取界面与容量设置。

## 功能

- 🧳 每位玩家一个**私人仓库**,通过指令或网页存入 / 取出背包物品
- 🎒 **完整保真**:保留物品耐久、品质、枪械配件、弹匣/弹药箱内的弹药等原始状态,取出原样还原
- 📐 容量按**背包格子数**计:每个物品按其网格尺寸 `宽×高` 占用(如 2×2 弹药箱 = 4 格),
  物品内部的堆叠 / 弹药数**不计入**(一盒 39 发的弹药箱仍只占其格子,不算 39 个)
- 👤 **按玩家容量**:基础容量 + 按权限的容量档位(VIP 更大),还能为单个玩家单独覆盖
- 🔎 网页仓库**按属性合并 + 可折叠详情**:完全相同的多件合并为一行;不同属性的同种物品(如 5 发/8 发弹药盒)
  折叠成卡片,点「详情」就地展开逐个变体;每行都有**全部 / 数量 / 一件**三种存取
- 🌐 可选 Web 面板:玩家网页仓库 + 管理员配置容量(基础 / 档位 / 单独玩家)

## 安装

```
openmod install well404.Vault
```

重启服务器或执行 `openmod reload` 后生效。无硬依赖。

## 命令

| 命令 | 别名 | 说明 |
| --- | --- | --- |
| `/vault` | `/storage` | 显示用法 |
| `/vault store <物品ID> [数量]` | `deposit` | 把背包里该物品(按游戏物品 ID)存入仓库 |
| `/vault take <物品ID> [数量]` | `withdraw` | 把仓库里该物品取回背包(背包满则掉落脚下) |
| `/vault list` | `info` | 列出仓库内容与占用 |

> 装了 WebPanel 时,`/menu vault` 可打开网页仓库(背包 / 仓库两区,点按钮存取)。

## 配置 (config.yaml)

```yaml
# 基础容量(背包格子数)。每个物品按其网格尺寸(宽×高)占用;内部堆叠/弹药数不计入。
maxSlots: 200
# 按权限的容量档位;玩家取「基础与其拥有的各档位」中的最大值。
tiers: {}
#   "well404.vault.size.vip": 400
```

装了 WebPanel 时,基础容量、权限档位、以及**单独玩家容量**都可在管理面板「仓库」模块里编辑。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店,买卖单品 / 礼包,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的实用指令：home/tp/warp/gift/sleep/back/party，经济收费可选 |
| [well404.Vault](https://www.nuget.org/packages/well404.Vault/) | 私人仓库,存取背包物品(完整保真,按格子计容量) |

完整文档见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
