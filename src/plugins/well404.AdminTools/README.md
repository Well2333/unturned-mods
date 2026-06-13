# well404.AdminTools

> Unturned / OpenMod 管理员工具 —— 无敌、踢出、临时封禁/解封、权限组(VIP 等)分配、按权限组授予指令；命令与 Web 面板双通道。

`well404.AdminTools` 是 **well404 OpenMod 插件家族** 的管理员侧插件。所有功能既有聊天/控制台命令，装了
[well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 时也会出现在管理面板里(中英双语，可切换)。

## 功能

- 🛡️ **无敌(godmode)**：让指定在线玩家免疫伤害(开关式，重启清除)。
- 👢 **踢出 / 封禁 / 解封**：踢出在线玩家；按时长临时封禁(留空=永久)；按 SteamID 解封。
- 🎖️ **权限组分配**：给玩家添加/移除权限组(如 VIP)，支持离线玩家(17 位 SteamID)。
- ⌨️ **权限组指令**：为某权限组授予/撤销某条指令(用「查找指令」查权限节点)，并可查看某组已有权限。

## 安装

```
openmod install well404.AdminTools
```

重启或 `openmod reload` 后生效。**不**强制依赖 WebPanel —— 没装也能用命令。

## 命令

| 命令 | 语法 | 说明 | 权限 |
| --- | --- | --- | --- |
| `/god` | `[玩家] [on\|off]` | 切换自己或某玩家的无敌 | `well404.AdminTools:god` |
| `/kick` | `<玩家> [原因]` | 踢出在线玩家 | `well404.AdminTools:kick` |
| `/ban` | `<玩家> [分钟] [原因]` | 封禁(分钟留空=永久) | `well404.AdminTools:ban` |
| `/unban` | `<SteamID>` | 解除封禁 | `well404.AdminTools:unban` |

> 权限组分配与「权限组指令」目前**仅在 Web 面板**操作(见下);命令侧后续可补。

## Web 面板

装了 WebPanel 后，注册「管理员工具」模块(🛡️)：在线玩家总览、无敌、踢出、封禁、解封、权限组列表、
玩家权限组增删、查找指令、为权限组授予/撤销指令、查看某组已有权限。所有标题/字段中英双语，随面板语言切换。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店 |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | Web 管理面板 + 玩家面板 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 玩家实用功能 |
| [well404.AdminTools](https://www.nuget.org/packages/well404.AdminTools/) | 管理员工具(无敌/踢封/权限组) |

完整文档见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
