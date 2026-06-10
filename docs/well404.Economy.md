# well404.Economy

为服务器提供货币系统，并以 OpenMod 标准抽象 `IEconomyProvider` 暴露给**其他插件**
（如每日签到）调用。支持玩家间转账、击杀奖励，货币既可存于 serverless 数据库，也可
直接复用 Unturned 的经验值。

## 安装

```bash
openmod install well404.Economy
openmod reload
```

或手动部署（见 [docs/README](README.md#本地构建与调试)）。

## 配置（`config.yaml`）

首次加载后生成于 `openmod/plugins/well404.Economy/config.yaml`：

```yaml
currency:
  name: "Credit"        # 货币名
  symbol: "$"           # 货币符号
  # 从未有过记录的账户的初始余额（仅 database 后端）
  startingBalance: 0

# 货币存储后端：
#   database   - serverless LiteDB 单文件账本（在线/离线玩家均可）
#   experience - 直接用原生 Unturned 经验值（仅在线玩家）
backend: "database"

database:
  # LiteDB 文件，生成在本插件目录下
  fileName: "economy.db"

transfer:
  enabled: true         # 是否允许 /pay 转账
  minAmount: 1          # 单次最小转账额
  taxPercent: 0         # 转账税（0-100，从转出额扣除）

# 击杀奖励货币。某项为 0 即关闭该来源。
killRewards:
  enabled: true
  player: 0             # 击杀其他玩家
  zombie: 0             # 普通僵尸
  megaZombie: 0         # 巨型/Boss 僵尸
  animal: 0             # 动物
```

### 两种货币后端

| 后端 | 说明 | 离线玩家 |
| --- | --- | --- |
| `database` | LiteDB 单文件账本，键 `ownerType:ownerId`，并记录交易流水。 | 支持 |
| `experience` | 余额即玩家的 Unturned 经验值；转账/买卖直接增减 XP。 | **不支持**（在线才有 XP） |

> 切换 `backend` 后两套余额各自独立（DB 账本与 XP 是两个数）。`experience` 模式下对
> 离线玩家的操作会返回「玩家需在线」的提示。

## 命令

| 命令 | 说明 | 权限 |
| --- | --- | --- |
| `/balance [玩家]` | 查看自己或他人余额。别名 `/bal`、`/money`。 | `well404.Economy:commands.balance` |
| `/pay <玩家> <金额>` | 给其他玩家转账（仅玩家可用）。 | `well404.Economy:commands.pay` |
| `/eco give <玩家> <金额>` | 管理：发放货币。 | `well404.Economy:commands.eco.give` |
| `/eco take <玩家> <金额>` | 管理：扣除货币。 | `well404.Economy:commands.eco.take` |
| `/eco set <玩家> <金额>` | 管理：设定余额。 | `well404.Economy:commands.eco.set` |

- 「玩家」可填在线玩家的名字或 SteamID；管理命令也接受**离线玩家的 17 位 SteamID**
  （database 后端）。
- 余额不足时（`/pay`、扣款）会给出友好提示，不会变成负数。

示例：

```
/balance
/pay Nelson 250
/eco give 76561198000000000 1000
```

## 给其他插件用：注入 `IEconomyProvider`

任意插件都可注入 `IEconomyProvider` 来增减玩家货币（例如签到、任务奖励、PVP 赏金）。
**不需要**引用 well404.Economy，只引用共享抽象包即可。

`.csproj`：

```xml
<PackageReference Include="OpenMod.Extensions.Economy.Abstractions" />
```

代码（一个发放签到奖励的命令）：

```csharp
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;

[Command("daily")]
public class CommandDaily : Command
{
    private readonly IEconomyProvider m_Economy;

    public CommandDaily(IServiceProvider serviceProvider, IEconomyProvider economy)
        : base(serviceProvider)
    {
        m_Economy = economy;
    }

    protected override async Task OnExecuteAsync()
    {
        var user = (UnturnedUser)Context.Actor;
        // ownerId = SteamID 字符串；ownerType = "player"
        var newBalance = await m_Economy.UpdateBalanceAsync(
            user.Id, user.Type, 100m, reason: "daily_reward");
        await PrintAsync($"Daily reward! New balance: {m_Economy.CurrencySymbol}{newBalance}");
    }
}
```

`IEconomyProvider` 关键方法：

- `Task<decimal> GetBalanceAsync(string ownerId, string ownerType)`
- `Task<decimal> UpdateBalanceAsync(string ownerId, string ownerType, decimal changeAmount, string? reason)`
  —— 余额会变负时抛 `NotEnoughBalanceException`。
- `Task SetBalanceAsync(string ownerId, string ownerType, decimal balance)`
- `string CurrencyName` / `string CurrencySymbol`

> 服务器上**只应启用一个**经济提供方（well404.Economy 或其他实现 `IEconomyProvider`
> 的插件），否则解析会按优先级二选一。

## 文案（`translations.yaml`）

所有面向玩家的文本都在 `translations.yaml`，可自行汉化/改写。占位符用 SmartFormat，
如 `{symbol}`、`{balance}`、`{player}`。
