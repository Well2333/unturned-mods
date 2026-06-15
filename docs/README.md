# 文档

各插件的用法、配置示例与本地开发说明。

| 插件 | 文档 |
| --- | --- |
| `well404.Economy` | [经济系统](well404.Economy.md) — 货币、转账、击杀奖励、`IEconomyProvider` 对接 |
| `well404.Shop` | [商店](well404.Shop.md) — 商品/组合包、买卖、权限折扣 |
| `well404.WebPanel` | [Web 管理面板](well404.WebPanel.md) — 通用可视化管理面板(供各插件挂载模块)+ 面向玩家的网页界面(`/menu`:服务器介绍、商店买卖、钱包转账、实用工具);路径式 token 鉴权 + 可选内置反代(cloudflared/ngrok);**网页中英双语可切换** |
| `well404.Essentials` | [实用功能](well404.Essentials.md) — 面向玩家的 home/tp/warp/gift/sleep/back/party，统一传送规则，经济收费可选;玩家网页「实用工具」 |
| `well404.AdminTools` | [管理员工具](well404.AdminTools.md) — 无敌、踢出、临时封禁/解封;命令 + 管理面板 |
| `well404.Vault` | [私人仓库](well404.Vault.md) — 玩家存取背包物品(完整保真,按背包格子计容量);命令 + 玩家网页仓库 |

> `well404.Shop` **硬依赖** `well404.Economy`(由它提供 `IEconomyProvider` 结算交易)。
> 该依赖已写入 Shop 的 NuGet 包,`openmod install well404.Shop` 会自动一并安装 Economy。
>
> `well404.WebPanel` 是可选的基础设施插件:装上它后,Economy / Shop / Essentials / AdminTools
> 会自动出现可视化管理模块;不装也不影响这些插件通过命令工作。

> **多语言(i18n)**:**网页**(管理面板 + 玩家面板)内置中/英双语,右上角下拉即可切换,默认英文;
> 各插件以「英文源串为键 + 中文映射表」提供翻译,新增语言只需再加一张映射表。**游戏内提示**走 OpenMod
> 的 `translations.yaml`(出厂英文),服务器管理员可整文件翻译/替换为任意语言来统一设置游戏内语言。

## 安装

> **前置**:`openmod install` 会读取服务器 `openmod.yaml` 里的 `nuget:install:allowedActors`。
> 若该段缺失(旧配置),命令会以 `ArgumentNullException` 报错。补上即可:
> ```yaml
> nuget:
>   install:
>     allowedActors:
>     - "*"
> ```

二选一：

- **从 NuGet 安装（推荐）**，自动解析依赖（如 LiteDB,以及 Shop 会带上 Economy）：

  ```bash
  openmod install well404.Economy
  openmod install well404.Shop      # 自动一并安装 well404.Economy
  openmod install well404.WebPanel  # 可选:可视化管理面板
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

**产物布局（扁平）**：`<输出目录>/<PluginId>.dll` + 非宿主第三方依赖（如
`well404.Economy` 会带 `LiteDB.dll`；`well404.Shop` 只有插件 dll），全部平铺。
**必须扁平**：OpenMod 的插件加载器只扫描 `plugins/*.dll` 顶层（不递归子目录），所以
服务器 `openmod/plugins/` 的布局就等于本输出目录。构建**前清除**上次同名残余、**后只
留目标文件**（中间产物在临时目录丢弃）；默认 `build/` 全量构建时会清掉旧产物但保留
版本控制的 `templates/`（构建产物已 `.gitignore`）。部署时把这些 dll 平铺复制进
`openmod/plugins/`，OpenMod 会自动在 `plugins/<PluginId>/` 下生成各插件的
`config.yaml`/`translations.yaml`（及如经济库 `economy.db`）。

`scripts/build.sh --help` 查看全部选项。其他脚本：

- `scripts/new-plugin.sh <PluginId> ["Display Name"]` — 新建插件脚手架。
- `scripts/plugin-version.sh <PluginId> [newVersion]` — 读取/设置插件版本。
- `scripts/release-plugin.sh <PluginId>` — 打 tag `<PluginId>/v<Version>` 并发版。

## 权限

OpenMod 命令权限形如 `<PluginId>:commands.<command>`（子命令追加路径），需在 OpenMod
的权限角色里授予。各命令的权限串见对应插件文档。
