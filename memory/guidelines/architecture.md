# 架构与仓库结构（一级记忆）

本仓库是一个**多插件 monorepo**：包含若干彼此独立的 OpenMod Unturned 插件，
它们共享一致的构建/运行环境。设计目标是「每个插件可独立构建与部署，但全仓库
环境统一、复用最大化」。

## 目录结构

```
unturned-mods/
├── UnturnedMods.sln              # 汇总所有项目的解决方案
├── Directory.Build.props         # 全仓库共享的 MSBuild 属性（统一环境）
├── Directory.Packages.props      # 集中式依赖版本管理（CPM）
├── global.json                   # 固定 .NET SDK 版本带
├── nuget.config                  # NuGet 源
├── src/
│   ├── Shared/
│   │   └── UnturnedMods.Shared/  # 跨插件共享库
│   └── plugins/
│       └── <PluginId>/           # 每个插件一个目录（脚手架生成）
├── build/
│   └── templates/plugin/         # 新建插件用的模板
├── scripts/
│   └── new-plugin.sh             # 插件脚手架
├── docs/                         # 面向人的文档（可选）
└── memory/                       # 长期记忆（见 memory/README.md）
    ├── guidelines/               # 一级：项目规范及开发守则
    └── changelog/                # 二级：变更记录
```

## 共享一致环境的设计决策

> 这些决策是「环境一致」目标的落地方式，修改前请评估对所有插件的影响。

- **`Directory.Build.props`**：在仓库根集中设定 `TargetFramework`、`Nullable`、
  `LangVersion`、警告策略、包元数据等。所有项目自动继承，避免逐插件漂移。
- **Central Package Management（`Directory.Packages.props`）**：所有插件锁定到
  同一组依赖版本，确保运行时环境一致。
- **命名即约束**：插件项目文件名 = 插件 Id，从而自动满足
  `RootNamespace == AssemblyName`（OpenMod 的硬性要求）。
- **共享库 `UnturnedMods.Shared`**：放置公共基类、扩展方法、工具与抽象。注意：
  插件引用它时，编译出的 `UnturnedMods.Shared.dll` 需与插件一起部署到
  `openmod/plugins`。
- **脚手架 `scripts/new-plugin.sh`**：保证每个新插件结构、约束一致，降低 AI
  逐个手写时的偏差。

## 每个插件目录的标准布局

```
src/plugins/<PluginId>/
├── <PluginId>.csproj            # 文件名 = 插件 Id（满足命名约束）
├── <Class>Plugin.cs             # 继承 OpenModUnturnedPlugin + PluginMetadata
├── config.yaml                  # EmbeddedResource
└── translations.yaml            # EmbeddedResource
```
