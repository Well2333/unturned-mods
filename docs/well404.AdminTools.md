# well404.AdminTools

面向**管理员**的工具插件：godmode、踢出、临时封禁/解封、权限组分配、按权限组授予指令。
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
| 权限组 | 表 | 列出所有权限组(id、名称、优先级) |
| 玩家权限组 | 表单 | 给玩家添加/移除权限组(在线用名字，离线用 17 位 SteamID) |
| 查找指令 | 检索 | 按名称/ID 模糊查已注册指令,得到其**权限节点** |
| 权限组指令 | 表单 | 为某权限组**授予/撤销**某条指令(填指令ID或权限节点) |
| 权限组已有指令 | 检索 | 输入权限组 ID,列出其当前已授予的权限(含指令) |

> **关于「权限组可用指令」的交互**：受现有面板表单/检索能力所限，目前采用「查找指令 → 为权限组逐条授予/撤销 →
> 查看某组已有指令」的方式实现，等效于勾选清单的增删；写入 `openmod.roles.yaml`。后续可加专门的勾选 UI。

## 实现要点(给开发者)

- **无敌**:`GodModeService` 维护一组 SteamID,`GodModeDamageListener`(`IEventListener<UnturnedPlayerDamagingEvent>`)
  在受伤前取消伤害。内存态,重启清除。
- **封禁/解封**:封禁走 OpenMod `IUserManager.BanAsync(user, reason, endTime?)`;解封走 SDG `SteamBlacklist.unban`。
- **权限组**:`IPermissionRoleStore` 列出/分配/移除角色;授予/撤销指令权限走 `IPermissionStore`(若该服务在当前
  作用域不可解析,则该项功能在运行时优雅失败,不影响其余功能与插件加载)。
- **配置**:本插件无可配置项(无敌为内存态),因此 `config.yaml` 为空壳。

## 本地构建与调试

见 [docs/README](README.md#本地构建与调试)。
