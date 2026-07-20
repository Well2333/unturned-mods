# well404.Essentials

> Essentials —— 面向玩家的实用指令合集：回家、传送、传送点、礼包、睡觉投票、回到死亡点。

`well404.Essentials` 是 **well404 OpenMod 插件家族** 的一员。它面向**玩家**而非管理员，
把生存常用的便利指令集中到一个插件里，并为所有传送提供统一的「预热静止 + 可选经济收费 +
冷却」体验。

## 功能

- **/home**：回到你设置的家；`/home set` 把当前位置设为家（单个家）。
- **/tp <玩家>**：传送到某玩家。**同队**直接传送；**不同队**则发出请求，对方用
  `/tpa` 接受或 `/tpd` 拒绝（请求会超时）。
- **/party**：组队。`/party invite <玩家>` 发邀请，对方 `/party accept` 入队（绕过难用的
  游戏内组队菜单）；还有 `/party deny`、`/party leave`、`/party kick`、`/party`（看成员）。
  组队后队友之间 `/tp` 即可免确认直传。
- **/warp**：传送点。`/warp <名称>` 传送，`/warp set <名称> [标签 ...]` 新建，
  `/warp delete <名称>` 删除，`/warps` 列出你有权限使用的传送点（指令格式参考 NewEssentials）。
- **/gift**：免费礼包。`/gift` 列出你可领取的，`/gift <id>` 领取。每个礼包可设
  **crontab 刷新规则**与**可选权限**（做 VIP 专属礼包）。
- **/sleep**：睡觉投票。在线玩家中达到设定比例（默认半数）后，白天跳到傍晚、夜晚跳到早晨。
- **/back**：回到上次死亡的地点，并获得可配置秒数的无敌时间。

**传送通用规则**（home/tp/warp/back 共用）：传送前需静止预热若干秒（默认 5s，移动则取消）、
可设全局传送冷却（所有传送点共用同一 warp 冷却）、以及**可选的经济费用**——仅当装了任一 `IEconomyProvider`（如
`well404.Economy`）时才扣费，默认全为 0（免费）。

装了 `well404.WebPanel` 时，上述所有设置项（传送规则、费用、tpa/sleep/back、传送点、礼包）
都可在 Web 界面里编辑。同时还会给玩家提供一个**面向玩家**的「实用工具」菜单(中英双语)：玩家
游戏内输入 `/menu`（或 `/menu essentials`）即可在浏览器里设置家/回家/返回死亡点/传送到有权限的传送点、
向玩家发起传送请求并接受/拒绝来请、组队邀请/接受/退出/队长踢人、领取礼包、投票睡觉(沿用同样的冷却/收费/读条)。

Essentials 已使用 WebPanel 的插件自建 UI：玩家页把在线玩家、传送请求与队伍合并到「玩家与队伍」标签（在线玩家在上、队伍在下），所有入队、退队、邀请和踢人操作都有二次确认；管理员页同样以标签切换各设置与目录，不会再让一个简单传送点占满整行。礼包物品与物品检索遵循面板语言：中文界面显示中文主标题及换行灰色英文参考，英文界面仅显示英文。

传送点支持 **GPS / Chart / 列表三视图**：GPS 读取原生 `Map.png`，Chart 读取 `Chart.png`；两张底图叠加同一批当前地图传送点。玩家点击固定尺寸的 emoji 图钉会立即弹出二次确认，确认后传送，不再显示额外操作条；管理员点击图钉仍直接编辑。地图视图会暂停宿主的 5 秒轮询，离开地图后恢复，因此滚轮/按钮缩放、拖动位置和底图不会被刷新复位。画布的“紧凑”模式按浏览器内容视口高度自适应，“宽大”模式按容器宽度展开；两种偏好均按玩家保存在服务器。回家与返回死亡点只在列表使用紧凑命令条，地图内仅显示 `🏠` 与 `💀` 两个快捷图标。

玩家地图默认遵循游戏原生规则：Chart 使用服务器 `Gameplay.Chart` / 物品 `enablesChart`，GPS 使用 `Gameplay.Satellite` / 物品 `enablesMap`；非生存地图和 `visibility: always` 会放开两者。管理员地图不受玩家背包限制，但仍需管理 token。地图图片只通过 WebPanel 鉴权接口读取，不公开服务器文件路径。

## 安装

```
openmod install well404.Essentials
```

安装或升级 DLL 后必须完整重启服务器；不要用 `openmod reload` 替换二进制。Essentials **不**强制依赖经济或面板插件——
没有它们也能正常工作（传送免费、无 Web 界面）。

## 命令

| 命令 | 别名 | 语法 | 说明 |
| --- | --- | --- | --- |
| `/home` | | | 传送回家 |
| `/home set` | | | 把当前位置设为家 |
| `/tp` | | `<玩家>` | 传送到玩家（同队直传，跨队发请求） |
| `/tpa` | | `[玩家]` | 接受传送请求（最新的，或指定玩家的） |
| `/tpd` | | `[玩家]` | 拒绝传送请求 |
| `/party` | | | 查看你的队伍成员 |
| `/party invite` | | `<玩家>` | 邀请玩家入队 |
| `/party accept` | | `[玩家]` | 接受入队邀请 |
| `/party deny` | | `[玩家]` | 拒绝入队邀请 |
| `/party leave` | | | 离开队伍 |
| `/party kick` | | `<玩家>` | 踢出队员（仅队长） |
| `/warp` | | `<名称>` | 传送到传送点 |
| `/warp set` | | `<名称> [标签 ...]` | 新建/覆盖传送点（管理） |
| `/warp delete` | `del` | `<名称>` | 删除传送点（管理） |
| `/warps` | | | 列出你能用的传送点 |
| `/gift` | | `[id]` | 列出可领礼包 / 领取指定礼包 |
| `/sleep` | | | 投票切换昼夜 |
| `/back` | | | 回到死亡点并短暂无敌 |

## 权限

OpenMod 会按命令路径自动派生命令权限（如 `well404.Essentials:commands.home`、
`well404.Essentials:commands.warp.set`）。此外本插件使用这些自定义权限节点：

| 权限 | 作用 |
| --- | --- |
| `well404.Essentials:well404.essentials.warps.<名称>` | 使用某个传送点（每个传送点单独授权；插件会随传送点自动注册） |
| `well404.Essentials:well404.essentials.cooldown.exempt` | 免除传送冷却 |
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
party:
  inviteExpirationSeconds: 60  # /party 邀请超时
  maxMembers: 0           # 队伍人数上限，0=不额外限制
back:
  invincibilitySeconds: 5 # /back 落地后的无敌秒数
sleep:
  enabled: true
  requiredRatio: 0.5      # 通过所需的在线玩家比例
warpMap:
  enabled: true            # 玩家互动地图总开关；管理员仍可用地图管理
  visibility: "native"    # native=遵循原生地图权限；always=玩家始终可看
warpTags:
  initialized: true
  presets:                 # 内置标签；ID、双语名称和 emoji 均可由管理员编辑
    - id: "city"
      nameEn: "City"
      nameZh: "城市"
      emoji: "🏙️"
  custom:                  # 自定义标签独立保存，结构与 presets 相同
    - id: "event"
      nameEn: "Event"
      nameZh: "活动"
      emoji: "🎯"
warps:
  - name: "spawn"
    map: "PEI"             # 所属地图；/warp set 会自动写入当前地图
    tags:                   # 一个传送点可属于多个玩家面板过滤标签
      - "public"
      - "city"
    order: 1               # 全局顺序；在任一标签下拖动会调整筛选项的相对顺序
    x: 0
    y: 20
    z: 0
    yaw: 0
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
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 分组物品商店,买卖物品,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的实用指令：home/tp/warp/gift/sleep/back，经济收费可选 |

完整文档见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404

### 传送点标签与排序

管理员可在 WebPanel 的传送点标签页为一个传送点设置多个 `tags`，编辑器使用可复选下拉列表，并允许直接录入自定义标签 ID。标签定义保存在 `warpTags.presets` 与 `warpTags.custom` 两个独立配置集合中，每项都含稳定 ID、英文名、中文名和 emoji；管理员可在“管理标签”中编辑两类定义。默认预设包括其他、城市、乡村、军事基地、安全区、病毒区、资源点和 NPC。地图图钉与列表优先使用传送点标签顺序中第一个已配置 emoji 的标签；没有匹配 emoji 时回退为定位图标。未知旧标签会安全迁移为自定义定义，不会丢失。管理页和玩家页都使用“全部/标签”筛选同一份全局目录；管理员可在任一标签筛选下拖动排序，玩家只能过滤和传送。`/warp set <名称>` 仅更新坐标并保留已有标签；追加标签时会替换标签列表。所有传送点共用同一个 warp 冷却桶。

### 互动传送地图与旧数据

每个传送点现在保存 `map`。`/warp set` 会用当前运行地图自动填写；管理员网页新建时 `map` 留空也会自动使用当前地图。玩家命令、`/warps` 和玩家网页只显示并允许使用**当前地图**的传送点，防止换图后传到无意义坐标；管理员列表仍显示全部地图并可修改归属。

旧配置里没有 `map` 的传送点不会被猜测归属，也不会自动套用当前地图。它们继续留在管理员列表中，但不会出现在玩家列表或地图上。管理员应核对实际用途后，在编辑弹窗填写正确地图；若传送点确实属于当前地图，也可在原位置重新执行 `/warp set <名称>`。这是有意的安全边界，避免插件替管理员猜错地图。

`warpMap.visibility: native` 是推荐值；它分别检查原生 Chart 与 GPS/卫星图资格。设为 `always` 会让玩家网页不再要求这两类资格。无论哪种模式，只有已登录、在线且有传送点权限的玩家能看到对应标记。`Chart.png` 与 `Map.png` 各自限制为 16 MiB，并独立使用私有缓存与 ETag，替换任一图片后会自动更新。
