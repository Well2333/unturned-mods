# unturned-mods

> 🌐 **English:** see [README.en.md](README.en.md) for an English overview.

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
| `well404.Shop` | 商店：`config.yaml` 配置商品价格，支持单物品与自定义组合包；`/buy` `/sell` `/shop`；按权限组的购买折扣（如 VIP 9 折，默认关闭）。**硬依赖 `well404.Economy`**,`openmod install` 会自动一并安装。 |
| `well404.WebPanel` | 通用 Web 管理面板：内置零依赖 HTTP 服务 + 单页应用,经 `IWebPanelRegistry` 让各插件挂载可视化管理模块(设置组、集合 CRUD、搜索)。可选,装上后 Economy / Shop / Essentials 自动出现管理界面。 |
| `well404.Essentials` | 面向玩家的实用指令合集：`/home`、`/tp`+`/tpa`/`/tpd`（同队直传、跨队需确认）、`/warp`、`/gift`（crontab 刷新+VIP 权限）、`/sleep`（投票切换昼夜）、`/back`（回死亡点+无敌）。所有传送共用「预热静止+可选经济收费+冷却」。经济收费**可选依赖**任一 `IEconomyProvider`（默认免费）；设置项在装 WebPanel 时可在 WebUI 编辑。 |
| `well404.Vault` | 私人仓库：每位玩家把背包物品存入 / 取出个人仓库（`/vault store`/`take`/`list` 或网页）。**完整保真**保存（耐久、配件、弹匣/弹药箱内弹药等），容量按**背包格子数**计（物品按 `宽×高` 占格，内部堆叠/弹药数不计入）。无硬依赖；装 WebPanel 时提供网页仓库与容量设置。 |
| `well404.AutoSave` | 定时保存 + 备份：按 **crontab 墙钟**触发 `SaveManager.save()`（默认每 10 分钟），每保存 N 次顺带做一次 **LZMA 实体压缩**备份（`.tar.lz`）。备份目录、排除规则（默认排除创意工坊/包缓存/日志等可下载内容）、保留上限（最大数量 / 总体积）均可配。**无游戏内指令**，仅 `config.yaml` + WebPanel。 |

## 快速开始

> 需要 .NET SDK（建议 8.0 LTS）；当前环境已装 SDK 8.0.127。

```bash
# 新建一个插件
scripts/new-plugin.sh well404.MyPlugin "My Plugin"

# 构建全部 -> 默认平铺输出到 build/（插件 dll + 非宿主第三方依赖，如 LiteDB.dll）
scripts/build.sh
scripts/build.sh --test          # 构建 + 跑单元测试

# 直接输出/部署到本地测试服务器（-o/--deploy 覆盖输出目录；OpenMod 要求 dll 平铺）
scripts/build.sh well404.Economy --deploy /path/to/server/openmod/plugins
```

构建产物为各插件的 `.nupkg`（`GeneratePackageOnBuild=true`）。部署到服务器：
`openmod install <PackageId>`，或将插件 dll 及依赖放入 `openmod/plugins/` 后
`openmod reload`。

**各插件的详细用法、配置示例与本地调试见 [`docs/`](docs/README.md)。**

## 开发约定

所有规范见 [`memory/guidelines/`](memory/guidelines/00-index.md)，关键点：

- 目标框架固定 `netstandard2.1`；`RootNamespace == AssemblyName`。
- 依赖版本集中在 `Directory.Packages.props`，插件不写 `Version`。
- 每次提交都要写变更记录（`memory/changelog/`），涉及规范的改动须同步更新规范。
