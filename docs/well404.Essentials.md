# well404.Essentials

面向**玩家**的实用指令合集：回家、玩家间传送、传送点、免费礼包、睡觉投票、回到死亡点。
所有传送共用一套「预热静止 → 可选经济收费 → 冷却」流程。

## 依赖

- **无硬依赖**。没有经济插件时传送免费；没有 `well404.WebPanel` 时只是没有 Web 界面。
- **可选**：任一 `IEconomyProvider`（如 [`well404.Economy`](well404.Economy.md)）→ 启用传送收费；
  [`well404.WebPanel`](well404.WebPanel.md) → 在 Web 界面编辑全部设置。

```bash
openmod install well404.Essentials
```

安装或升级 DLL 后必须完整重启服务器；`openmod reload` 只适合未替换二进制时的配置重载。

## 命令

> OpenMod 按命令路径自动派生命令权限，需在权限角色里授予。下表「权限」列为命令权限节点。

| 命令 | 语法 | 命令权限 | 说明 |
| --- | --- | --- | --- |
| `/home` | | `well404.Essentials:commands.home` | 传送回家（需先 `/home set`） |
| `/home set` | | `well404.Essentials:commands.home.set` | 把当前位置设为家（单个家，覆盖旧家） |
| `/tp` | `<玩家>` | `well404.Essentials:commands.tp` | 传送到玩家。同队**直接**传送；跨队发出请求 |
| `/tpa` | `[玩家]` | `well404.Essentials:commands.tpa` | 接受请求（不带参=最新一条；带参=指定玩家） |
| `/tpd` | `[玩家]` | `well404.Essentials:commands.tpd` | 拒绝请求 |
| `/party` | | `well404.Essentials:commands.party` | 查看队伍成员 |
| `/party invite` | `<玩家>` | `well404.Essentials:commands.party.invite` | 邀请玩家入队 |
| `/party accept` | `[玩家]` | `well404.Essentials:commands.party.accept` | 接受邀请（不带参=最新） |
| `/party deny` | `[玩家]` | `well404.Essentials:commands.party.deny` | 拒绝邀请 |
| `/party leave` | | `well404.Essentials:commands.party.leave` | 离开队伍 |
| `/party kick` | `<玩家>` | `well404.Essentials:commands.party.kick` | 踢出队员（仅队长 ADMIN） |
| `/warp` | `<名称>` | `well404.Essentials:commands.warp` | 传送到传送点（还需 per-warp 权限，见下） |
| `/warp set` | `<名称> [标签 ...]` | `well404.Essentials:commands.warp.set` | 新建/覆盖传送点，可追加多个空格分隔标签（管理命令） |
| `/warp delete`（`del`） | `<名称>` | `well404.Essentials:commands.warp.delete` | 删除传送点（管理命令） |
| `/warps` | | `well404.Essentials:commands.warps` | 列出你有权限使用的传送点 |
| `/gift` | `[id]` | `well404.Essentials:commands.gift` | 不带参=列出可领；带参=领取 |
| `/sleep` | | `well404.Essentials:commands.sleep` | 睡觉投票，达比例切换昼夜 |
| `/back` | | `well404.Essentials:commands.back` | 回到死亡点并短暂无敌 |

### 自定义权限节点

| 权限 | 作用 |
| --- | --- |
| `well404.Essentials:well404.essentials.warps.<名称>` | 使用某个传送点。**每个传送点单独授权**（小写名称；插件会随传送点自动注册）。`/warps` 也只列出你有此权限的项 |
| `well404.Essentials:well404.essentials.cooldown.exempt` | 免除所有传送冷却 |
| 礼包 `permission` 字段（管理员自定义，如 `well404.essentials.gift.vip`） | 领取该 VIP 专属礼包 |

## 传送流程（home / tp / warp / back 共用）

1. **冷却**：若该命令在冷却中（且玩家无 `cooldown.exempt`），拒绝并提示剩余时间。
   所有传送点共用 `teleport.cooldownSeconds` 和同一个 warp 冷却桶，不支持单个传送点独立冷却。
2. **费用预检**：若配置了费用且场上存在 `IEconomyProvider`，余额不足则拒绝。
3. **预热**：提示静止 `warmupSeconds` 秒；期间每 200ms 检查位移，移动超过
   `moveThreshold` 米则取消（`cancelOnMove`）。中途掉线也取消。
4. **扣费**：预热完成后原子扣除费用（仅有经济插件时），并再次防止余额并发变化导致透支。
5. **传送**：到达目的地（含朝向）；若传送失败，会自动退回刚才扣除的费用。
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
warpMap:
  enabled: true            # 玩家互动地图开关
  visibility: "native"    # native=遵循原生 Chart 权限；always=始终允许玩家查看
warps: []                 # 由 /warp set 或 WebUI 维护；每项可含多个 tags
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

输入 `/sleep` 记一票。当票数 ≥ `ceil(requiredRatio × 在线人数)` 时：当前为白天则跳到
傍晚，当前为夜晚则跳到早晨；随后票数清零并广播。离线玩家的票会在下次投票时被剔除。
`enabled: false` 可关闭。

## 数据持久化

- 玩家的**家**、**死亡点**、**礼包领取时间**存于插件数据目录（`players.data.yaml`），按 Steam ID。
- **传送点**、**预设/自定义标签定义**与**礼包定义**存于 `config.yaml`（与 WebUI / 命令共用同一份）。
- 玩家选择的地图框尺寸存于 `players.data.yaml`，按 Steam ID 保存为 `compact`（紧凑、自适应）或 `large`（宽大）。
- 冷却与待确认的 tpa 请求是内存态，插件重载后清空。

## Web 面板模块

装了 `well404.WebPanel` 时，Essentials 注册「实用功能 / Essentials」模块：

- **传送设置 / tpa·sleep·back**：两组设置，页尾统一保存。
- **传送点**：GPS / Chart / 列表三视图。两张底图都叠加当前运行地图中坐标有效的传送点；管理员点击图钉直接编辑，玩家点击图钉弹出二次确认后传送。列表保留所有地图的数据，可按“全部/标签”过滤、弹窗新增/编辑/删除，并在当前标签内拖动排序。
- **标签库**：预设与自定义标签分别保存稳定 ID、英文名、中文名与 emoji；管理员可管理定义，并在传送点弹窗中用下拉复选框组合多个标签或录入自定义 ID。
- **礼包**：集合式 CRUD（点选编辑、新增、删除）。
- **检索游戏物品**：按名称或 ID 查物品，便于填礼包内容；输入**纯数字**时优先列出该**精确 ID** 的物品。中文界面显示中文主标题与换行灰色英文参考，英文界面仅显示英文；中英文名称都可检索。

管理端用顶部标签在传送设置、其他规则、传送点、礼包和物品检索之间切换；玩家「实用工具」只展示当前选中的功能标签。普通列表保持 5 秒刷新，进入 GPS 或 Chart 后暂停轮询，离开地图时自动恢复。

> 在 WebUI 新建传送点后，插件会立即注册其权限；请给玩家授予对应的 `well404.Essentials:well404.essentials.warps.<名称>` 权限。

## 玩家网页「实用工具」菜单（可选）

该菜单与管理员模块均采用 Essentials 自带的 HTML/CSS/JavaScript，并由 WebPanel 在 Shadow DOM
中挂载。传送、请求、队伍、在线玩家、礼包和世界时间使用类似商店的顶部标签切换；当前标签内部使用紧凑自适应网格，窄版 Steam 浏览器自动退化为单列。业务操作仍调用原有
handler，与游戏指令共用权限和服务。

装了 `well404.WebPanel` 后，Essentials 还会注册一个**面向玩家**的「实用工具」菜单（玩家面
`/p`，图标 🧭，中英双语）。玩家在游戏内输入 `/menu`（或 `/menu essentials`）收到链接，在浏览器里：

- **家与传送点**：**把家设在当前位置**（等价 `/home set`）、**回家**、**返回死亡点**；家与死亡点只在列表使用按内容宽度排列的紧凑命令条，地图内改为 `🏠` / `💀` 图标。当前地图的传送点可在 GPS、Chart 或列表中按标签筛选，点图钉后二次确认传送；
- **传送请求**：向任一在线玩家发起 `/tp` 请求；并能看到、**接受/拒绝**别人发给你的请求；
- **组队**：邀请在线玩家；看到并**接受/拒绝**别人的邀请；查看队伍成员、退出队伍；身为**队长**时可逐个**踢出队员**；
- **领取礼包**：列出你可领的礼包，就绪即可一键领取（含 crontab 冷却显示）；
- **睡觉投票**：一键投票切换昼夜。

所有传送仍走与命令相同的 `TeleportService` 流程（冷却、可选经济费用、静止读条）；具体失败原因
（冷却中、余额不足、移动取消）仍在游戏内提示。请求/邀请的接受流程与游戏内 `/tpa`、`/party accept` 等价。

## 玩家与队伍、传送点标签（1.3.0）

玩家面板把在线玩家、传送请求、组队邀请和当前队伍合并到「玩家与队伍」标签：在线玩家列表位于上方，当前队伍位于下方。邀请、接受入队、退出队伍和踢出队员都会显示二次确认。

管理员可在「Essentials → 传送点」为每个传送点设置多个 `tags`。传送点弹窗提供下拉复选框，并允许录入自定义标签 ID；“管理标签”弹窗可编辑预设与自定义两类定义。两类定义分别存入 `warpTags.presets` 和 `warpTags.custom`，每项包含稳定 ID、英文名、中文名和 emoji。默认预设为其他、城市、乡村、军事基地、安全区、病毒区、资源点和 NPC；列表与地图使用传送点标签顺序中第一个配置了 emoji 的标签作为图标。未知旧标签会迁移为自定义定义。管理页和玩家页均通过“全部/标签”筛选同一份目录；管理员可在任一筛选下拖动全局顺序，玩家只能过滤和传送。旧 `category` 自动迁移成单个标签；没有标签的传送点归入 `default`。`/warp set <名称>` 只更新坐标并保留原标签，追加标签时则替换标签列表。所有传送点共用同一个 warp 冷却桶。

## 互动传送地图（玩家端与管理员端）

底图直接读取当前 Unturned 地图目录中的 `Map.png`（GPS/卫星图）和 `Chart.png`（纸质地图），不生成截图，也不使用外部地图服务。两张图共用游戏原生投影：普通地图以 `Level.size - 2 × Level.border` 为有效边长；声明了主 Cartography Volume 的地图则使用该体积的本地坐标。超出地图范围、无法换算或不属于当前地图的传送点不会生成图钉，但仍可在管理员列表中查看。

操作方式：

- 玩家端和管理员端都可切换 GPS / Chart / 列表，并记住当前浏览器会话的选择；
- 标签筛选同时更新两种底图的图钉和列表；
- 地图画布提供“紧凑/宽大”两种模式：“紧凑”按浏览器内容视口高度自适应并允许少量纵向滚动，“宽大”按容器宽度展开；偏好按 Steam ID 保存在服务器；
- 滚轮缩放，按住空白处拖动平移，`+`/`−`/“复位”可辅助操作；GPS / Chart 地图视图暂停 5 秒宿主刷新，切回列表时恢复，因此操作中不会突然复位；
- 图钉位于独立叠加层，底图缩放时保持固定尺寸，名称只在悬停或键盘聚焦时出现；图标取第一个配置了 emoji 的标签；
- 玩家点击图钉直接进入宿主二次确认，确认后传送，不需要滚动到额外词条；管理员点击图钉直接打开同一套编辑弹窗；
- 回家和返回死亡点只在玩家列表中显示独立命令条，地图上仅保留 `🏠` 与 `💀` 两个紧凑图标；
- 管理员列表显示传送点的 `map`，编辑时可显式改图；新建时留空会自动取当前地图。

玩家可见性由 `warpMap` 控制。推荐 `enabled: true`、`visibility: native`：Chart 单独检查 `Gameplay.Chart` 或物品 `enablesChart`，GPS 单独检查 `Gameplay.Satellite` 或物品 `enablesMap`；非生存地图默认允许两者。`visibility: always` 会跳过两套资格检查。管理员两种底图只受管理 token 保护，不依赖管理员对应玩家的背包；即使关闭玩家地图，管理员仍可用地图管理传送点。

图片请求仍经过 WebPanel 身份验证。服务端只接受固定资源 id `gps` 和 `chart`，不会接受浏览器提供的文件路径；只返回 PNG，每张最大 64 MiB，两张图分别缓存并使用独立 ETag。任一底图缺失、超限、无原生资格或加载失败时，对应按钮仍可切换并显示具体原因，另一底图与列表不受影响。

### 旧传送点迁移

每个传送点现在有 `map`。`/warp set` 会自动写当前地图；管理员网页新建且 `map` 留空时也会写当前地图。玩家的 `/warp`、`/warps` 和网页只接受 `map` 与当前地图匹配的传送点，防止换图后落到错误坐标。

旧配置没有 `map` 时不会自动猜测。这类记录仍出现在管理员列表，但不会暴露给玩家，也不能传送。管理员必须根据实际游戏用途填写正确地图；确认属于当前地图的点也可在正确位置重新执行 `/warp set <名称>`。标签迁移规则不变：旧 `category` 只迁移成已有标签，不会用于推断地图。

```yaml
warps:
  - name: "spawn"
    map: "PEI"
    tags:
      - "public"
      - "city"
    order: 1
    x: 0
    y: 20
    z: 0
    yaw: 0
```
