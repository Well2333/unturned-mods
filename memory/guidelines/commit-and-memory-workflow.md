# 提交流程与记忆同步规则（一级记忆）

本文件规定：每次改动如何撰写变更记录、如何处理「文件名含提交哈希」的先后矛盾、
以及何时必须同步更新一级规范。

## 最高红线：推送与发版需用户当次明确授权

- **`git push`（推送 GitHub）、创建 tag / GitHub Release、发布 NuGet
  （`scripts/release-plugin.sh`、publish workflow 等）只能在用户当次明确要求时执行，
  严禁擅自进行。** 「之前授权过一次」不等于持续授权——每次都要有当次的明确指令。
- **需求被修改、扩展或重新定义后，此前针对旧成果给出的推送/发版授权立即失效。**
  完成新需求后只能保留本地代码、测试和提交；不得根据同一会话更早的“发布”指令自行推送。
- **用户明确说“不要发”“等我要求再发”即撤销当前全部远程写入授权。** 撤销后必须等待
  用户在成果完成并确认后再次明确说“现在提交并发布/现在推送”等，才能执行 `git push`、
  创建远程分支/tag/GitHub Release 或触发 NuGet 发布；本地 `git commit` 不属于远程发布。
- 本地 `git commit` 与变更记录可按下方流程进行，但提交后**停在本地**，
  不推送、不发版，等待用户指示。若不确定，先问。

## 分支工作流与 squash 合并（强制）

1. **每项工作开新分支**：新功能、新插件、bug 修复、文档/规范改动等，都**先开一条新分支**
   再动手，不在 `main` 上直接开发。命名用类型前缀：`feat/<简述>`、`fix/<简述>`、
   `chore/<简述>`、`docs/<简述>`。
2. **尽量不直接 commit 到 `main`**：除非是「极小且必要、不值得单开分支」的改动（如紧急小修），
   否则一律走分支。拿不准就开分支。
3. **合并回 `main` 一律用 squash merge**：一条分支 → `main` 上**一个**提交，避免开发过程中的
   零碎/试错提交污染主线历史。可用本地 `git merge --squash <branch>` 后提交，或 GitHub PR 的
   *Squash and merge*。squash 提交信息写成该分支工作的浓缩（即变更记录的概要）。
4. **changelog 跟随 squash 提交**：仍按下方「两步提交」，但此处的「功能提交」= **squash 合并到
   `main` 的那个提交**；变更记录文件名用**该 squash 提交**的短哈希。分支内开发期间可暂不写或先
   起草 changelog，最终以 squash 提交为准（避免文件名哈希指向被压扁的分支提交）。
5. **合并后删除分支**（**本地 + 远程**）：`git branch -d <b>` 与
   `git push origin --delete <b>`（或 PR 合并时勾选自动删除分支）。已打 tag/Release 的提交不会
   因删分支而丢失（tag 会保活该提交）。

> 推送 `main` / 删除远程分支属「写远程」，与发版同受**最高红线**约束：需用户当次明确授权。

## 核心原则

1. **一个开发分支最终只保留一条正式 changelog**，对应其合入 `main` 的 squash 功能提交；
   不再要求分支内每个临时提交各写一条正式记录。
2. 正式记录位于 `memory/changelog/YYYY-MM-DD-<commithash>.md`，其中哈希指向
   `main` 上该分支的 squash 功能提交。
3. 分支开发期间如需记录中间状态，只能放在 `memory/changelog/_wip/`；合并时汇总为一条
   正式记录并删除该分支的全部 WIP 草稿。
4. 若改动涉及开发规范，必须在同一分支同步更新 `memory/guidelines/`，并在正式记录中标记
   `guideline_changed: true`、逐项说明。

## 标准收敛流程

1. 在类型分支完成代码、文档和规范更新；中间提交可有可无，不生成顶层正式 changelog。
2. 合入前根据**整个分支相对 main 的 diff**起草一条汇总记录；如使用 `_wip/`，同时归并并删除
   此分支草稿。
3. 在 `main` 上执行 squash 合并并创建功能提交。
4. 获取功能提交短哈希，将汇总记录命名为 `memory/changelog/YYYY-MM-DD-<hash>.md`，再用一个
   极简 `docs(memory)` 提交写入记录。记录提交本身不再生成记录，避免递归。
5. 只有用户针对当前成果当次明确授权时，才可推送 `main`、删除远程分支或发版。

示例：

```bash
git switch main
git merge --squash feat/example
git commit -m "feat(scope): summary"
git rev-parse --short HEAD  # 记下输出为 HASH
# 撰写 memory/changelog/YYYY-MM-DD-<HASH>.md
git add memory/changelog/YYYY-MM-DD-<HASH>.md
git commit -m "docs(memory): changelog for <HASH>"
```

## 变更记录模板

```markdown
---
date: YYYY-MM-DD
branch: <分支名>
merge_commit: <main 上的 squash 短哈希>
type: feature | fix | refactor | chore | docs | guideline
scope: <插件Id 或仓库范围>
guideline_changed: false
---

# <一句话标题：本分支整体做了什么>

## 做了什么
- ...（汇总本分支全部要点）

## 为什么
- ...

## 影响的文件 / 范围
- ...

## 注意事项 / 后续
- ...

## 规范同步（仅当 guideline_changed = true）
- 更新了 `memory/guidelines/<file>`：<改了什么、为什么>
```

## 何时必须更新一级规范（guidelines）

出现以下情况时，**同一次改动内**必须更新 `memory/guidelines/`：

- 改变了目录结构、构建方式、依赖/版本策略；
- 改变了编码约定、命名约定、提交流程本身；
- 引入或废弃了某项长期技术决策。

## 提交前文档自检（强制）

改动若涉及某插件**面向用户的行为**(命令 / 配置 / 权限 / Web 面板 / 依赖关系),
**同一次提交内**必须同步更新对应文档,否则视为未完成(详见
`development-standards.md` §6):

- [ ] `src/plugins/<PluginId>/README.md`(随 NuGet 包发布)
- [ ] `docs/<PluginId>.md`
- [ ] 根 `README.md` / `docs/README.md` 的插件总表(增删插件或依赖变化时)

规范更新与功能改动可在同一次功能提交中完成；变更记录需在 `guideline_changed`
标记为 `true` 并说明同步内容。
