# AI Agent 项目开发前必读清单

> **本文件是每个新会话、上下文压缩/续接以及新 Agent 开始开发时的最高优先级启动入口。**
> 详细规则只在 `memory/guidelines/` 维护；本文件负责加载顺序和执行自检，避免重复正文漂移。

## 0. 强制加载信号

完成本清单后，该段会话的首次用户可见回复必须独立成行输出：

`核心规范已加载`

若遗漏，视为规范可能已从上下文丢失：立即停止开发动作，重新执行本清单后再继续。

## 1. 开发前必读顺序（全部必读）

- [ ] 完整阅读本文件，不依赖旧会话记忆或摘要代替。
- [ ] 阅读 [`memory/README.md`](memory/README.md)，确认长期记忆层级与冲突处理。
- [ ] 阅读 [`memory/guidelines/00-index.md`](memory/guidelines/00-index.md)。
- [ ] 涉及 WebPanel 或插件网页 UI 时，完整阅读根目录 [`DESIGN.md`](DESIGN.md)。
- [ ] 按索引完整阅读**全部**一级规范：
  - [ ] [工作协作协议](memory/guidelines/working-protocol.md)
  - [ ] [开发规范](memory/guidelines/development-standards.md)
  - [ ] [架构与仓库结构](memory/guidelines/architecture.md)
  - [ ] [提交与记忆同步](memory/guidelines/commit-and-memory-workflow.md)
  - [ ] [发布规范](memory/guidelines/publishing.md)
  - [ ] [测试规范](memory/guidelines/testing.md)
  - [ ] [本地服务器测试](memory/guidelines/local-server-testing.md)
- [ ] 若 `.local-memory/` 存在，完整读取其中的本地专有记忆。该目录被 Git 排除，内容不得加入
  commit、补丁、日志、公开文档、远程消息或任何发布产物；只有用户明确要求时才能使用其中记录的远程能力。
- [ ] 阅读当前模块 README、对应 `docs/<PluginId>.md`、相关代码和最近相关 changelog。
- [ ] 检查当前分支、`git status` 与现有 diff，识别并保护用户已有改动。
- [ ] 建立/更新 todo，确保原任务和执行过程中追加的需求都已登记并重新排序。

## 2. 开工前红线自检

- [ ] 提问只允许集中在任务开头；开工后凡可合理自决的事项必须自驱完成并在交付时说明。
- [ ] 用户追加、修改或撤销需求时，立即加入 todo、重新排序，逐项给出完成或受阻结论。
- [ ] `git push`、远程分支、tag、GitHub Release、NuGet 发布等远程写入，只能依据**当前成果完成后的当次明确授权**；历史授权、旧需求授权均无效。用户说“不要发”后必须等待新的明确授权。
- [ ] 每项工作在类型分支开发，合入 `main` 使用 squash；不得覆盖无关或来源不明的工作区改动。
- [ ] 面向用户的命令、配置、权限、WebUI、依赖等变化，必须同步 README 和详细文档。
- [ ] WebPanel 配置与功能必须通过通用描述符和可选注册实现；宿主不得写具体插件 id 或业务特判。
- [ ] 触及 Unturned/Unity API 前切换主线程；跨插件依赖抽象，遵守 Shared 的唯一程序集规则。
- [ ] 本地迥代默认只做 Release 构建；只有用户明确要求测试时，才运行单元/完整/服务器测试。构建不等于测试授权。
- [ ] **TestServer 只用于需要用户本人参与的实机检查。** 纯自动化、加载、日志、HTTP、命令等
  非玩家参与验证必须使用本地 `.localserver`，不得为此启动远程 `TestServer`；任何测试服若没有
  正在等待用户参与的步骤，使用完毕必须立即关闭，禁止常驻。

## 3. 完成前检查

- [ ] 重新核对 todo，所有需求均有完成或受阻说明。
- [ ] 审查 diff、版本、文档、配置迁移、依赖与打包内容，确保没有调试残留或意外文件。
- [ ] 依用户授权范围执行构建/测试；未获测试授权时只构建，未获远程授权时停留在本地。
- [ ] 按 [提交与记忆同步规则](memory/guidelines/commit-and-memory-workflow.md) 收敛分支 changelog；规范变化标记 `guideline_changed: true`。
- [ ] 最终回复明确列出完成内容、构建/测试实际执行范围、未执行项和发布状态。

## 4. 规则维护

- `memory/guidelines/` 是详细规则的唯一事实来源；本清单只保留启动顺序和红线摘要。
- 规范改变时，在同一改动中更新对应 guideline、本索引及分支 changelog。
- 若本清单与详细 guideline 表述不一致，先停止有冲突的动作，按更严格约束执行，并在当前改动中消除漂移。
- 被实践证伪的长期记忆必须立即更正或删除，不保留误导。
