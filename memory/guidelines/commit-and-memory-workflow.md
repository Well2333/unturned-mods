# 提交流程与记忆同步规则（一级记忆）

本文件规定：每次改动如何撰写变更记录、如何处理「文件名含提交哈希」的先后矛盾、
以及何时必须同步更新一级规范。

## 最高红线：推送与发版需用户当次明确授权

- **`git push`（推送 GitHub）、创建 tag / GitHub Release、发布 NuGet
  （`scripts/release-plugin.sh`、publish workflow 等）只能在用户当次明确要求时执行，
  严禁擅自进行。** 「之前授权过一次」不等于持续授权——每次都要有当次的明确指令。
- 本地 `git commit` 与变更记录可按下方流程进行，但提交后**停在本地**，
  不推送、不发版，等待用户指示。若不确定，先问。

## 核心原则

1. **每次提交都要在 `memory/changelog/` 留下一条变更记录。**
2. 变更记录文件名为 **`YYYY-MM-DD-<commithash>.md`**，`<commithash>` 指向**该记录
   所描述的那次功能提交**的短哈希。
3. 若改动**涉及开发规范**，必须在 `memory/guidelines/` 同步更新相应文件，并在
   变更记录里注明同步了什么。
4. 记录内容应**在提交前依据 diff 撰写**（即「根据其内容撰写」）；哈希在功能提交
   产生后回填到文件名（见下）。

## 标准流程（两步提交，保证文件名哈希精确）

由于短哈希只有在提交完成后才存在，采用「功能提交 + 紧随其后的记录提交」：

```bash
# 1) 暂存并完成功能提交（提交信息即变更记录的浓缩）
git add -A
git commit -m "feat(<scope>): <简述>"

# 2) 取得刚刚那次提交的短哈希
HASH=$(git rev-parse --short HEAD)
DATE=$(date +%F)              # YYYY-MM-DD

# 3) 按 diff 撰写变更记录，文件名指向上一步的提交
#    （用模板填好内容后写入）
#    memory/changelog/${DATE}-${HASH}.md

# 4) 提交这条记录
git add memory/changelog/${DATE}-${HASH}.md
git commit -m "docs(memory): changelog for ${HASH}"
```

> 这样，`changelog/YYYY-MM-DD-<hash>.md` 的文件名始终精确对应它所描述的功能
> 提交，溯源无歧义。记录提交本身保持极简、不再为它单独写记录（避免无限递归）。

## 变更记录模板

```markdown
---
date: YYYY-MM-DD
commit: <short-hash>
type: feature | fix | refactor | chore | docs | guideline
scope: <插件Id 或 仓库范围，如 well404.AutoMessage / repo>
guideline_changed: false   # 若改了一级规范则为 true，并在下方说明
---

# <一句话标题>

## 做了什么
- ...

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
