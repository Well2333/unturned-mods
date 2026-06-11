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
- 🔐 令牌鉴权 + 监听地址绑定,默认仅本机回环安全可用
- 🛠️ 安装即用:装了哪个家族插件,面板里就出现对应模块

## 安装

```
openmod install well404.WebPanel
```

重启服务器或执行 `openmod reload` 后,浏览器访问 `http://<地址>:<端口>` 即可。

## 配置 (config.yaml)

```yaml
web:
  bindAddress: "127.0.0.1"   # 监听地址:127.0.0.1(仅本机)| 0.0.0.0(全部)| 指定 IP
  port: 8080                 # 监听端口
  token: ""                  # 访问令牌;对外暴露(非回环)时必填
  allowInsecurePublic: false # 【危险】允许无令牌对外暴露,请保持 false
```

### 安全说明

- **回环 + 空令牌**:无鉴权,仅本机可访问(默认,最安全)。
- **对外地址 + 设置令牌**:所有 `/api` 调用需带 `X-Web-Token` 头或 `?token=` 查询参数。
- **对外地址 + 空令牌**:插件会自动降级回 `127.0.0.1`,除非显式设置 `allowInsecurePublic: true`(强烈不建议)。

## HTTP 接口

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/`、`/index.html` | 单页应用(无需令牌,便于在 UI 内输入令牌) |
| GET | `/api/modules` | 列出已注册模块及其字段 schema |
| GET | `/api/modules/{module}/{action}/values` | 拉取设置 / 搜索动作的预填值 |
| GET | `/api/modules/{module}/{action}/records` | 列出集合动作的记录 |
| POST | `/api/modules/{module}/{action}` | 提交动作(表单 / 搜索) |
| POST | `/api/modules/{module}/{action}/delete` | 删除集合中的一条记录 |

## 给插件开发者

在你的插件里可选注入 `IWebPanelRegistry`(来自共享库 `UnturnedMods.Shared`),用
`WebPanelModule` 描述若干 `WebPanelAction` 并注册即可;面板会自动渲染。注入为可选——
未安装本面板时注册被跳过,你的插件照常工作。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店,买卖物品 / 组合包,依赖 Economy |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |

完整文档、配置示例与本地调试说明见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
