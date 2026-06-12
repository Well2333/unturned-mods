# well404.Essentials

> Essentials —— 面向玩家的实用指令合集：回家、传送、传送点、礼包、睡觉投票、回到死亡点。

`well404.Essentials` 是 **well404 OpenMod 插件家族** 的一员。它面向**玩家**而非管理员，
把生存常用的便利指令集中到一个插件里，并为所有传送提供统一的「预热静止 + 可选经济收费 +
冷却」体验。

## 功能

- **/home**：回到你设置的家；`/home set` 把当前位置设为家（单个家）。
- **/tp <玩家>**：传送到某玩家。**同队**直接传送；**不同队**则发出请求，对方用
  `/tpa` 接受或 `/tpd` 拒绝（请求会超时）。
- **/warp**：传送点。`/warp <名称>` 传送，`/warp set <名称> [冷却]` 新建，
  `/warp delete <名称>` 删除，`/warps` 列出你有权限使用的传送点（指令格式参考 NewEssentials）。
- **/gift**：免费礼包。`/gift` 列出你可领取的，`/gift <id>` 领取。每个礼包可设
  **crontab 刷新规则**与**可选权限**（做 VIP 专属礼包）。
- **/sleep**：睡觉投票。在线玩家中达到设定比例（默认半数）即在白天/黑夜之间切换。
- **/back**：回到上次死亡的地点，并获得可配置秒数的无敌时间。

**传送通用规则**（home/tp/warp/back 共用）：传送前需静止预热若干秒（默认 5s，移动则取消）、
可设每条命令的冷却、以及**可选的经济费用**——仅当装了任一 `IEconomyProvider`（如
`well404.Economy`）时才扣费，默认全为 0（免费）。

装了 `well404.WebPanel` 时，上述所有设置项（传送规则、费用、tpa/sleep/back、传送点、礼包）
都可在 Web 界面里编辑。

## 安装

```
openmod install well404.Essentials
```

重启服务器或执行 `openmod reload` 后生效。Essentials **不**强制依赖经济或面板插件——
没有它们也能正常工作（传送免费、无 Web 界面）。

## 命令

| 命令 | 别名 | 语法 | 说明 |
| --- | --- | --- | --- |
| `/home` | | | 传送回家 |
| `/home set` | | | 把当前位置设为家 |
| `/tp` | | `<玩家>` | 传送到玩家（同队直传，跨队发请求） |
| `/tpa` | | `[玩家]` | 接受传送请求（最新的，或指定玩家的） |
| `/tpd` | | `[玩家]` | 拒绝传送请求 |
| `/warp` | | `<名称>` | 传送到传送点 |
| `/warp set` | | `<名称> [冷却秒]` | 新建传送点（管理） |
| `/warp delete` | `del` | `<名称>` | 删除传送点（管理） |
| `/warps` | | | 列出你能用的传送点 |
| `/gift` | | `[id]` | 列出可领礼包 / 领取指定礼包 |
| `/sleep` | | | 投票切换昼夜 |
| `/back` | | | 回到死亡点并短暂无敌 |

## 权限

OpenMod 会按命令路径自动派生命令权限（如 `well404.Essentials:home`、
`well404.Essentials:warp.set`）。此外本插件使用这些自定义权限节点：

| 权限 | 作用 |
| --- | --- |
| `well404.essentials.warps.<名称>` | 使用某个传送点（每个传送点单独授权） |
| `well404.essentials.cooldown.exempt` | 免除传送冷却 |
| 礼包的 `permission` 字段（自定义） | 领取该 VIP 专属礼包 |

## 配置 (config.yaml)

```yaml
teleport:
  warmupSeconds: 5        # 传送前静止秒数，0=瞬移
  cancelOnMove: true      # 预热期间移动则取消
  moveThreshold: 0.5      # 移动判定阈值（米）
  cooldownSeconds: 0      # 成功传送后的冷却，0=无
  costs:                  # 经济费用（需经济插件，默认 0=免费）
    home: 0
    tp: 0
    warp: 0
    back: 0
tpa:
  expirationSeconds: 30   # /tp 请求超时
back:
  invincibilitySeconds: 5 # /back 落地后的无敌秒数
sleep:
  enabled: true
  requiredRatio: 0.5      # 通过所需的在线玩家比例
warps: []                 # /warp set 或 WebUI 维护
gifts: []                 # 礼包：id/name/permission/cron/items
```

`gifts` 示例：

```yaml
gifts:
  - id: "daily"
    name: "Daily Supply"
    permission: ""          # 留空=所有人
    cron: "0 0 * * *"       # 每天 0 点刷新（服务器本地时间）
    items:
      - itemId: 15
        amount: 2
  - id: "vip"
    name: "VIP Weekly Crate"
    permission: "well404.essentials.gift.vip"
    cron: "0 0 * * 1"       # 每周一刷新
    items:
      - itemId: 81
        amount: 1
```

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店,买卖物品 / 组合包,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的实用指令：home/tp/warp/gift/sleep/back，经济收费可选 |

完整文档见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
