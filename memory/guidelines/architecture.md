# 架构与仓库结构（一级记忆）

本仓库是一个**多插件 monorepo**：包含若干彼此独立的 OpenMod Unturned 插件，
它们共享一致的构建/运行环境。设计目标是「每个插件可独立构建与部署，但全仓库
环境统一、复用最大化」。

## 目录结构

```
unturned-mods/
├── UnturnedMods.sln              # 汇总所有项目的解决方案
├── Directory.Build.props         # 全仓库共享的 MSBuild 属性（统一环境）
├── Directory.Packages.props      # 集中式依赖版本管理（CPM）
├── global.json                   # 固定 .NET SDK 版本带
├── nuget.config                  # NuGet 源
├── src/
│   ├── Shared/
│   │   └── UnturnedMods.Shared/  # 跨插件共享库
│   └── plugins/
│       └── <PluginId>/           # 每个插件一个目录（脚手架生成）
├── build/
│   └── templates/plugin/         # 新建插件用的模板
├── scripts/
│   └── new-plugin.sh             # 插件脚手架
├── docs/                         # 面向人的文档（可选）
└── memory/                       # 长期记忆（见 memory/README.md）
    ├── guidelines/               # 一级：项目规范及开发守则
    └── changelog/                # 二级：变更记录
```

## 共享一致环境的设计决策

> 这些决策是「环境一致」目标的落地方式，修改前请评估对所有插件的影响。

- **`Directory.Build.props`**：在仓库根集中设定 `TargetFramework`、`Nullable`、
  `LangVersion`、警告策略、包元数据等。所有项目自动继承，避免逐插件漂移。
- **Central Package Management（`Directory.Packages.props`）**：所有插件锁定到
  同一组依赖版本，确保运行时环境一致。
- **命名即约束**：插件项目文件名 = 插件 Id，从而自动满足
  `RootNamespace == AssemblyName`（OpenMod 的硬性要求）。
- **共享库 `UnturnedMods.Shared`**：放置公共基类、扩展方法、工具与**跨插件抽象**
  （如 `IWebPanelRegistry`）。注意：插件引用它时，编译出的 `UnturnedMods.Shared.dll`
  需与插件一起部署到 `openmod/plugins`。**`scripts/build.sh` 会平铺 `ProjectReference`
  产物**（`project_deps` 按「项目文件名 = AssemblyName」约定取 dll 名），所以引用 Shared
  的插件部署时会自动带上 `UnturnedMods.Shared.dll`。**NuGet 发布**：Shared 不是独立发布的包，
  故引用它的插件 `.csproj` 用 `TargetsForTfmSpecificBuildOutput` + `BuildOutputInPackage` 把
  `UnturnedMods.Shared.dll` 打进自身 nupkg 的 `lib/`，否则 `openmod install` 装出来会缺该 dll。
- **脚手架 `scripts/new-plugin.sh`**：保证每个新插件结构、约束一致，降低 AI
  逐个手写时的偏差。

## 插件间协作：依赖抽象，不依赖具体插件

跨插件能力（如经济系统）通过 **OpenMod 的全局服务抽象**暴露，消费方注入抽象接口，
**不**对提供方建立 `ProjectReference`：

- 提供方用 `[ServiceImplementation(Lifetime = Singleton)]` 实现某个 `[Service]` 抽象
  接口（如 `IEconomyProvider`，来自 `OpenMod.Extensions.Economy.Abstractions`）。该实现
  在**插件作用域**内构造，因此可直接注入插件的 `IConfiguration` / `IOpenModComponent`
  等；但对**其他插件全局可见**。（与官方 `OpenMod.Economy` 同构。）
- 消费方（如 `well404.Shop`）只注入抽象 `IEconomyProvider`，对提供方插件无编译期依赖，
  仅有**运行时顺序依赖**：场上需有任一经济插件提供该实现。
- 好处：插件可独立编译/发布/替换；第三方插件（每日签到等）也能用同一抽象给玩家发币。

### 两条硬规则（已在真实服务器验证，违反会运行时报错而非编译报错）

1. **全局服务（`[ServiceImplementation]`）不要在构造函数注入插件作用域的服务**
   （如插件的 `IConfiguration`、`IOpenModComponent`）。全局单例在**全局作用域**激活，
   那里没有这些插件服务，Autofac 会报 “None of the constructors … can be invoked”。
   正确做法：注入 **`IPluginAccessor<TPlugin>`**（全局服务）+ 需要的其它全局服务
   （如 `IUserManager`），在调用时**懒**读取
   `accessor.Instance.LifetimeScope.Resolve<IConfiguration>()` 与
   `accessor.Instance.WorkingDirectory`。
2. **没有 `[Service]` 接口的具体类，不能用 `[ServiceImplementation]` /
   `[PluginServiceImplementation]` 注册**——OpenMod 只会警告
   “marked as ServiceImplementation but does not inherit any services” 并**跳过**，
   于是注入它的命令运行时报 “Unable to resolve service …”。正确做法：实现
   `IPluginContainerConfigurator`，在 `ConfigureContainer` 里
   `context.ContainerBuilder.RegisterType<T>().AsSelf().SingleInstance()`。

## WebPanel：可选的跨插件注册式扩展（well404.WebPanel）

`well404.WebPanel` 是一个**通用 Web 管理面板基底**，自身不含任何具体业务功能；
其他插件把「管理动作」**注册**进来，面板按元数据动态渲染。这是「依赖抽象、不依赖
具体插件」的又一落地，沿用经济抽象同构的全局服务模式：

- 抽象 `IWebPanelRegistry`（`[Service]`，定义在 `UnturnedMods.Shared.WebPanel`）+
  描述符模型（`WebPanelModule` / `WebPanelAction` / `WebField` / `WebActionResult`）。
  描述符**不含 JSON 类型**，只建模 UI 语义，handler 是
  `Func<WebActionRequest, Task<WebActionResult>>` 异步闭包。动作种类（`WebActionKind`）：
  `Table`（进页面自动加载的只读表）、`Form`（带独立按钮的命令，如增减余额）、
  `Search`（实时检索）、`Settings`（进页面用 `Loader` 预填、由页尾**统一保存**按钮批量提交）、
  `Collection`（CRUD：`RecordsLoader` 出条目 + 新增 + 点选编辑 + `DeleteHandler` 删除，
  按 `KeyField` upsert）。余额、商品目录用 `Collection`；货币/击杀奖励/转账/折扣用 `Settings`。

  **Collection 展示约定（守则）**：
  - **目录型数据**（如商店商品）用**小方块网格**（tile，`Layout` 默认）；
    **按实体的数据**（如玩家余额）用**纵向列表**（`Layout = "list"`）。
  - **一个槽位内有多个元素时**（如礼包的多件物品），每个元素用独立的**胶囊（pill）徽章**
    渲染、彼此间隔——通过 `WebRecord.Tags` 传入，**不要**把多元素塞进一串括号文本
    （如 `名称（A，B，C）`）里，胶囊更易辨认。`WebRecord.Label` 只放标题（如商品名/玩家名）。
- 提供方 `well404.WebPanel` 用 `[ServiceImplementation(Singleton)]` 实现
  `WebPanelRegistry`（**全局单例、构造不注入任何插件作用域服务**，遵守上文硬规则 1）；
  HTTP 宿主由插件本体在 `OnLoadAsync` 起、`OnUnloadAsync` 停。
- 消费方（如 `well404.Economy`）在 `OnLoadAsync` 里用
  **`LifetimeScope.ResolveOptional<IWebPanelRegistry>()`** 取得注册表——**可选注入**，
  没装 WebPanel 时返回 null、插件照常工作；拿到则 `RegisterModule(...)`，`OnUnloadAsync`
  里 `UnregisterModule(...)`。此外必须监听 `PluginLoadedEvent` 并幂等重试注册：全局服务通常
  在插件加载前已经可解析，但 `openmod reload`、不同安装组合与框架初始化顺序不能依赖这一
  偶然时序。注册表按 id 覆盖，因此重复注册安全；插件仍须保存成功取得的 registry，并在
  unload 时从该实例注销，禁止从已释放的插件作用域重新 Resolve。

**铁律：宿主（well404.WebPanel）绝不内含任何具体插件的业务逻辑或 id 判断。** 两个内嵌
SPA（`index.html` 管理面、`player.html` 玩家面）只按**通用描述符字段**渲染，**不得**出现
`menu.id === "shop"` 这类对某插件的特判。任何插件想要的样式/布局都必须经由通用字段表达，
由该插件**注册数据**驱动，从而保证「不同人不同插件的任意组合都不会让面板出问题」。曾经把
商店专属渲染写进 `player.html` 是反面教材，已重构为下述通用能力：

- **玩家菜单渲染模型（`IPlayerMenu` → `PlayerMenuView`/`PlayerCard`）**，全部为通用 UI 语义：
  - `PlayerMenuView.Layout`：`"list"`（紧凑行）或 null（默认卡片）。任何插件可选。
  - `PlayerCard.Group`：分区标题；宿主按卡片给出的 group **顺序**分组、加小标题+计数。
    分区文案由插件用 `m_Tr.Resolve` 自行本地化后填入（宿主不认识其含义）。
  - `PlayerCard.Badge`：紧凑布局下的前置短徽章（如物品 id）。
  - `PlayerCard.Tags`：胶囊徽章（如礼包内容）；列表行有 tags 时整行占满宽度。
  - `PlayerButton.Style`：`primary`/`success`/`danger`/null —— 通用配色，价格等动态文本由插件
    拼进按钮 `Label`（玩家菜单按钮的 Label 是已本地化文本，不是键）。
- **通用管理面扩展**（供任意插件复用，非商店专属）：
  - `WebPanelAction.Hidden`：只可被 id 调用、不渲染自己的卡片（如表格行内动作的目标）。
  - `WebActionResult.WithRowAction(actionId, label, rowKeys?)`：给 `Table`/`Search` 结果的每行
    挂一个按钮，点击即以该行 key（缺省取首列）调用本模块的 `actionId`。商店「检索→＋快速加入
    商品」即用它把搜索结果行接到隐藏的 `additem` 动作。

宿主技术决策：用 BCL `System.Net.HttpListener` + 手写极简 JSON，**零额外 NuGet 依赖**
（避免 `build.sh` 漏拷第三方包的传递子依赖，也规避 Mono 兼容风险）。监听/鉴权：
`bindAddress` 非回环时**强制 token**（缺 token 自动降级回 `127.0.0.1`），
`allowInsecurePublic` 为隐藏的绕过开关。前端是内嵌的单文件通用 SPA（EmbeddedResource）。

handler 在 HttpListener 线程上执行，**凡触及 Unturned API 的 handler 必须先
`await UniTask.SwitchToMainThread()`**（与硬性约束 5 一致）。

## 每个插件目录的标准布局

```
src/plugins/<PluginId>/
├── <PluginId>.csproj            # 文件名 = 插件 Id（满足命名约束）
├── <Class>Plugin.cs             # 继承 OpenModUnturnedPlugin + PluginMetadata
├── config.yaml                  # EmbeddedResource
└── translations.yaml            # EmbeddedResource
```
