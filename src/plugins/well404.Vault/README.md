# well404.Vault

> Vault 的玩家仓库与管理员仓库页已迁移到插件自建 Web UI。个人/小队仓库使用顶部标签切换；物品使用六列紧凑等高网格，长名称自动换行。所有存取权限与原指令保持一致。

> Unturned / OpenMod 私人仓库插件 —— 玩家把背包里的物品存入 / 取出个人仓库,容量按背包格子数计算。

`well404.Vault` 是 **well404 OpenMod 插件家族** 的仓库模块。个人仓库可独立运行；安装
[well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) 后会启用当前 Unturned 小队的共享仓库，
安装 [well404.Economy](https://www.nuget.org/packages/well404.Economy/) 后，玩家可用 SQLite 余额或在线游戏经验值购买个人与小队容量。
装了 [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) 时提供完整的网页存取与配置界面。

## 功能

- 🧳 每位玩家一个**私人仓库**,通过指令或网页存入 / 取出背包物品
- 👥 **小队共享仓库**：玩家同一时间只属于一个 Unturned 小队；仓库绑定稳定的小队 ID，成员共享存取，
  新仓库默认 200 格，购买后最高 5000 格，离队不会带走或退还小队容量
- 🎒 **完整保真**:保留物品耐久、品质、枪械配件、弹匣/弹药箱内的弹药等原始状态,取出原样还原
- 📐 容量按**背包格子数**计:每个物品按其网格尺寸 `宽×高` 占用(如 2×2 弹药箱 = 4 格),
  物品内部的堆叠 / 弹药数**不计入**(一盒 39 发的弹药箱仍只占其格子,不算 39 个)
- 👤 **个人容量购买**：默认 50 格，玩家可用余额每次购买 10 格（默认价格 100），最高 5000 格；权限档位仍可提高基础容量
- 🔎 网页仓库**按语义合并 + 弹窗详情**：只有游戏资源 `ItemAsset.showQuality` 为真时才把 quality 当作耐久并区分变体；医疗品、补给品等无耐久物品不会显示虚假百分比，也不会因原始 quality 字节拆成多张卡。不同装填量或状态的同种物品（如 5 发/8 发弹药盒）
  默认收起为汇总卡,点击后在弹窗内展示逐个变体,弹药/堆叠以**装填比例**(如 `6/8`)展示;物品以六列紧凑等高网格排布，长名称自动换行,
  单件给一个**存入/取出**按钮、多件给**全部 / 数量 / 一件**(数量弹出空白框、需自行输入,标题标注范围 `1–N`,与「全部」「一件」都不同)
- 🌐 可选 Web 面板：玩家网页仓库 + 管理员统一查看/编辑个人与小队仓库容量和物品；恢复隔离区位于小队仓库页，容量购买可通过强确认、CAS 和追加审计进行受控解决
- 🌏 物品名称随面板语言显示：中文界面为中文主标题 + 下一行灰色英文参考，英文界面只显示英文
- 🧩 创意工坊重复使用同一旧式数字 ID 时，名称、稀有度、类型与耐久规则以 Unturned 实际加载的
  `Assets.find` 结果为准，不再被物品目录中另一个同 ID 资源覆盖
- 🎨 物品卡片按 Unturned 原生稀有度显示对应颜色边框，并同时显示中英文稀有度文字，避免只靠颜色辨识
- ↕️ 支持按总占用格数、物品 ID、物品数量、稀有度、名称排序；默认按总占用格数降序，方向可切换
- 🧭 使用两行分类筛选：第一行是现有物品大类，第二行是准确的 Unturned 原生 `EItemType`；选择大类后
  只显示其中当前存在的原生类型。分类不读取配方语义，`Supply` 始终按自身类型进入材料；未知新类型
  显示原始类型文本并安全归入「其他」
- 🎽 背包页支持按身上容器多选过滤手部、背包、胸挂、上衣和裤子；默认全部显示，可关闭裤子/手部来保留常用工具。相同物品跨容器分开显示，存入只作用于卡片标明的容器
- 💾 背包/仓库标签、排序方式、排序方向、大类/原生类型和容器过滤条件在当前浏览器会话内保留

## 安装

```
openmod install well404.Vault
```

安装或升级 DLL 后必须完整重启服务器；不要用 `openmod reload` 替换二进制。部署本版本时必须同时更新
`UnturnedMods.Shared.dll`、Essentials、Economy、Vault 与 WebPanel，避免新旧共享接口和刷新协议并存。

## 命令

| 命令 | 别名 | 说明 |
| --- | --- | --- |
| `/vault` | `/storage` | 显示用法 |
| `/vault store <物品ID> [数量]` | `deposit` | 把背包里该物品(按游戏物品 ID)存入仓库 |
| `/vault take <物品ID> [数量]` | `withdraw` | 把仓库里该物品取回背包(背包满则掉落脚下) |
| `/vault list` | `info` | 列出仓库内容与占用 |
| `/vault upgrade` | `buy` | 用自己的余额购买个人仓库容量 |
| `/vault team store <物品ID> [数量]` | `deposit` | 把背包物品存入当前小队共享仓库 |
| `/vault team take <物品ID> [数量]` | `withdraw` | 从当前小队共享仓库取出物品 |
| `/vault team list` | `info` | 列出当前小队仓库内容与占用 |
| `/vault team upgrade` | `buy` | 用自己的余额为当前小队购买容量 |

> 装了 WebPanel 时,`/menu vault` 可打开网页仓库(背包 / 仓库两区,点按钮存取)。

- **权限**:个人命令使用 `well404.Vault:commands.vault.store|take|list|upgrade`；小队命令分别使用
  `well404.Vault:commands.vault.team.store|take|list|upgrade`。网页端逐项检查同一权限，个人权限不能替代小队权限。
- **只存背包**:手持的主 / 副武器槽物品不会被存入,需先放进背包。

## 配置 (config.yaml)

```yaml
# 基础容量(背包格子数)。每个物品按其网格尺寸(宽×高)占用;内部堆叠/弹药数不计入。
maxSlots: 50
# 按权限的容量档位;玩家取「基础与其拥有的各档位」中的最大值。
tiers: {}
#   "well404.vault.size.vip": 400

personalPurchase:
  enabled: true
  maxSlots: 5000
  slotsPerPurchase: 10
  price: 100

teamVault:
  enabled: true
  baseSlots: 200
  maxSlots: 5000
  purchase:
    enabled: true
    slotsPerPurchase: 10
    price: 500
```

所有容量字段的硬上限为 1,000,000 格；数据库和运行时均校验边界。旧 `capacity_overrides` 在 schema v6 中按绝对容量迁移，包括空库存玩家，迁移可幂等重跑且不会覆盖 v5 已编辑/购买的容器。

装了 WebPanel 时，可配置个人与小队的基础容量、购买步长、价格和上限。管理员在“仓库查看”中可快捷选择有库存玩家或小队，也可手工输入所有者 ID，查看/编辑物品并直接调整当前仓库容量；“恢复隔离区”已合并到“小队仓库”页面。

容量购买采用唯一操作号、SQLite 幂等账本和 `debiting/debited/ready/refund_pending` 持久阶段，重试不会重复扣款；
只有重新确认买家仍属于原小队后才能扩容，已完成操作不能被并发恢复流程再次退款。团队查询会区分离线和在线无队伍，
并在依赖加载、30 秒周期任务及下次购买前持续恢复。恢复按批次处理，外部 Economy/小队 provider 单次最多等待 3 秒。经验值后端也能购买个人和小队容量，但仅适用于在线玩家，且不具备 SQLite 的跨重启幂等账本；极端中断会保留在恢复隔离区，禁止自动猜测扣款或退款。管理员人工核对并退款后，可用强确认关闭经验值 `debiting` / `refund_pending` 记录。经验值没有小数，使用该后端时扩容价格必须是整数。价格为 0 的免费扩容不依赖 Economy。
物品存取另有持久审计，并保存每件物品的完整状态载荷和逐项处理阶段：明确未提交的异常会即时返还/恢复；模糊提交先查询审计结果，
崩溃留下的 `removal_started` / `delivery_started` 等不确定记录会出现在管理员「恢复隔离区」供人工核对，不会自动重放造成复制。玩家在扣款期间换队时，
购买会进入只退款状态，绝不会给旧队扩容。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 分组物品商店,买卖物品,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的实用指令：home/tp/warp/gift/sleep/back/party，经济收费可选 |
| [well404.Vault](https://www.nuget.org/packages/well404.Vault/) | 私人仓库,存取背包物品(完整保真,按格子计容量) |

完整文档见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404

## 管理员仓库查看

管理员 WebPanel 的“仓库查看”统一支持个人与小队仓库：可从有库存玩家/小队快捷列表选择，也可手工输入个人 SteamID 或稳定小队 ID；可新增、编辑、逐条删除或按物品 ID 批量删除记录，并可把当前仓库容量调整为不低于已占用格数且不超过 1,000,000 的正整数。容量调整以独立增量保存，不会清空既有购买格数，后续购买仍会继续增加有效容量。恢复隔离区已并入“小队仓库”页；购买解决必须填写原因并完整输入操作号二次确认，状态变更与审计记录同事务提交。
