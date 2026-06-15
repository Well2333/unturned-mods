# well404.Essentials

面向**玩家**的实用指令合集：回家、玩家间传送、传送点、免费礼包、睡觉投票、回到死亡点。
所有传送共用一套「预热静止 → 可选经济收费 → 冷却」流程。

## 依赖

- **无硬依赖**。没有经济插件时传送免费；没有 `well404.WebPanel` 时只是没有 Web 界面。
- **可选**：任一 `IEconomyProvider`（如 [`well404.Economy`](well404.Economy.md)）→ 启用传送收费；
  [`well404.WebPanel`](well404.WebPanel.md) → 在 Web 界面编辑全部设置。

```bash
openmod install well404.Essentials
openmod reload
```

## 命令

> OpenMod 按命令路径自动派生命令权限，需在权限角色里授予。下表「权限」列为命令权限节点。

| 命令 | 语法 | 命令权限 | 说明 |
| --- | --- | --- | --- |
| `/home` | | `well404.Essentials:home` | 传送回家（需先 `/home set`） |
| `/home set` | | `well404.Essentials:home.set` | 把当前位置设为家（单个家，覆盖旧家） |
| `/tp` | `<玩家>` | `well404.Essentials:tp` | 传送到玩家。同队**直接**传送；跨队发出请求 |
| `/tpa` | `[玩家]` | `well404.Essentials:tpa` | 接受请求（不带参=最新一条；带参=指定玩家） |
| `/tpd` | `[玩家]` | `well404.Essentials:tpd` | 拒绝请求 |
| `/party` | | `well404.Essentials:party` | 查看队伍成员 |
| `/party invite` | `<玩家>` | `well404.Essentials:party.invite` | 邀请玩家入队 |
| `/party accept` | `[玩家]` | `well404.Essentials:party.accept` | 接受邀请（不带参=最新） |
| `/party deny` | `[玩家]` | `well404.Essentials:party.deny` | 拒绝邀请 |
| `/party leave` | | `well404.Essentials:party.leave` | 离开队伍 |
| `/party kick` | `<玩家>` | `well404.Essentials:party.kick` | 踢出队员（仅队长 ADMIN） |
| `/warp` | `<名称>` | `well404.Essentials:warp` | 传送到传送点（还需 per-warp 权限，见下） |
| `/warp set` | `<名称> [冷却秒]` | `well404.Essentials:warp.set` | 新建/覆盖传送点（管理命令） |
| `/warp delete`（`del`） | `<名称>` | `well404.Essentials:warp.delete` | 删除传送点（管理命令） |
| `/warps` | | `well404.Essentials:warps` | 列出你有权限使用的传送点 |
| `/gift` | `[id]` | `well404.Essentials:gift` | 不带参=列出可领；带参=领取 |
| `/sleep` | | `well404.Essentials:sleep` | 睡觉投票，达比例切换昼夜 |
| `/back` | | `well404.Essentials:back` | 回到死亡点并短暂无敌 |

### 自定义权限节点

| 权限 | 作用 |
| --- | --- |
| `well404.essentials.warps.<名称>` | 使用某个传送点。**每个传送点单独授权**（小写名称）。`/warps` 也只列出你有此权限的项 |
| `well404.essentials.cooldown.exempt` | 免除所有传送冷却 |
| 礼包 `permission` 字段（管理员自定义，如 `well404.essentials.gift.vip`） | 领取该 VIP 专属礼包 |

## 传送流程（home / tp / warp / back 共用）

1. **冷却**：若该命令在冷却中（且玩家无 `cooldown.exempt`），拒绝并提示剩余时间。
   `/warp` 可为单个传送点设独立冷却（`cooldownSeconds`，0=用全局）。
2. **费用预检**：若配置了费用且场上存在 `IEconomyProvider`，余额不足则拒绝。
3. **预热**：提示静止 `warmupSeconds` 秒；期间每 200ms 检查位移，移动超过
   `moveThreshold` 米则取消（`cancelOnMove`）。中途掉线也取消。
4. **传送**：到达目的地（含朝向）。
5. **扣费**：传送成功后扣除费用（仅有经济插件时）。
6. **记录冷却**。

跨队 `/tp` 不立即传送，而是给目标发请求；目标 `/tpa` 接受后，**发起者**完成上面的预热并
传送到目标当时所在位置，`/tpd` 则拒绝。请求在 `tpa.expirationSeconds` 后自动过期并通知发起者。

## 组队（`/party`）

绕过 Unturned 难用的游戏内组队菜单，用指令直接组队。采用**邀请 + 接受**模式（防滥用）：

- `/party invite <玩家>` 发出邀请；对方 `/party accept` 入队、`/party deny` 拒绝，
  邀请在 `party.inviteExpirationSeconds` 后过期。
- 邀请者若**还没有队伍**，接受时会自动新建一个队伍（邀请者为队长 ADMIN，新成员为
  MEMBER）；若已有队伍则直接加入。
- `/party leave` 离队；`/party kick <玩家>` 由**队长**踢人；`/party` 查看在线队员与角色。
- `party.maxMembers > 0` 时按此上限拦截入队（始终绕过原版人数上限，仅用插件自己的上限）。

实现上用服务端 `PlayerQuests.ServerAssignToGroup` + `GroupManager` 直接改组，因此**与原版
组队完全互通**：组好的队伍在客户端 UI 正常显示，并让 Essentials 的 `/tp` 走「同队免确认
直传」分支（`isMemberOfSameGroupAs` 对无队玩家返回 false，所以单人不会被误判同队）。

## 配置（`config.yaml`）

首次加载后生成于 `openmod/plugins/well404.Essentials/config.yaml`。装了 WebPanel 时这些值
也可在 Web 界面编辑（编辑会重写本文件，其中的注释会丢失）。

```yaml
teleport:
  warmupSeconds: 5        # 传送前静止秒数，0=瞬移
  cancelOnMove: true      # 预热期间移动是否取消
  moveThreshold: 0.5      # 移动判定阈值（米）
  cooldownSeconds: 0      # 成功传送后的冷却，0=无
  costs:                  # 经济费用，按命令；需经济插件，默认 0=免费
    home: 0
    tp: 0
    warp: 0
    back: 0
tpa:
  expirationSeconds: 30   # /tp 请求有效期
party:
  inviteExpirationSeconds: 60  # /party 邀请有效期
  maxMembers: 0           # 队伍人数上限，0=不额外限制（始终绕过原版上限）
back:
  invincibilitySeconds: 5 # /back 落地后的无敌秒数，0=无
sleep:
  enabled: true
  requiredRatio: 0.5      # 通过所需的「在线玩家比例」，0.5=半数
warps: []                 # 由 /warp set 或 WebUI 维护
gifts: []                 # 见下
```

### 礼包（`gifts`）

每位玩家在每个 **crontab 周期**内可领取一次。`cron` 按**服务器本地时间**评估；为空表示
「只能领一次」。`permission` 为空=所有人可领，否则需要该权限（用于 VIP 专属）。

```yaml
gifts:
  - id: "daily"
    name: "Daily Supply"
    permission: ""          # 所有人
    cron: "0 0 * * *"       # 每天 00:00 刷新
    items:
      - itemId: 15
        amount: 2
  - id: "vip"
    name: "VIP Weekly Crate"
    permission: "well404.essentials.gift.vip"
    cron: "0 0 * * 1"       # 每周一 00:00 刷新
    items:
      - itemId: 81
        amount: 1
```

支持标准 5 字段 crontab（分 时 日 月 周）：`*`、`*/步长`、`a-b` 区间、`a-b/步长`、
逗号列表；周为 0–6（周日=0，也接受 7）。当「日」与「周」都被限定时，任一匹配即触发
（Vixie cron 规则）。

## 睡觉投票（`/sleep`）

输入 `/sleep` 记一票。当票数 ≥ `ceil(requiredRatio × 在线人数)` 时，世界在白天/黑夜之间
切换、票数清零并广播。离线玩家的票会在下次投票时被剔除。`enabled: false` 可关闭。

## 数据持久化

- 玩家的**家**、**死亡点**、**礼包领取时间**存于插件数据目录（`players.data.yaml`），按 Steam ID。
- **传送点**与**礼包定义**存于 `config.yaml`（与 WebUI / 命令共用同一份）。
- 冷却与待确认的 tpa 请求是内存态，插件重载后清空。

## Web 面板模块

装了 `well404.WebPanel` 时，Essentials 注册「实用功能 / Essentials」模块：

- **传送设置 / tpa·sleep·back**：两组设置，页尾统一保存。
- **传送点 / 礼包**：集合式 CRUD（点选编辑、新增、删除）。
- **检索游戏物品**：按名称或 ID 查物品，便于填礼包内容；输入**纯数字**时优先列出该**精确 ID** 的物品。

> 在 WebUI 新建传送点后，记得给玩家授予对应的 `well404.essentials.warps.<名称>` 权限。

## 玩家网页「实用工具」菜单（可选）

装了 `well404.WebPanel` 后，Essentials 还会注册一个**面向玩家**的「实用工具」菜单（玩家面
`/p`，图标 🧭，中英双语）。玩家在游戏内输入 `/menu`（或 `/menu essentials`）收到链接，在浏览器里：

- **家**：**把家设在当前位置**（等价 `/home set`）、**回家**（已设置时）、**返回死亡点**（有记录时才出现）、传送到任一**有权限**的传送点；
- **传送请求**：向任一在线玩家发起 `/tp` 请求；并能看到、**接受/拒绝**别人发给你的请求；
- **组队**：邀请在线玩家；看到并**接受/拒绝**别人的邀请；查看队伍成员、退出队伍；身为**队长**时可逐个**踢出队员**；
- **领取礼包**：列出你可领的礼包，就绪即可一键领取（含 crontab 冷却显示）；
- **睡觉投票**：一键投票切换昼夜。

所有传送仍走与命令相同的 `TeleportService` 流程（冷却、可选经济费用、静止读条）；具体失败原因
（冷却中、余额不足、移动取消）仍在游戏内提示。请求/邀请的接受流程与游戏内 `/tpa`、`/party accept` 等价。
