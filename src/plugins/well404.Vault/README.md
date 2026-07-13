# well404.Vault

> Vault 的玩家仓库与管理员容量页已迁移到插件自建 Web UI。背包/仓库与容量/玩家覆盖使用顶部标签切换；物品使用六列紧凑等高网格，长名称自动换行。所有存取权限与原指令保持一致。

> Unturned / OpenMod 私人仓库插件 —— 玩家把背包里的物品存入 / 取出个人仓库,容量按背包格子数计算。

`well404.Vault` 是 **well404 OpenMod 插件家族** 的私人仓库模块。它**无硬依赖**,可独立运行;
装了 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 时还会提供网页存取界面与容量设置。

## 功能

- 🧳 每位玩家一个**私人仓库**,通过指令或网页存入 / 取出背包物品
- 🎒 **完整保真**:保留物品耐久、品质、枪械配件、弹匣/弹药箱内的弹药等原始状态,取出原样还原
- 📐 容量按**背包格子数**计:每个物品按其网格尺寸 `宽×高` 占用(如 2×2 弹药箱 = 4 格),
  物品内部的堆叠 / 弹药数**不计入**(一盒 39 发的弹药箱仍只占其格子,不算 39 个)
- 👤 **按玩家容量**:基础容量 + 按权限的容量档位(VIP 更大),还能为单个玩家单独覆盖
- 🔎 网页仓库**按属性合并 + 弹窗详情**:完全相同的多件合并为一张卡;不同属性的同种物品(如 5 发/8 发弹药盒)
  默认收起为汇总卡,点击后在弹窗内展示逐个变体,弹药/堆叠以**装填比例**(如 `6/8`)展示;物品以六列紧凑等高网格排布，长名称自动换行,
  单件给一个**存入/取出**按钮、多件给**全部 / 数量 / 一件**(数量弹出空白框、需自行输入,标题标注范围 `1–N`,与「全部」「一件」都不同)
- 🌐 可选 Web 面板:玩家网页仓库 + 管理员配置容量(基础 / 档位 / 单独玩家)

## 安装

```
openmod install well404.Vault
```

安装或升级 DLL 后必须完整重启服务器；不要用 `openmod reload` 替换二进制。无硬依赖。

## 命令

| 命令 | 别名 | 说明 |
| --- | --- | --- |
| `/vault` | `/storage` | 显示用法 |
| `/vault store <物品ID> [数量]` | `deposit` | 把背包里该物品(按游戏物品 ID)存入仓库 |
| `/vault take <物品ID> [数量]` | `withdraw` | 把仓库里该物品取回背包(背包满则掉落脚下) |
| `/vault list` | `info` | 列出仓库内容与占用 |

> 装了 WebPanel 时,`/menu vault` 可打开网页仓库(背包 / 仓库两区,点按钮存取)。

- **权限**:命令按 OpenMod 路径派生权限(`well404.Vault:commands.vault[.store|.take|.list]`);**网页仓库的存 / 取
  强制相同权限**,缺权限则网页只读,无法绕过。
- **只存背包**:手持的主 / 副武器槽物品不会被存入,需先放进背包。

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
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 分组物品商店,买卖物品,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的实用指令：home/tp/warp/gift/sleep/back/party，经济收费可选 |
| [well404.Vault](https://www.nuget.org/packages/well404.Vault/) | 私人仓库,存取背包物品(完整保真,按格子计容量) |

完整文档见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
