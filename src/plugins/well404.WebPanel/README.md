# well404.WebPanel

> Unturned / OpenMod 通用 Web 管理面板 —— 一个 schema 驱动的单页面板,让各功能插件挂载自己的可视化管理模块。

`well404.WebPanel` 是 **well404 OpenMod 插件家族** 的基础设施插件。它内置一个轻量
HTTP 服务(基于 BCL `HttpListener`,无外部依赖)与单页应用,并对外暴露 `IWebPanelRegistry`;
[well404.Economy](https://www.nuget.org/packages/well404.Economy/)、
[well404.Shop](https://www.nuget.org/packages/well404.Shop/) 等插件在加载时把自己的管理
模块注册进来,面板即可通用地渲染出表单、设置组、集合 CRUD 与搜索框,而无需了解各插件实现。

## 功能

- 🌐 内置 HTTP 服务 + 单页管理界面,零外部依赖
- 🧩 `IWebPanelRegistry`:其他插件按统一 schema 注册管理模块(Settings / Collection / Search / Form / Table)
- 🔐 路径式 token 鉴权(`/<token>/`,管理面强制有 token;留空则自动生成并写回配置)
- 🌐 内置反代/隧道(可选):一键经 cloudflared / ngrok 把面板安全暴露到公网,隐藏真实 IP、自动 HTTPS
- 🛠️ 安装即用:装了哪个家族插件,面板里就出现对应模块
- 🎮 **面向玩家**的网页界面(`/p`):玩家游戏内 `/menu` 即可在浏览器里看服务器介绍页、逛商店买卖、查钱包转账、用实用工具(传送/组队/礼包/睡觉);各插件经 `IPlayerMenuRegistry` 注册「玩家菜单」
- 🌍 管理面板 + 玩家面板均**中英双语可切换**(默认英文);网页文案经 `IWebTranslationRegistry` 提供翻译,新增语言只需加一张映射表

## 安装

```
openmod install well404.WebPanel
```

重启服务器或执行 `openmod reload` 后,**启动日志会打印管理面地址** `http://<地址>:<端口>/<token>/`。

## 配置 (config.yaml)

```yaml
web:
  bindAddress: "127.0.0.1"   # 监听地址:127.0.0.1(仅本机)| 0.0.0.0(全部)| 指定 IP
  port: 27020                 # 监听端口
  token: ""                  # 管理面密钥;留空=首次启动随机生成 12 位并写回本文件
  tunnel:                    # 可选:内置反代,把面板安全暴露到公网
    enabled: false
    type: "cloudflare"       # cloudflare(装好 cloudflared 即用) | custom(自定义,适配 ngrok 等)
    command: "cloudflared"
  publicBaseUrl: ""          # 玩家 /menu 链接的公网地址;空=自动推导(开 tunnel 时用隧道地址)
  playerSessionMinutes: 5    # 玩家链接有效期下限;实际不少于 15 分钟
```

### 安全说明

- **路径式 token**:管理面始终在 `http://host:port/<token>/` 下,token 即鉴权;路径不对一律 404(不泄露面板是否存在)。
- **token 强制存在**:留空则自动生成 12 位大小写字母+数字并**写回 config.yaml**(重启不变);**不在 WebUI 中可改**,只能改配置文件。
- **内置反代**(`web.tunnel`):开启后经 cloudflared / ngrok 把面板反代到公网,**不暴露真实 IP、不开入站端口、自动 HTTPS**;隧道把整个端口反代出去,管理面就靠路径 token 保护,务必保密。
- **玩家面**(`/p`)用各玩家一次性短时令牌:**有效期至少 15 分钟,之后只要在线就有效,下线即失效**;与管理员 token 互相独立。

## 命令

| 命令 | 别名 | 说明 |
| --- | --- | --- |
| `/menu [tab]` | `/panel` | 向玩家推送其专属面板链接(可选 `tab` 定位标签,如 `/menu shop`) |

## HTTP 接口

管理面全部在 `/<token>/` 前缀下(下表省略前缀);玩家面在 `/p`、`/api/p/*`。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/<token>/` | 管理单页应用(API 用相对路径,自动带 token 前缀) |
| GET | `/<token>/api/modules` | 列出已注册模块及其字段 schema |
| GET/POST | `/<token>/api/modules/{module}/{action}[/values\|/records\|/delete]` | 预填 / 记录 / 提交 / 删除 |
| GET | `/p` | 玩家单页应用(令牌从 `?t=` 读取) |
| GET | `/api/p/view` | 玩家的全部菜单(玩家令牌鉴权) |
| POST | `/api/p/invoke/{menu}` | 以玩家身份执行卡片按钮(玩家令牌鉴权) |

## 给插件开发者

- **管理模块**:可选注入 `IWebPanelRegistry`(来自共享库 `UnturnedMods.Shared`),用
  `WebPanelModule` 描述若干 `WebPanelAction` 并注册;面板自动渲染。
- **玩家菜单**:可选注入 `IPlayerMenuRegistry`,实现 `IPlayerMenu`(`RenderAsync` 出卡片、
  `InvokeAsync` 执行按钮)并注册;玩家在 `/p` 面板里以自己的身份操作。

两者注入皆为可选——未安装本面板时注册被跳过,你的插件照常工作。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店,买卖物品 / 组合包,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的实用指令：home/tp/warp/gift/sleep/back，经济收费可选 |

完整文档、配置示例与本地调试说明见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
