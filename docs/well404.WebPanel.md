# well404.WebPanel

通用的 **Web 管理面板**:内置一个轻量 HTTP 服务(基于 BCL `HttpListener`,无外部依赖)
与单页应用,并对外暴露 `IWebPanelRegistry`。其他功能插件(Economy、Shop 等)在加载时把
自己的管理模块按统一 schema 注册进来,面板即可通用地渲染出设置组、集合 CRUD、搜索框等,
而无需了解各插件的实现细节。

除面向管理员的管理面板外,本插件还提供一个**面向玩家**的网页界面(`/p`):玩家在游戏内
输入 `/menu` 即可收到一条专属链接(经 Steam 叠加层浏览器打开),在网页里以**自己的身份**
浏览商店并购买/出售、查看钱包并转账、使用各种实用工具(传送/组队/礼包/睡觉)等。各功能
插件通过 `IPlayerMenuRegistry` 把自己的「玩家菜单」注册进来,与管理模块互不影响。

玩家面板首页是一个**服务器介绍页**:顶部是管理员可在面板里编辑的 **Markdown 简介**(单一
共享文本),下面是**该玩家有权限使用的指令列表**(各插件经 `IPlayerCommandRegistry` 登记,
按权限过滤)。**「首页」始终是第一个标签,并作为开屏默认页**(`/menu shop` 等带参数才定位到对应标签)。

**多语言**:管理面板与玩家面板均内置**中/英双语**,右上角下拉切换,默认英文;玩家页另有
「手动刷新 ↻ + 不会自动刷新」提示(因页面数据不自动刷新)。**玩家在网页里切换的语言会按
其 Steam ID 保存在服务端**(`player-languages.txt`),下次从任意设备打开都沿用该语言。给开发者:网页文案用
`IWebTranslationRegistry`(英文源串为键 + 中文映射表),详见
[development-standards.md §9](../memory/guidelines/development-standards.md)。

## 安装

```bash
openmod install well404.WebPanel
openmod reload
```

启动后日志会打印管理面访问地址 `http://<bindAddress>:<port>/<token>/`(见下「鉴权」)。
装了哪个家族插件,面板里就出现对应模块。

## 配置（`config.yaml`）

首次加载后生成于 `openmod/plugins/well404.WebPanel/config.yaml`：

```yaml
web:
  bindAddress: "127.0.0.1"   # 监听地址:127.0.0.1(仅本机)| 0.0.0.0(全部网卡)| 指定 IP
  port: 27020                 # 监听端口
  token: ""                  # 管理面密钥;留空=首次启动随机生成 12 位并写回本文件
  tunnel:                    # 可选:内置反代,把面板安全地暴露到公网(见下)
    enabled: false
    type: "cloudflare"       # cloudflare | custom
    command: "cloudflared"
  publicBaseUrl: ""          # 玩家 /menu 链接用的公网地址;空=由 bindAddress+port 推导(开了 tunnel 时自动用隧道地址)
  playerSessionMinutes: 5    # 玩家链接有效期下限(分钟);实际不少于 15
  devPlayer:                 # 开发预览:不进游戏也能以指定账号查看玩家面(默认关,见下)
    enabled: false
    steamId: ""              # 要模拟的玩家 Steam ID
    displayName: "Dev Player"
```

> **玩家面要能用,必须让玩家的浏览器能访问到本服务**:要么开 `tunnel`(推荐,见下),
> 要么手动把 `publicBaseUrl` 设为玩家可达的公网地址。否则 `/menu` 会提示面板不可达。

### 开发者预览(`web.devPlayer`,可选)

调试玩家面(`/p`)时通常得先进游戏发 `/menu` 才能拿到会话。打开本开关后,访问
`http://host:port/<token>/dev-player` 会为 `steamId` 指定的账号**直接签发一个长效会话并跳转进
其玩家面板**,无需进游戏即可在浏览器里预览/调试所有玩家菜单(首页、商店、实用工具…)。

- **双重门槛**:既藏在管理面密钥路径 `/<token>/` 之后,又需 `enabled: true`;关闭时该路径同样
  返回普通 404(无信息泄露)。**这是玩家身份模拟,生产环境请保持关闭。**
- 仅**预览渲染**:凡需玩家真正在线的动作(买卖、传送)仍会提示「需要在线」。
- 开启且日志可见时,启动会打印一条 `DEV player preview is ON — …/dev-player` 警告,附带该 URL。

### 鉴权与安全（路径式 token）

管理面**始终**藏在一个密钥路径后面:`http://host:port/<token>/`。**这个 token 就是鉴权**
——路径不对(或没带)一律返回普通 404,不会泄露「未授权」这种信号,扫描者无从判断面板是否存在。

- **token 强制存在**:`web.token` 留空时,插件会随机生成一个 **12 位大小写字母+数字**的
  token 并**写回 `config.yaml`**(重启保持不变);也可自己设一个固定值。
- token **不在 WebUI 中可改**(避免把后台钥匙暴露在后台页面里);只能改 `config.yaml`。
- 启动日志会打印完整管理面地址 `…/<token>/`,**请妥善保管,等同后台密码**。
- 玩家面(`/p`)用各玩家自己的一次性短时令牌,与管理员 token **完全独立、互不通用**。

> 推荐做法:面板绑定 `127.0.0.1`,开启内置 `tunnel`(或自建反向代理)对外。这样游戏服
> 不暴露真实 IP、不开入站端口,且自动获得 HTTPS。

### 内置反代 / 隧道（`web.tunnel`，可选)

开启后,插件会拉起一个**你已安装**的隧道工具(不内置、不下载二进制),把面板端口安全地
反代到公网,并自动把得到的公网地址用于玩家 `/menu` 链接与管理面地址。两种类型:

| `type` | 说明 |
| --- | --- |
| `cloudflare` | Cloudflare Quick Tunnel(无需账号)。装好 `cloudflared` 即可,参数/URL 解析已内置;只需在 `command` 指定二进制路径(默认 `cloudflared`)。得到随机 `https://<…>.trycloudflare.com`。 |
| `custom` | 你完全自定义 `command` / `args` / `urlPattern` / `apiUrl`(`{port}` 会被替换为面板端口)。适配 ngrok 等任意工具。 |

`custom` + ngrok 示例:

```yaml
tunnel:
  enabled: true
  type: "custom"
  command: "ngrok"
  args: "http {port}"
  apiUrl: "http://127.0.0.1:4040/api/tunnels"
  urlPattern: "https://[a-z0-9-]+\\.ngrok[a-z0-9.-]*"
```

> 隧道把**整个端口**(管理面 + 玩家面)反代出去,所以管理面的路径式 token 此时就是唯一防线
> ——务必保密。隧道随插件卸载自动关闭。响应已带宽松 CORS 头,便于套在任意第三方反代后面。

## HTTP 接口

管理面全部在 `/<token>/` 路径下(下表省略该前缀);路径式 token 即鉴权,无需额外头部。

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/<token>/`、`/<token>/index.html` | 管理单页应用(其 API 调用为相对路径,自动带上 token 前缀) |
| GET | `/<token>/api/modules` | 列出已注册模块及其字段 schema |
| GET | `/<token>/api/modules/{module}/{action}/values` | 拉取设置 / 搜索动作的预填值 |
| GET | `/<token>/api/modules/{module}/{action}/records` | 列出集合动作的记录 |
| POST | `/<token>/api/modules/{module}/{action}` | 提交动作(表单 / 搜索) |
| POST | `/<token>/api/modules/{module}/{action}/delete` | 删除集合中的一条记录 |
| GET | `/<token>/dev-player` | 开发预览:签发 `devPlayer` 会话并 302 跳转 `/p`(仅 `web.devPlayer.enabled` 为真时;否则 404) |

### 玩家面接口(`/p`,独立鉴权)

| 方法 | 路径 | 鉴权 | 说明 |
| --- | --- | --- | --- |
| GET | `/p` | 否 | 玩家单页应用(令牌从 `?t=` 读取) |
| GET | `/api/p/view` | 玩家令牌 | 该玩家的全部菜单,每个菜单已预渲染 |
| POST | `/api/p/invoke/{menu}` | 玩家令牌 | 以该玩家身份执行某张卡片的按钮 |

> 玩家令牌经 `?t=`(或 `X-Player-Token` 头)传入,由短时会话校验。**有效期至少 15 分钟,
> 之后只要玩家仍在线就一直有效,一旦下线即失效**。管理员 token 在玩家面无效,反之亦然。

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

通用扩展(任意插件可用,**宿主不含任何插件专属逻辑**):
- `WebPanelAction.SummaryFields`:`Collection` 的每条记录在瓦片上额外用「字段标签: 值」胶囊显示选定字段
  (用已本地化的字段标签),让关键数据(如商品买/卖价)不必打开编辑器即可见。
- `WebPanelAction.Hidden`:只可被 id 调用、不渲染卡片(作为下面行内动作的目标)。
- `WebActionResult.WithRowAction(actionId, label, rowKeys?, fields?)`:给 `Table`/`Search` 结果每行挂一个
  按钮,点击即以该行 key(缺省取首列)调用本模块的 `actionId`;若给了 `fields` 则**先弹窗收集这些输入**
  一并提交。Shop 的「检索→＋(弹窗填买卖价)一键加入商品」即用它。

参考实现见 Economy 的 `EconomyWebPanelModule.cs` 与 Shop 的 `ShopWebPanelModule.cs`。

## 给插件开发者:注册自己的玩家菜单

玩家面的能力来自同一共享库中的 `IPlayerMenuRegistry`。实现一个 `IPlayerMenu`
(`RenderAsync` 返回若干 `PlayerCard`,每张卡片带文字、标签与按钮;`InvokeAsync` 执行按钮),
在插件加载时**可选**注入 `IPlayerMenuRegistry` 并注册;未安装本面板时跳过即可。

- 处理器运行在 Web 线程,触碰 Unturned API 前先 `await UniTask.SwitchToMainThread()`;
- `PlayerMenuContext` 给出玩家 Steam ID 与显示名,据此用 `IUserManager` 解析在线玩家;
- 按钮可带 `promptLabel`,客户端会先弹窗让玩家输入一个数字(如数量/金额)再提交;
- `PlayerActionResult.Refresh` 控制动作后是否重新渲染该菜单(默认 `true`);
- **样式/布局全走通用字段**(宿主不认识具体含义):`PlayerMenuView.Layout`(`"list"`/默认卡片)、
  `PlayerCard.Group`(分区标题,按顺序分组)、`PlayerCard.Badge`(前置短徽章)、`PlayerCard.Tags`(胶囊)、
  `PlayerButton.Style`(`primary`/`success`/`danger`)。动态文本(价格等)由插件本地化后拼进按钮 `Label`。

若插件还想给玩家一条进入入口,可在某个命令里注入 `IPlayerWebSessionService`,用
`CreateLink(steamId, displayName, menuId)` 生成链接并 `Player.sendBrowserRequest` 发给玩家
(也可直接让玩家用本插件的 `/menu` 命令)。参考实现见 Shop 的 `ShopPlayerMenu.cs`、
Economy 的 `EconomyPlayerMenu.cs`、Essentials 的 `EssentialsPlayerMenu.cs`。

## 本地构建与调试

见 [docs/README](README.md#本地构建与调试)。
