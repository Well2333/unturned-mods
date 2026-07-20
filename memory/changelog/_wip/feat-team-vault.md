---
date: 2026-07-17
branch: feat/team-vault
merge_commit: pending
type: feature
scope: well404.Vault, well404.Economy, well404.Essentials, UnturnedMods.Shared, well404.WebPanel
guideline_changed: false
---

# 小队共享仓库、幂等容量购买与可恢复存取

## 做了什么

- 新增基于 Unturned 当前小队 `groupID` 的共享仓库；默认基础容量 200 格、最高 5000 格，成员可用个人余额购买小队容量。
- Essentials 提供实时小队上下文，Vault 每次共享存取前重新验证当前归属，不信任浏览器提交的小队 ID。
- Economy 的 SQLite 后端新增持久幂等操作；容量购买采用 pending、completed、refund_pending、refunded 状态机。
- 扣款等待期间换队时只允许退款；账本查询失败保持 pending 重试，不再把数据库异常误判成“未扣款”。
- 存取审计保存完整物品载荷，并按操作号判定 SQLite 模糊提交，避免错误补偿造成复制。
- 玩家面板增加个人/小队与背包/仓库标签、空态、紧凑内联容量购买、当前容量，以及重复购买版本保护；取出目标可在背包和另一个仓库间切换。
- 背包分区新增手部、背包、胸挂、上衣、裤子的多选来源过滤并记忆开关；跨容器的同类物品分开建卡，网页存入动作携带并校验来源页，隐藏容器不会被误操作。
- 个人仓库默认 50 格、每 100 货币购买 10 格；小队仓库默认 200 格、每 500 货币购买 10 格，二者默认上限 5000。SQLite 余额和在线经验值均可购买。
- WebPanel 定时刷新与动作请求加入代次控制，旧 GET 不会覆盖 POST 返回的新视图。
- WebPanel 升至 1.5.1；多笔玩家动作只应用最后发起者的响应，并在全部完成后执行权威刷新。
- 管理员面板统一为“容量设置/小队仓库/仓库查看”：仓库查看可选择或手工输入个人/小队所有者，编辑当前仓库容量和物品；恢复隔离区合并到小队仓库页。
- 容量购买使用独立 `purchase_version`，普通物品存取不会让购买按钮过期；玩家确认时的格数、价格和最大容量会被服务端精确复核。
- 新购买在创建事务内直接进入 `debiting`，再按 `debiting → debited → ready → completed` /
  `refund_pending → refunded` CAS 状态机推进；恢复流程不会把暂时查不到经济账本标记的 `debiting` 当成扣款失败。
  团队查询区分离线、在线无队伍和在队状态，并由依赖加载事件与 30 秒周期任务持续恢复。
- 每次购买（包括命令入口）都会原子推进 `purchase_version`，所有旧网页报价立即失效。
- 存取隔离审计使用 schema v4，为每件候选记录 `candidate`、`removal_started`、`inventory_removed`、
  `database_removed`、`delivery_started`、`inventory_given` 等阶段；跨 Unity/SQLite 的最后崩溃窗口仍只人工核对、不自动重放。

## 为什么

- 玩家需要一个全队共享、可共同扩容的物品仓库，同时必须避免跨插件扣款和游戏背包/SQLite 双系统间产生重复或资产损失。
- 旧 UI 仅从现有物品卡推导标签，空仓库会隐藏功能；后台也缺少恢复状态的运维可见性。

## 影响的文件 / 范围

- `src/Shared/UnturnedMods.Shared/Teams/`、`Economy/` 的共享接口。
- `src/plugins/well404.Essentials/Party/` 的小队上下文桥接。
- `src/plugins/well404.Economy/` 的 SQLite 幂等账本。
- `src/plugins/well404.Vault/` 的容器模型、SQLite schema、服务、命令、玩家/管理员 Web UI、配置与文档。
- `src/plugins/well404.WebPanel/player.html` 的刷新竞态保护。
- `tests/well404.Economy.Tests/`、`tests/well404.Vault.Tests/` 的并发、迁移、审计和状态机覆盖。

## 注意事项 / 后续

- 本分支完成全量代码审查、构建和本地浏览器验证后，仅提交并推送独立分支供审查，不合并、不发布 NuGet。
- 游戏背包与 SQLite 不能组成同一 ACID 事务；不确定的进程崩溃记录保留完整载荷供人工核对，禁止自动重放。
