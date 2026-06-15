# well404.AdminTools

面向**管理员**的工具插件：godmode、踢出、临时封禁/解封。
每项功能都有命令；装了 [`well404.WebPanel`](well404.WebPanel.md) 时也进管理面板(中英双语，随面板语言切换)。

## 依赖

- **无硬依赖**。没装 WebPanel 也能用命令；权限/封禁走 OpenMod 与 Unturned 原生系统。

## 命令

| 命令 | 语法 | 命令权限 | 说明 |
| --- | --- | --- | --- |
| `/god` | `[玩家] [on\|off]` | `well404.AdminTools:god` | 切换无敌(无玩家参数时作用于自己)。开关式，重启清除 |
| `/kick` | `<玩家> [原因]` | `well404.AdminTools:kick` | 踢出在线玩家 |
| `/ban` | `<玩家> [分钟] [原因]` | `well404.AdminTools:ban` | 封禁玩家；分钟留空=永久 |
| `/unban` | `<SteamID>` | `well404.AdminTools:unban` | 按 SteamID 解封 |

## Web 面板模块「管理员工具」(🛡️)

| 动作 | 类型 | 说明 |
| --- | --- | --- |
| 在线玩家 | 表 | 当前在线玩家(名字、SteamID、是否无敌) |
| 无敌 | 表单 | 对在线玩家开/关无敌 |
| 踢出 / 封禁 / 解封 | 表单 | 踢出、按分钟封禁(留空=永久)、按 SteamID 解封 |

> 面板结果消息(如「已为 X 开启无敌」)随面板语言本地化。

## 实现要点(给开发者)

- **无敌**:`GodModeService` 维护一组 SteamID,`GodModeDamageListener`(`IEventListener<UnturnedPlayerDamagingEvent>`)
  在受伤前取消伤害。内存态,重启清除。
- **封禁/解封**:封禁走 OpenMod `IUserManager.BanAsync(user, reason, endTime?)`;解封走 SDG `SteamBlacklist.unban`。
- **消息本地化**:`AdminResult` 携带英文模板 + 参数,既能在游戏内格式化为英文,也能在 Web 面板按语言用
  `IWebTranslationRegistry` 本地化。
- **配置**:本插件无可配置项(无敌为内存态),因此 `config.yaml` 为空壳。

## 本地构建与调试

见 [docs/README](README.md#本地构建与调试)。
