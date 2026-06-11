# well404.WebPanel

通用的 **Web 管理面板**:内置一个轻量 HTTP 服务(基于 BCL `HttpListener`,无外部依赖)
与单页应用,并对外暴露 `IWebPanelRegistry`。其他功能插件(Economy、Shop 等)在加载时把
自己的管理模块按统一 schema 注册进来,面板即可通用地渲染出设置组、集合 CRUD、搜索框等,
而无需了解各插件的实现细节。

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
```

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

## 命令与权限

本插件**无聊天/控制台命令**,也不声明权限——它是纯基础设施,通过浏览器管理。

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

## 本地构建与调试

见 [docs/README](README.md#本地构建与调试)。
