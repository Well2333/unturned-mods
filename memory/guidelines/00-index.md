# 项目规范及开发守则 — 索引（一级记忆）

> 最高优先级。开发前必读，开发中必守。修改规范须随对应改动同步提交。

| 文件 | 内容 |
| --- | --- |
| [development-standards.md](development-standards.md) | 编码规范、OpenMod 插件硬性约束、依赖与版本管理 |
| [architecture.md](architecture.md) | 仓库结构、多插件设计、共享环境的设计决策 |
| [commit-and-memory-workflow.md](commit-and-memory-workflow.md) | 提交流程、变更记录撰写规则、记忆同步规则 |
| [publishing.md](publishing.md) | NuGet / GitHub 按插件发布流程、CI、包元数据、密钥配置 |
| [testing.md](testing.md) | 测试项目放置/约定、只测游戏无关纯逻辑的原则 |

## 修改规范的规则

- 当一次改动**改变了开发规范**（约定、流程、版本策略、目录结构等），必须在
  **同一次改动**中更新本目录下相应文件——规范更新是改动的一部分，不可延后。
- 规范变更同样需要在 `memory/changelog/` 留下变更记录，并在记录中注明
  「同步更新了哪份规范、改了什么、为什么」。
- 新增一类长期约定时，可新建独立的 guideline 文件，并在本索引登记。
