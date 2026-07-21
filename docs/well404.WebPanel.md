# well404.WebPanel

通用的 **Web 管理面板**:内置一个轻量 HTTP 服务(基于 BCL `HttpListener`,无外部依赖)
与单页应用,并对外暴露 `IWebPanelRegistry`。其他功能插件(Economy、Shop 等)在加载时把
自己的管理模块按统一 schema 注册进来,面板即可通用地渲染出设置组、集合 CRUD、搜索框等,
而无需了解各插件的实现细节。

Collection schema 还可声明分组标签和 reorder handler:管理页会以响应式三列卡片预览,在组内拖放后通过 `/reorder` 提交完整键顺序。表格行操作弹窗使用 `URLSearchParams.entries()` 复制输入字段,确保买价、卖价等弹窗值真正随请求发送。玩家按钮 schema 支持 1/5/10/全部等快捷值、二次确认和紧凑的分组 header 动作。

除面向管理员的管理面板外,本插件还提供一个**面向玩家**的网页界面(`/p`):玩家在游戏内
输入 `/menu` 即可收到一条专属链接(经 Steam 叠加层浏览器打开),在网页里以**自己的身份**
浏览商店并购买/出售、查看钱包并转账、使用各种实用工具(传送/组队/礼包/睡觉)等。各功能
插件通过 `IPlayerMenuRegistry` 把自己的「玩家菜单」注册进来,与管理模块互不影响。

玩家面板首页是一个**服务器介绍页**:顶部是管理员可在面板里编辑的 **Markdown 简介**(单一
共享文本),下面是**该玩家有权限使用的指令列表**(各插件经 `IPlayerCommandRegistry` 登记,
按权限过滤)。**「首页」始终是第一个标签,并作为开屏默认页**(`/menu shop` 等带参数才定位到对应标签)。

**多语言**:管理面板与玩家面板均内置**中/英双语**,右上角下拉切换,默认英文;玩家页另有
「定时刷新状态 + 手动刷新 ↻」提示。**两个面板切换的语言都保存在服务端的配置文件里,
不在浏览器**——玩家语言按其 Steam ID 存于 `player-languages.yaml`(`players:` 映射),管理面语言存于
`admin-language.yaml`(单值);因此即使管理面 URL(快速隧道域名)每次重启变化、浏览器 localStorage 丢失,
下次打开仍沿用上次所选语言(旧的 `*.txt` 会在首次加载时自动迁移为 `.yaml`)。给开发者:网页文案用
`IWebTranslationRegistry`(英文源串为键 + 中文映射表),详见
[development-standards.md §9](../memory/guidelines/development-standards.md)。物品名称统一由共享目录解析：中文界面显示中文主标题，英文名在下一行以灰色小字参考；英文界面只显示英文名。

## UI 扩展与自动刷新

简单模块继续使用原有 Settings、Collection、Table 等描述符注册。复杂模块可以把自身
HTML/CSS/JavaScript 作为程序集资源注册，WebPanel 会在 Shadow DOM 中挂载；宿主不含任何具体
插件判断。Essentials、Shop、Vault 已迁移到这种自建 UI，其余模块继续使用描述符制。

两个面板默认每 5 秒刷新安全数据。页面隐藏、存在弹窗或输入框正在编辑时会暂停，避免覆盖未保存
内容；手动刷新按钮始终保留。玩家动作会使先前 GET 失效，多笔动作只接受最后发起动作的视图，全部动作结束后再执行一次
权威刷新，因此慢响应不会把页面回滚到旧库存。可在 web 配置段设置 refreshIntervalSeconds，设为 0 关闭自动刷新。

插件自建玩家 UI 可通过 `PlayerCard.Metadata` 为卡片附加非敏感字符串键值。WebPanel 将其作为
JSON `metadata` 对象原样传给 Shadow DOM 运行时，并按键稳定序列化，但不解释任何插件字段。
旧的 8 参数与 10 参数 `PlayerCard` 构造函数仍保留；需要元数据时使用新增重载。

## 安装

```bash
openmod install well404.WebPanel
```

安装或升级 DLL 后必须完整重启服务器；`openmod reload` 只适合未替换二进制时的配置或生命周期重载。

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
  refreshIntervalSeconds: 5  # 安全自动刷新间隔(秒);0=关闭
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

开启后,插件会拉起一个隧道工具,把面板端口安全地反代到公网,并自动把得到的公网地址用于
玩家 `/menu` 链接与管理面地址。两种类型:

| `type` | 说明 |
| --- | --- |
| `cloudflare` | Cloudflare Quick Tunnel(无需账号)。参数/URL 解析已内置;只需在 `command` 指定二进制路径(默认 `cloudflared`)。找不到 `cloudflared` 时会停止并提示管理员安装。得到随机 `https://<…>.trycloudflare.com`。 |
| `custom` | 你完全自定义 `command` / `args` / `urlPattern` / `apiUrl`(`{port}` 会被替换为面板端口)。适配 ngrok 等任意工具;`custom` 不会自动下载,需你自行安装。 |

**cloudflared 安装与供应链安全**:`autoDownload` 默认关闭，当前构建也不会下载或执行网络取得的
cloudflared。原因是仓库尚未维护“固定官方版本 + 各平台官方 SHA-256 白名单”；在具备并逐次校验这份
可信清单前，mutable `latest` 地址、第三方代理和既有未校验缓存都不会被采用。请由管理员安装
cloudflared，并将 `command` 设为其可执行文件路径。`downloadMirrors` 与 `downloadAttempts` 仅为兼容
旧配置而保留，当前忽略。

- **不阻塞启动**:隧道(含下载)在**后台线程**进行,服务器启动不会被下载卡住;隧道就绪后会单独打印
  公网管理面地址。下载/启动失败不影响面板本地访问,只是隧道未启用。玩家若在隧道刚启动的短暂窗口内执行
  `/menu`,会**短暂等待隧道就绪**(最多约 25 秒)再返回链接,而不是立刻报「无公网地址」。
- **启动后醒目提醒**:若 WebPanel 启动期间出现任何问题(HTTP 监听失败、隧道未起来等),会在**服务器
  完成开服(servercode 弹出)之后**再用一条醒目的 `ERR` 横幅日志重新提示管理员,避免被刷屏淹没。
- **不残留进程占用端口**:插件启动绑定端口前、以及卸载时,都会清理**它自己启动的** cloudflared 进程
  (按二进制路径匹配)。Windows 上 Mono 会让子进程**继承**面板的监听套接字,若上次是崩溃/强杀导致
  cloudflared 成为孤儿,它会一直占住端口,使下次启动报 `address already in use`;现在重启会自动清掉它。
- **支持 `openmod reload` 后重新建隧道**:玩家链接状态按插件加载代次隔离，reload 会清除旧的
  Quick Tunnel URL，并等待 cloudflared 报告新 URL；旧实例的迟到回调不会把新状态标成不可用。
  `autoRestart: true` 时，reload 后首次启动若暂时未取得 public URL，也会每 3 秒重新尝试。
- **默认 `127.0.0.1` 绑定也能经隧道访问**:cloudflared 以 `--http-host-header 127.0.0.1:{port}` 调起,
  转发到本地时发送 `Host: 127.0.0.1:{port}`,匹配默认绑定的监听前缀;否则 cloudflared 转发的是公网
  Host,`HttpListener` 会因 Host 不匹配回 `400 (Invalid host)`,导致经隧道访问全部 400。无需把
  `bindAddress` 改成 `0.0.0.0`/开放端口即可经隧道访问。

### 使用 Cloudflare 账号与永久域名（Named Tunnel，推荐用于长期运行）

内置的 `type: "cloudflare"` 是**无需账号的 Quick Tunnel**：地址随机，重启后会变化。
如果服务器要长期运行，建议在 Cloudflare 账号中创建 **Named Tunnel（命名隧道）**，把
`panel.example.com` 之类的固定域名指向 WebPanel。Named Tunnel 由系统服务独立运行，
WebPanel 仅负责监听本地端口和生成固定域名下的玩家链接。

这种方式的优点：

- 公网地址固定，服务器、WebPanel 或 `openmod reload` 后都不变；
- `cloudflared` 由 systemd / Windows 服务管理，WebPanel reload 不会结束隧道；
- 不开放 27020 入站端口，WebPanel 仍只监听 `127.0.0.1`；
- Tunnel Token 不写入 OpenMod 配置，避免它出现在插件日志或管理页面中。

> **不要同时开启两种隧道。** Named Tunnel 作为系统服务运行时，必须设置
> `web.tunnel.enabled: false`。否则 WebPanel 还会额外启动一个随机 Quick Tunnel，
> 日志和 `/menu` 可能改用随机地址。
>
> 当前 WebPanel 的 `type: "cloudflare"` 专用于 Quick Tunnel。不要把 Named Tunnel Token
> 填进 `tunnel.args`；Named Tunnel 应由 Cloudflare 官方服务命令运行，固定公网地址交给
> `web.publicBaseUrl`。

#### 准备条件

开始前确认：

1. 你有一个已接入 Cloudflare DNS 的域名，例如 `example.com`；
2. 可以登录该域名所在的 Cloudflare 账号；
3. Cloudflare Tunnel 与 Unturned/WebPanel 运行在同一台服务器；若不在同一台机器，
   后面的 Service URL 要改为 WebPanel 所在机器的内网地址；
4. WebPanel 本地端口没有被其它程序占用，以下示例使用默认端口 `27020`；
5. 服务器可以主动访问 Cloudflare。受限防火墙环境需要允许 `cloudflared` 建立出站连接。

Cloudflare 官方流程参见
[Set up Cloudflare Tunnel](https://developers.cloudflare.com/tunnel/setup/) 和
[Create a remotely-managed tunnel](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/get-started/create-remote-tunnel/)。

#### 第 1 步：先配置并验证本地 WebPanel

编辑：

```text
openmod/plugins/well404.WebPanel/config.yaml
```

使用以下关键配置；保留文件中其它配置项：

```yaml
web:
  # 只允许本机访问，不需要改成 0.0.0.0，也不要开放 27020 入站端口。
  bindAddress: "127.0.0.1"
  port: 27020

  # 管理面板固定密钥。请换成你自己的高强度随机值并妥善保管。
  token: "请替换为至少32位的随机字符串"

  # Named Tunnel 由操作系统服务运行；必须关闭插件内置 Quick Tunnel。
  tunnel:
    enabled: false

  # 填稍后在 Cloudflare 创建的固定 HTTPS 域名，不要带末尾斜杠。
  publicBaseUrl: "https://panel.example.com"
```

Linux 可以生成只含十六进制字符、适合放在 URL 路径中的随机 token：

```bash
openssl rand -hex 24
```

完整重启一次服务器，然后在服务器本机检查：

```bash
curl -i http://127.0.0.1:27020/
curl -i http://127.0.0.1:27020/你的管理token/
```

第一条应返回 WebPanel 正在运行的提示，第二条应返回管理面 HTML。若本地都连接失败，
先不要配置 Cloudflare，检查 OpenMod 日志、端口占用以及 `bindAddress` / `port`。

#### 第 2 步：在 Cloudflare 创建 Named Tunnel

1. 登录 Cloudflare Dashboard；
2. 打开 **Networking → Tunnels**；
3. 选择 **Create Tunnel**；
4. 输入名称，例如 `unturned-webpanel`；
5. 创建后选择服务器对应的操作系统和架构；
6. Cloudflare 会显示该 Tunnel 专属的 **Install and Run** 命令，在服务器上执行它；
7. 等待控制台中 Tunnel 状态变为 **Healthy**，再继续下一步。

Linux 上，Cloudflare 给出的服务安装命令形式通常为：

```bash
sudo cloudflared service install <TUNNEL_TOKEN>
```

确认服务状态：

```bash
sudo systemctl status cloudflared
sudo journalctl -u cloudflared -n 100 --no-pager
```

Windows 请在**管理员身份**的命令提示符或 PowerShell 中运行 Cloudflare 页面给出的命令，
形式通常为：

```powershell
cloudflared.exe service install <TUNNEL_TOKEN>
sc.exe query Cloudflared
```

`<TUNNEL_TOKEN>` 是连接服务器到 Tunnel 的凭据，等同密码：不要发给他人、不要提交到 Git、
不要放入 WebPanel 的 `config.yaml`。若泄露，应在 Cloudflare 控制台轮换 Tunnel Token。

#### 第 3 步：添加固定 Public Hostname

进入刚创建的 Tunnel：

1. 打开 **Routes**；
2. 选择 **Add route → Published application**；
3. 在 Hostname 中填写：
   - Subdomain：`panel`
   - Domain：`example.com`
   - 最终得到：`panel.example.com`
4. Service type 选择 **HTTP**；
5. Service URL 填：

```text
http://127.0.0.1:27020
```

6. 展开 Additional application settings / HTTP settings；
7. 把 **HTTP Host Header** 设置为：

```text
127.0.0.1:27020
```

8. 保存 route。

**HTTP Host Header 不能省略。** WebPanel 默认只监听
`http://127.0.0.1:27020/`，Cloudflare 如果把公网域名原样作为 `Host` 转给
`HttpListener`，可能得到 `400 Invalid Host`。该参数会让请求使用与本地监听一致的 Host。
Cloudflare 对此参数的定义见
[Origin parameters: httpHostHeader](https://developers.cloudflare.com/tunnel/advanced/origin-parameters/)。
Public Hostname 到本地 HTTP 服务的映射方式见
[Published applications](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/routing-to-tunnel/)。

如果 `cloudflared` 与 WebPanel 不在同一台机器，Service URL 和 HTTP Host Header 都应改成
WebPanel 机器可达的内网地址，例如：

```text
Service URL:      http://192.168.1.20:27020
HTTP Host Header: 192.168.1.20:27020
```

此时 WebPanel 的 `bindAddress` 也要绑定对应内网地址，并通过防火墙只允许
`cloudflared` 所在机器访问，不能直接对公网开放。

#### 第 4 步：验证永久链接

等待 DNS 和 Tunnel route 生效，然后测试：

```bash
curl -i https://panel.example.com/
curl -i https://panel.example.com/你的管理token/
```

验证结果：

- `https://panel.example.com/你的管理token/` 能打开管理员面板；
- 玩家进服执行 `/menu` 后，收到的链接以
  `https://panel.example.com/p?t=...` 开头；
- 重启 Unturned、执行 `openmod reload` 或重启 `cloudflared` 后，域名仍然不变；
- OpenMod 日志不应再打印新的 `trycloudflare.com` 地址。

最终地址格式：

```text
管理员面板：https://panel.example.com/<web.token>/
玩家面板：  https://panel.example.com/p?t=<玩家临时令牌>
```

`publicBaseUrl` 只填域名根地址，**不要**把管理 token、`/p` 或结尾斜杠写进去。

#### 第 5 步：日常维护与安全

- 更新 WebPanel 不需要重建 Tunnel；只要端口和域名不变，原 route 继续生效；
- 更新 `cloudflared` 时使用操作系统的软件包管理器或 Cloudflare 官方安装方式，然后重启服务；
- `web.token` 是管理员面板密码，建议至少 32 个随机字符，禁止分享或出现在截图中；
- Tunnel Token 只负责连接器身份，`web.token` 负责 WebPanel 管理员鉴权，两者不是同一个 token；
- 玩家链接带一次性会话参数 `?t=...`，不要把玩家链接当作管理员链接；
- 不要为了 Tunnel 把 `bindAddress` 改为 `0.0.0.0`，也不要在公网防火墙开放 27020；
- 若使用 Cloudflare Access，注意对整个 `panel.example.com` 强制登录会同时拦截玩家
  `/p` 页面；应先验证游戏内浏览器的登录流程，或只设计针对管理入口的独立访问策略。

#### 常见故障排查

| 现象 | 原因与处理 |
| --- | --- |
| Cloudflare 显示 Tunnel `Inactive` / `Down` | `cloudflared` 服务未运行或 Token 无效。Linux 检查 `systemctl status cloudflared` 和 `journalctl -u cloudflared`；Windows 检查 `sc.exe query Cloudflared`。 |
| 公网返回 `502 Bad Gateway` | Tunnel 已连接，但 `cloudflared` 无法访问 WebPanel。先在服务器执行 `curl http://127.0.0.1:27020/`，再核对 Service URL 和端口。 |
| 公网返回 `400 Invalid Host` | Public Hostname 的 **HTTP Host Header** 未设置或填写错误；设为与 Service URL 一致的 `127.0.0.1:27020`。 |
| `/menu` 提示没有公网 URL | 检查 `web.publicBaseUrl: "https://panel.example.com"` 是否位于 `web:` 下，且没有误写到 `tunnel:` 内；保存后完整重启或 reload WebPanel。 |
| 日志仍出现随机 `trycloudflare.com` | `web.tunnel.enabled` 仍为 `true`，或修改了错误服务器/实例的配置文件。设置为 `false` 后完整重启。 |
| 固定域名能打开根路径，但管理面 404 | URL 缺少正确的 `web.token`，或 token 中包含未编码的特殊字符。建议使用 `openssl rand -hex 24` 生成。 |
| 本地能访问、公网 DNS 解析失败 | Public Hostname route 尚未保存、域名不在同一 Cloudflare 账号，或 DNS 尚未生效。回到 Tunnel 的 Routes 页面确认 hostname。 |
| reload 后域名正常但页面功能缺失 | 这不是 Tunnel 地址问题；检查各功能插件是否成功重新注册 WebPanel 模块，必要时完整重启服务器。 |
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
> ——务必保密。隧道随插件卸载自动关闭。管理页与 API 必须由同一公网源反代，服务端不开放跨源读取。

**自动保活(`autoRestart`,默认开)**:Cloudflare Quick Tunnel 跑一段时间后可能掉线,导致网页
「connection closed」而服务器并不会自动恢复。开启后,插件会监控隧道——**进程退出**会立即被发现,
**公网 URL 不再响应**也会被探测到——随即**自动重启隧道并启用新地址**(快速隧道每次重启地址会变,
玩家需重新 `/menu` 取新链接;新管理面地址会打印到日志)。`healthCheckSeconds`(默认 60)控制 URL
探测频率,设 0 则只在进程退出时重启;探测若一次都连不通(如本机无出站 HTTPS)会自动停用,绝不会
陷入反复重启。这些是隧道基础设施配置,**不纳入 WebUI**,只能改 `config.yaml`。

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
| GET | `/<token>/api/modules/{module}/asset/{asset}` | 当前管理模块提供的只读图片资源 |
| GET | `/<token>/dev-player` | 开发预览:签发 `devPlayer` 会话并 302 跳转 `/p`(仅 `web.devPlayer.enabled` 为真时;否则 404) |

### 玩家面接口(`/p`,独立鉴权)

| 方法 | 路径 | 鉴权 | 说明 |
| --- | --- | --- | --- |
| GET | `/p` | 否 | 玩家单页应用(令牌从 `?t=` 读取) |
| GET | `/api/p/view` | 玩家令牌 | 该玩家的全部菜单,每个菜单已预渲染 |
| POST | `/api/p/invoke/{menu}` | 玩家令牌 | 以该玩家身份执行某张卡片的按钮 |
| GET | `/api/p/asset/{menu}/{asset}` | 玩家令牌 | 当前玩家菜单提供的只读图片资源 |

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

### 自建 UI 的只读图片资源

复杂 UI 若要显示地图等插件自有图片，不应创建公开静态目录。管理模块把 `IWebPanelModuleAssetProvider` 传给 `WebPanelModule`；玩家菜单实现 `IPlayerMenuAssetProvider`。两者都按不透明 `assetId` 返回 `PlayerMenuAsset`，自建 JavaScript 通过 `panel.assetUrl(assetId)` 取得限定在当前模块/菜单的 URL。提供者必须使用固定 id 白名单，不能把 id 拼成磁盘路径。

宿主在读取提供者前已经完成管理 token 或玩家会话校验；返回时只接受 PNG、JPEG、WebP、GIF，拒绝空内容和超过 64 MiB 的内容，添加 `Cache-Control: private`、ETag、`X-Content-Type-Options: nosniff`，并支持 `If-None-Match` 返回 304。图片内容仍由业务插件决定，WebPanel 不判断地图、物品等具体用途。


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
