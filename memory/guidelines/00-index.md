# 项目规范及开发守则 — 索引（一级记忆）

> 最高优先级。开发前必读，开发中必守。修改规范须随对应改动同步提交。

## 项目开发前必读顺序（硬性）

1. 仓库根 [`Agent.md`](../../Agent.md)：会话加载自检、任务捕获与必读清单。
2. [`../README.md`](../README.md)：长期记忆分级、优先级和冲突处理。
3. 本索引列出的**全部一级规范**；不得只读与当前改动看似相关的一部分。
4. 涉及 WebPanel 或插件网页 UI 时，完整阅读仓库根 `DESIGN.md`。
5. 当前模块的 README、`docs/<PluginId>.md`、相关代码，以及最近与该模块有关的 changelog。
6. 开始修改前检查当前分支、工作区状态和 todo；不得覆盖用户已有改动。

每个新会话、上下文压缩/续接后都必须重新执行上述顺序。完成后首次回复输出
`核心规范已加载`，作为规范仍在上下文内的可见信号。

| 文件 | 内容 |
| --- | --- |
| [working-protocol.md](working-protocol.md) | **接到任务后如何推进**：提问只在开头、之后自动作业、禁止中途停下提问 |
| [development-standards.md](development-standards.md) | 编码规范、OpenMod 插件硬性约束、依赖与版本管理 |
| [architecture.md](architecture.md) | 仓库结构、多插件设计、共享环境的设计决策 |
| [commit-and-memory-workflow.md](commit-and-memory-workflow.md) | 提交流程、变更记录撰写规则、记忆同步规则 |
| [publishing.md](publishing.md) | NuGet / GitHub 按插件发布流程、CI、包元数据、密钥配置 |
| [testing.md](testing.md) | 测试项目放置/约定；Web UI 部署/发版前真实浏览器交互验收；**TestServer 只用于用户参与步骤，无人参与测试禁止启动** |
| [local-server-testing.md](local-server-testing.md) | 无人参与的服务器自测只用 `.localserver`，完成即关；TestServer 仅用于用户实机验收 |

## 修改规范的规则

- 当一次改动**改变了开发规范**（约定、流程、版本策略、目录结构等），必须在
  **同一次改动**中更新本目录下相应文件——规范更新是改动的一部分，不可延后。
- 规范变更同样需要在 `memory/changelog/` 留下变更记录，并在记录中注明
  「同步更新了哪份规范、改了什么、为什么」。
- 新增一类长期约定时，可新建独立的 guideline 文件，并在本索引登记。
