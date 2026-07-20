---
date: 2026-07-17
branch: feat/warp-multi-tags
merge_commit: pending
type: feat
scope: well404.Essentials
guideline_changed: false
---

# 传送点多标签过滤与共享冷却

## 做了什么

- 将传送点从互斥的单个 `category` 改为可同时拥有多个 `tags`，管理员与玩家界面均按标签过滤同一份全局目录。
- `/warp set <名称> [标签 ...]` 支持追加多个空格分隔标签；不带标签更新已有传送点时保留原标签。
- 移除单个传送点的冷却字段，命令和玩家 WebUI 的所有传送点统一使用 `teleport.cooldownSeconds` 与共享的 `warp` 冷却桶。
- 旧 `category` 自动迁移成标签，缺少标签时归入 `default`；标签统一小写、去重，并保留全局排序。
- 玩家页将 home/back 的快捷传送与传送点目录分开；标签按钮显示匹配数量，筛选只影响传送点，不会隐藏快捷操作。

## 为什么

- 原分类模型使一个传送点只能出现在一个分页中，不符合“一个地点可同时属于城市、安全区、公共点”等多维过滤需求。
- 原单点冷却增加了配置复杂度，同一玩家还可通过切换传送点绕过预期的 warp 冷却。

## 影响的文件 / 范围

- `src/plugins/well404.Essentials/` 的传送点模型、配置存储、YAML、命令、传送服务与自建 Web UI。
- `src/plugins/well404.Essentials/README.md`、`docs/well404.Essentials.md`。
- `tests/well404.Essentials.Tests/` 的标签迁移和 UI 资源断言源码。

## 注意事项 / 后续

- 旧配置无需手工迁移；下一次配置重写会输出 `tags` 并移除旧 `category` 与传送点级 `cooldownSeconds`。
- `well404.Essentials.Tests` 共 43 项通过；管理员与玩家自建 UI 均通过真实 Chrome 交互验证。
- 本功能未发布版本。远程部署和生命周期记录仅保存在项目私有 .local-memory/changelog.md。
