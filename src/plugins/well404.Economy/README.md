# well404.Economy

> Unturned / OpenMod 货币经济插件 —— 以全局 `IEconomyProvider` 对外供币,后端可选 SQLite 事务账本或原生经验值。

`well404.Economy` 是 **well404 OpenMod 插件家族** 的经济核心。它实现 OpenMod 的
`IEconomyProvider` 抽象并注册为全局服务,任何其他插件(商店、签到等)都能注入它来收发货币。

## 功能

- 💰 玩家货币账户,余额查询与管理命令
- 🗄️ 两种后端:`database`(SQLite 单文件事务账本)或 `experience`(原生 Unturned 经验值)
- 🔁 `database` 后端支持原子玩家转账 `/pay`,可设最低额度与手续费税率
- ⚔️ 击杀奖励:玩家 / 僵尸 / 巨型僵尸 / 动物可分别配置奖励金额
- 🌐 可选 Web 管理面板集成(配合 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/))
- 🎮 可选玩家网页钱包:玩家 `/menu` 即可在浏览器里查看余额、向在线玩家转账

## 安装

```
openmod install well404.Economy
```

安装或升级 DLL 后必须完整重启服务器；不要用 `openmod reload` 替换二进制。

## 命令

| 命令 | 别名 | 说明 |
| --- | --- | --- |
| `/balance [玩家]` | `/bal`, `/money` | 查看自己或他人余额 |
| `/pay <玩家> <金额>` | | 向其他玩家原子转账(需 `database` 后端并在配置中开启) |
| `/eco give <玩家> <金额>` | | 管理:发放货币 |
| `/eco take <玩家> <金额>` | | 管理:扣除货币 |
| `/eco set <玩家> <金额>` | | 管理:设定余额 |

## 配置 (config.yaml)

```yaml
currency:
  name: "Credit"        # 货币名称(显示在消息中)
  symbol: "$"           # 货币符号
  startingBalance: 0    # 新账户初始余额(仅 database 后端)
backend: "database"     # database | experience（experience 下转账自动不可用）
# database 后端固定写入插件目录下的 economy.sqlite3
transfer:
  enabled: true         # 是否允许 /pay 转账
  minAmount: 1          # 单笔最低转账额
  taxPercent: 0         # 0-100,每笔转账抽税百分比
killRewards:
  enabled: true         # 击杀奖励总开关
  player: 0             # 击杀玩家奖励
  zombie: 0             # 击杀僵尸奖励
  megaZombie: 0         # 击杀巨型僵尸奖励
  animal: 0             # 击杀动物奖励
```

> 2.0.0 不读取或迁移旧 LiteDB `economy.db`，数据库后端会创建全新的 `economy.sqlite3`。
> `backend: experience` 时不会创建、读取或写入 SQLite；已有文件只为切回数据库后端保留。

`database` 后端还提供带唯一操作号的持久幂等扣款/退款能力，供仓库容量购买等跨插件流程使用。
同一操作号重复提交只会入账一次。`experience` 后端也可供在线玩家购买个人与小队仓库容量，但不提供跨重启幂等账本；若进程恰好在扣除经验值期间中断，Vault 会把不确定操作留在恢复隔离区，禁止自动猜测扣款或退款。经验值没有小数，相关余额、奖励和仓库扩容价格必须为整数；`/pay` 与 Shop 在该后端安全拒绝。

## Web 管理面板

安装 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 后,Economy 会自动注册「经济」模块:浏览并编辑所有玩家余额、配置货币信息、击杀奖励与转账参数。未安装面板时插件照常通过命令工作。

此外还会注册一个**面向玩家**的钱包菜单:玩家在游戏内输入 `/menu`(或 `/menu economy`)即可在浏览器里查看自己的余额、向任一在线玩家转账(沿用 `/pay` 的开关、最低额与税率)。

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
