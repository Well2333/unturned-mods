# 文档

各插件的用法、配置示例与本地开发说明。

| 插件 | 文档 |
| --- | --- |
| `well404.Economy` | [经济系统](well404.Economy.md) — 货币、转账、击杀奖励、`IEconomyProvider` 对接 |
| `well404.Shop` | [商店](well404.Shop.md) — 商品/组合包、买卖、权限折扣 |

> `well404.Shop` 依赖任一 `IEconomyProvider` 实现（即 `well404.Economy` 或兼容经济
> 插件）提供货币，需与之同时安装。

## 安装

二选一：

- **从 NuGet 安装（推荐）**，自动解析依赖（如 LiteDB）：

  ```bash
  openmod install well404.Economy
  openmod install well404.Shop
  openmod reload
  ```

- **手动放 dll**：把插件 dll 及其第三方依赖 dll 放入服务器的 `openmod/plugins/`。
  用下面的脚本可一键生成「最小依赖集」。

首次加载后，会在 `openmod/plugins/<PluginId>/` 生成可编辑的 `config.yaml` 与
`translations.yaml`；改完执行 `openmod reload`。

## 本地构建与调试

需要 .NET SDK 8.0（LTS）。仓库根的 `scripts/build.sh` 封装了常用操作：

```bash
# 构建全部 -> 默认输出到仓库根的 build/<PluginId>/
scripts/build.sh

# 构建 + 跑单元测试
scripts/build.sh --test

# 只构建某个插件（Debug，便于调试）
scripts/build.sh well404.Economy -c Debug

# 直接输出/部署到本地测试服务器的 plugins 目录（-o 或 --deploy 覆盖输出目录）：
scripts/build.sh well404.Economy --deploy /path/to/server/openmod/plugins
# 然后在服务器控制台执行： openmod reload
```

**产物布局**：`<输出目录>/<PluginId>/<PluginId>.dll`（+ 非宿主第三方依赖，如
`well404.Economy` 会带 `LiteDB.dll`；`well404.Shop` 只有插件 dll）。每个插件的输出
目录会在构建**前清空**上次残余、构建**后只保留目标文件**（中间产物在临时目录里丢弃）。
默认 `build/` 目录只保留版本控制的 `templates/` 与新构建出的插件目录（构建产物已
`.gitignore`）。部署时把 `<输出目录>/<PluginId>/` 整个复制进服务器
`openmod/plugins/`。

`scripts/build.sh --help` 查看全部选项。其他脚本：

- `scripts/new-plugin.sh <PluginId> ["Display Name"]` — 新建插件脚手架。
- `scripts/plugin-version.sh <PluginId> [newVersion]` — 读取/设置插件版本。
- `scripts/release-plugin.sh <PluginId>` — 打 tag `<PluginId>/v<Version>` 并发版。

## 权限

OpenMod 命令权限形如 `<PluginId>:commands.<command>`（子命令追加路径），需在 OpenMod
的权限角色里授予。各命令的权限串见对应插件文档。
