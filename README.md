# unturned-mods

基于 [OpenMod](https://openmod.github.io/) 开发的 **Unturned** 插件集合（多插件
monorepo）。各插件彼此独立、可单独构建与部署，但共享统一的构建与运行环境。

> 本仓库完全由 AI 开发。开发规范与变更历史以仓库内长期记忆为准，见
> [`memory/`](memory/README.md)。AI 协作入口见 [`CLAUDE.md`](CLAUDE.md)。

## 仓库结构

```
├── UnturnedMods.sln          # 解决方案
├── Directory.Build.props     # 全仓库共享 MSBuild 属性
├── Directory.Packages.props  # 集中式依赖版本管理 (CPM)
├── global.json / nuget.config
├── src/
│   ├── Shared/UnturnedMods.Shared/   # 跨插件共享库
│   └── plugins/<PluginId>/           # 各插件
├── build/templates/plugin/   # 插件模板
├── scripts/new-plugin.sh     # 插件脚手架
└── memory/                   # 长期记忆（规范 + 变更记录）
```

详见 [`memory/guidelines/architecture.md`](memory/guidelines/architecture.md)。

## 插件

| 插件 | 说明 |
| --- | --- |
| `well404.Economy` | 经济系统：以全局 `IEconomyProvider` 暴露货币；后端可选 serverless LiteDB 账本或原生经验值；支持 `/pay` 转账、`/balance`、`/eco` 管理、以及击杀（玩家/僵尸/动物）奖励。其他插件（签到等）可注入 `IEconomyProvider` 发币。 |
| `well404.Shop` | 商店：`config.yaml` 配置商品价格，支持单物品与自定义组合包；`/buy` `/sell` `/shop`；按权限组的购买折扣（如 VIP 9 折，默认关闭）。依赖任一 `IEconomyProvider` 实现。 |

## 快速开始

> 需要 .NET SDK（建议 8.0 LTS）；当前环境已装 SDK 8.0.127。

```bash
# 新建一个插件
scripts/new-plugin.sh well404.MyPlugin "My Plugin"

# 构建全部
dotnet build UnturnedMods.sln -c Release
```

构建产物为各插件的 `.nupkg`（`GeneratePackageOnBuild=true`）。部署到服务器：
`openmod install <PackageId>`，或将插件 dll 及依赖放入 `openmod/plugins/` 后
`openmod reload`。

## 开发约定

所有规范见 [`memory/guidelines/`](memory/guidelines/00-index.md)，关键点：

- 目标框架固定 `netstandard2.1`；`RootNamespace == AssemblyName`。
- 依赖版本集中在 `Directory.Packages.props`，插件不写 `Version`。
- 每次提交都要写变更记录（`memory/changelog/`），涉及规范的改动须同步更新规范。
