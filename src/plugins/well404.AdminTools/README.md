# well404.AdminTools

> Unturned / OpenMod 管理员工具 —— 无敌、全身装备维修、踢出、临时封禁/解封；命令与 Web 面板双通道。

`well404.AdminTools` 是 **well404 OpenMod 插件家族** 的管理员侧插件。所有功能既有聊天/控制台命令，装了
[well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 时也会出现在管理面板里(中英双语，可切换)。

## 功能

- 🛡️ **无敌(godmode)**：让指定在线玩家免疫伤害(开关式，重启清除)。
- 🔧 **维修装备**：用 `/repair <玩家>` 将在线玩家身上、背包和枪械附件中所有有耐久的装备恢复到 100%。
- 👢 **踢出 / 封禁 / 解封**：踢出在线玩家；按时长临时封禁(留空=永久)；按 SteamID 解封。

## 安装

```
openmod install well404.AdminTools
```

安装或升级后请完整重启服务器。**不**强制依赖 WebPanel —— 没装也能用命令。

## 命令

| 命令 | 语法 | 说明 | 权限 |
| --- | --- | --- | --- |
| `/god` | `[玩家] [on\|off]` | 切换自己或某玩家的无敌 | `well404.AdminTools:commands.god` |
| `/repair` | `<玩家>` | 将指定在线玩家所有装备维修到 100% | `well404.AdminTools:commands.repair` |
| `/kick` | `<玩家> [原因]` | 踢出在线玩家 | `well404.AdminTools:commands.kick` |
| `/ban` | `<玩家> [分钟] [原因]` | 封禁(分钟留空=永久) | `well404.AdminTools:commands.ban` |
| `/unban` | `<SteamID>` | 解除封禁 | `well404.AdminTools:commands.unban` |

## Web 面板

装了 WebPanel 后，注册「管理员工具」模块(🛡️)：在线玩家总览、无敌、维修装备、踢出、封禁、解封。所有标题/字段
及结果消息中英双语，随面板语言切换。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店 |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | Web 管理面板 + 玩家面板 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 玩家实用功能 |
| [well404.AdminTools](https://www.nuget.org/packages/well404.AdminTools/) | 管理员工具(无敌/维修/踢出/封禁) |

完整文档见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
