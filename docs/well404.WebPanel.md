# well404.WebPanel

通用的 **Web 管理面板**:内置一个轻量 HTTP 服务(基于 BCL `HttpListener`,无外部依赖)
与单页应用,并对外暴露 `IWebPanelRegistry`。其他功能插件(Economy、Shop 等)在加载时把
自己的管理模块按统一 schema 注册进来,面板即可通用地渲染出设置组、集合 CRUD、搜索框等,
而无需了解各插件的实现细节。

除面向管理员的管理面板外,本插件还提供一个**面向玩家**的网页界面(`/p`):玩家在游戏内
输入 `/menu` 即可收到一条专属链接(经 Steam 叠加层浏览器打开),在网页里以**自己的身份**
浏览商店并购买/出售、查看钱包并转账、一键传送回家/死亡点/传送点等。各功能插件通过
`IPlayerMenuRegistry` 把自己的「玩家菜单」注册进来,与管理模块互不影响。

## 安装

```bash
openmod install well404.WebPanel
openmod reload
```

启动后浏览器访问 `http://<bindAddress>:<port>`。装了哪个家族插件,面板里就出现对应模块。

## 配置（`config.yaml`）

首次加载后生成于 `openmod/plugins/well404.WebPanel/config.yaml`：

```yaml
web:
  bindAddress: "127.0.0.1"   # 监听地址:127.0.0.1(仅本机)| 0.0.0.0(全部网卡)| 指定 IP
  port: 8080                 # 监听端口
  token: ""                  # 访问令牌;对外暴露(非回环)时必填
  allowInsecurePublic: false # 【危险】允许无令牌对外暴露,请保持 false
  publicBaseUrl: ""          # 玩家 /menu 链接用的公网地址,如 http://your-ip:8080;空=由 bindAddress+port 推导
  playerSessionMinutes: 5    # 玩家 /menu 链接的有效期(分钟)
```

> **玩家面要能用,必须让玩家的浏览器能访问到本服务。** 若面板绑定 `127.0.0.1`,请把
> `publicBaseUrl` 设为玩家可达的公网地址(经反向代理时通常是你的域名);否则 `/menu`
> 会提示玩家面板不可达。玩家链接用一次性短时令牌鉴权,与管理员 `token` 完全独立。

### 鉴权与安全

| 场景 | 行为 |
| --- | --- |
| 回环地址 + 空令牌 | 无鉴权,仅本机可访问(默认,最安全) |
| 对外地址 + 设置令牌 | 所有 `/api` 调用需带 `X-Web-Token` 头或 `?token=` 查询参数 |
| 对外地址 + 空令牌 | 自动降级回 `127.0.0.1`,除非显式 `allowInsecurePublic: true`(强烈不建议) |
| 回环地址 + 设置令牌 | 令牌被忽略(回环本就只对本机开放) |

> 推荐做法:面板绑定 `127.0.0.1`,通过 SSH 隧道或反向代理(自带 TLS + 鉴权)对外访问;
> 如必须直接对外,请务必设置高强度 `token`。

## HTTP 接口

| 方法 | 路径 | 鉴权 | 说明 |
| --- | --- | --- | --- |
| GET | `/`、`/index.html` | 否 | 单页应用(无需令牌,便于在 UI 内输入令牌) |
| GET | `/api/modules` | 是* | 列出已注册模块及其字段 schema |
| GET | `/api/modules/{module}/{action}/values` | 是* | 拉取设置 / 搜索动作的预填值 |
| GET | `/api/modules/{module}/{action}/records` | 是* | 列出集合动作的记录 |
| POST | `/api/modules/{module}/{action}` | 是* | 提交动作(表单 / 搜索) |
| POST | `/api/modules/{module}/{action}/delete` | 是* | 删除集合中的一条记录 |

\* 仅当配置了 `token`(或对外暴露)时强制校验。

### 玩家面接口(`/p`,独立鉴权)

| 方法 | 路径 | 鉴权 | 说明 |
| --- | --- | --- | --- |
| GET | `/p` | 否 | 玩家单页应用(令牌从 `?t=` 读取) |
| GET | `/api/p/view` | 玩家令牌 | 该玩家的全部菜单,每个菜单已预渲染 |
| POST | `/api/p/invoke/{menu}` | 玩家令牌 | 以该玩家身份执行某张卡片的按钮 |

> 玩家令牌经 `?t=`(或 `X-Player-Token` 头)传入,由短时会话校验;**管理员 `token`
> 在玩家面无效,玩家令牌在管理面也无效**,两套鉴权互不通用。

## 命令与权限

| 命令 | 说明 | 权限 |
| --- | --- | --- |
| `/menu [tab]` | 生成并向玩家推送其专属面板链接;可选参数定位到某个标签(如 `/menu shop`) | `well404.webpanel:commands.menu` |

`/menu` 别名 `/panel`。除此之外,本插件是纯基础设施,管理面通过浏览器使用。

## 给插件开发者:注册自己的管理模块

面板的能力来自共享库 `UnturnedMods.Shared` 中的 `IWebPanelRegistry` 抽象。在你的插件里
**可选**注入它,用 `WebPanelModule` 描述若干 `WebPanelAction` 并注册;注入为可选,未安装
本面板时注册自动跳过,你的插件照常工作。

动作类型(`WebActionKind`):

| 类型 | 用途 |
| --- | --- |
| `Table` | 只读、自动加载的数据表 |
| `Form` | 一次性提交的表单 |
| `Search` | 实时查询框 |
| `Settings` | 预填、可编辑的设置组(统一保存按钮) |
| `Collection` | 记录的增删改列表(支持网格 / 列表布局) |

参考实现见 Economy 的 `EconomyWebPanelModule.cs` 与 Shop 的 `ShopWebPanelModule.cs`。

## 给插件开发者:注册自己的玩家菜单

玩家面的能力来自同一共享库中的 `IPlayerMenuRegistry`。实现一个 `IPlayerMenu`
(`RenderAsync` 返回若干 `PlayerCard`,每张卡片带文字、标签与按钮;`InvokeAsync` 执行按钮),
在插件加载时**可选**注入 `IPlayerMenuRegistry` 并注册;未安装本面板时跳过即可。

- 处理器运行在 Web 线程,触碰 Unturned API 前先 `await UniTask.SwitchToMainThread()`;
- `PlayerMenuContext` 给出玩家 Steam ID 与显示名,据此用 `IUserManager` 解析在线玩家;
- 按钮可带 `promptLabel`,客户端会先弹窗让玩家输入一个数字(如数量/金额)再提交;
- `PlayerActionResult.Refresh` 控制动作后是否重新渲染该菜单(默认 `true`)。

若插件还想给玩家一条进入入口,可在某个命令里注入 `IPlayerWebSessionService`,用
`CreateLink(steamId, displayName, menuId)` 生成链接并 `Player.sendBrowserRequest` 发给玩家
(也可直接让玩家用本插件的 `/menu` 命令)。参考实现见 Shop 的 `ShopPlayerMenu.cs`、
Economy 的 `EconomyPlayerMenu.cs`、Essentials 的 `EssentialsPlayerMenu.cs`。

## 本地构建与调试

见 [docs/README](README.md#本地构建与调试)。
