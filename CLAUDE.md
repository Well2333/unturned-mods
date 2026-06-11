# CLAUDE.md

本仓库基于 **OpenMod** 开发 **Unturned** 插件，是一个**多插件 monorepo**，
**完全由 AI 开发**。

## 开工前必读（最高优先级）

1. 先读 [`memory/README.md`](memory/README.md) 了解两级长期记忆系统。
2. 再读一级记忆 [`memory/guidelines/`](memory/guidelines/00-index.md)：
   - [开发规范](memory/guidelines/development-standards.md)
   - [架构与仓库结构](memory/guidelines/architecture.md)
   - [提交流程与记忆同步规则](memory/guidelines/commit-and-memory-workflow.md)

**一级规范优先于一切**，包括本文件中的概述。

## 工作准则（摘要，细节以 guidelines 为准）

- 新建插件用 `scripts/new-plugin.sh <PluginId> ["Display Name"]`，不要手搓目录。
- 依赖版本集中在 `Directory.Packages.props`；插件 `.csproj` 不写 `Version`。
- 目标框架固定 `netstandard2.1`；`RootNamespace == AssemblyName`；
  `config.yaml`/`translations.yaml` 为 EmbeddedResource。
- **每次提交都要写变更记录**到 `memory/changelog/YYYY-MM-DD-<commithash>.md`，
  按「两步提交」流程操作。
- **凡改动涉及开发规范，必须同步更新 `memory/guidelines/` 对应文件。**
- **凡改动插件面向用户的行为（命令/配置/权限/Web 面板/依赖），必须同一次提交内同步
  更新该插件的 `README.md`、`docs/<PluginId>.md` 及总表**（见 development-standards.md §6）。

## 当前环境状态

- 已安装 .NET **SDK 8.0.127**（含运行时），可直接 `dotnet build` / `dotnet pack`。
  `UnturnedMods.sln` 当前为绿色（`dotnet build -c Release` 0 警告 0 错误）。
