# 长期记忆系统（Long-Term Memory）

本仓库完全由 AI 开发。为保证跨会话、跨时间的一致性与可溯源性，所有长期记忆
**存储在本仓库内**（而非 AI 运行环境的私有记忆区），随代码一并进入版本控制。

开始任何开发前，必须先完整读取仓库根 [`Agent.md`](../Agent.md)，再按
[`guidelines/00-index.md`](guidelines/00-index.md) 的顺序加载全部一级规范。

记忆分为两级，**优先级从高到低**：

## 一级：项目规范及开发守则（`memory/guidelines/`）

> **最高优先级。** 任何开发行为都必须先服从这里的规范。

存放长期、稳定、具有约束力的内容：开发规范、架构约定、命名规则、提交流程、
技术决策（ADR）等。当某次改动**涉及对开发规范本身的修改**时，必须**及时**
同步更新这里的对应文件——这属于改动的一部分，不能延后。

入口见 [`guidelines/00-index.md`](guidelines/00-index.md)。

## 二级：常规长期记忆 / 变更记录（`memory/changelog/`）

存放**编辑与修改的记录**，用于后续溯源：某次提交做了什么、为什么、影响了哪些
文件、有哪些注意事项。

- **不涉及**开发规范修改的常规文件改动：只需在此处记录即可。
- **涉及**开发规范修改的改动：既要更新一级规范，**也要**在此处留下变更记录
  （并在记录中指明同步更新了哪份规范）。

命名格式：**`YYYY-MM-DD-<commithash>.md`**，其中 `<commithash>` 是该记录所
描述的那次提交的短哈希（`git rev-parse --short HEAD`）。

> 由于提交哈希在提交完成后才存在，写作时机与文件名的处理方式见
> [`guidelines/commit-and-memory-workflow.md`](guidelines/commit-and-memory-workflow.md)。

## 优先级与冲突解决

1. **一级规范** 永远优先于二级记录。
2. 若二级记录与一级规范冲突，以一级规范为准，并应修正记录或补充说明。
3. 任何被验证为**错误**的记忆都应被更正或删除，而非保留误导。

## Unturned 权威外部参考

- [`SmartlyDressedGames/U3-SDK`](https://github.com/SmartlyDressedGames/U3-SDK) 是
  Smartly Dressed Games 公布的 Unturned 3 源码。调查游戏运行时行为、资产加载、专用服务器
  差异和 `SDG.Unturned` API 时，应优先核对该仓库与
  [Unturned 官方文档](https://docs.smartlydressedgames.com/)，再参考社区资料。
- 源码结论必须注明所核对的游戏版本、tag 或 commit；`main` 的最新实现不应被无条件套用到版本
  不同的生产服务器。2026-07-19 物品图片预研核对的快照为
  `21dd044d31f15b92a79fc351432714c95305603e`（仓库当时的 `main`）。
- U3-SDK 使用其自有许可证。可以把它作为 API 和行为的权威参考，但不要把其中源码、资源或图片
  直接复制进本仓库；确需派生或分发时，必须先逐项核对 U3-SDK License Agreement、版权声明和
  `THIRDPARTYNOTICES.txt` 要求。
