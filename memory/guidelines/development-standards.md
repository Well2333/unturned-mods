# 开发规范（一级记忆）

## 1. OpenMod Unturned 插件硬性约束

这些是框架层面的强制要求，违反会导致插件**静默失效**或加载失败：

1. **目标框架必须是 `netstandard2.1`**。统一由根目录 `Directory.Build.props`
   设置，插件 `.csproj` **不得**自行覆盖。
   （社区中 `net461` 的旧示例已过时，勿参考。）
2. **`RootNamespace` 必须等于 `AssemblyName`**，否则 `IConfiguration` 与
   `IStringLocalizer` 无法解析。本仓库通过「让项目文件名 = 插件 Id」来自动满足
   （SDK 默认 `RootNamespace`/`AssemblyName` 均取项目名）。**不要**单独覆盖其中之一。
3. 插件主类继承 `OpenMod.Unturned.Plugins.OpenModUnturnedPlugin`，并在程序集级别
   声明 `[assembly: PluginMetadata("<PluginId>", DisplayName = "...")]`。
4. `config.yaml` 与 `translations.yaml` 以 **EmbeddedResource** 形式打包，置于插件
   项目根目录。它们是源码的一部分，**纳入版本控制，不得 ignore**。
5. 任何调用 Unturned / UnityEngine API 的代码，须先 `await UniTask.SwitchToMainThread();`。
6. 生命周期方法 `OnLoadAsync()` / `OnUnloadAsync()` 返回 `UniTask`（Cysharp）。

## 2. 依赖与版本管理

- 采用 **Central Package Management**：所有 NuGet 版本集中在根目录
  `Directory.Packages.props` 声明；插件 `.csproj` 中 `<PackageReference>`
  **不写 `Version`**。
- 插件通常只需引用 `OpenMod.Unturned`（它会传递引入 `OpenMod.API` /
  `OpenMod.Core` / `OpenMod.UnityEngine`）。
- 升级依赖版本：改 `Directory.Packages.props` 一处，并在变更记录中说明。

## 3. 编码规范

- `Nullable` 全程开启，且 `nullable` 警告视为错误（见 `Directory.Build.props`）。
- 私有字段使用 `m_PascalCase` 前缀（与 OpenMod 官方模板一致）。
- 优先通过构造函数注入依赖（`IConfiguration`、`IStringLocalizer`、`ILogger<T>`、
  `IServiceProvider` 等）。
- 跨插件可复用的代码放入 `src/Shared/UnturnedMods.Shared`，保持各插件精简。
- 面向用户的文案走 `translations.yaml` + `IStringLocalizer`，不要硬编码。

## 4. 新建插件

统一使用脚手架，保证结构一致：

```bash
scripts/new-plugin.sh <PluginId> ["Display Name"]
# 例：scripts/new-plugin.sh well404.AutoMessage "Auto Message"
```

它会在 `src/plugins/<PluginId>/` 生成项目并尝试加入解决方案。

## 5. 构建与部署（参考）

- 构建需要 **.NET SDK**（建议 8.0 LTS）；当前环境已装 **SDK 8.0.127**，可直接构建。
- `dotnet build` 因 `GeneratePackageOnBuild=true` 会产出 `.nupkg`。
- 本地构建/测试/部署封装在 **`scripts/build.sh`**：默认把「插件 dll + 非宿主第三方
  依赖」**平铺**装配到仓库根 **`build/`**（构建前清旧产物、构建后只留目标文件；中间
  产物用临时目录丢弃）。**必须平铺**——OpenMod 插件加载器只扫描 `plugins/*.dll` 顶层、
  不递归子目录（`FileSystemPluginAssembliesSource`）。`-o/--deploy <dir>` 覆盖输出目录
  （如服务器 plugins 目录）、`--test` 跑测试。`build/` 产物已 `.gitignore`（保留
  `templates/`）。详见 `docs/README.md`。
- 部署二选一：`openmod install <PackageId[@Version]>`，或把插件 `.dll` 及其
  全部依赖 dll 放入服务器的 `openmod/plugins/`；之后 `openmod reload`。
- **版本打标**：`scripts/build.sh` 构建前会删除 `src/**/obj/$CONFIG/**` 下的
  `*.AssemblyInfoInputs.cache`，强制按当前 `<Version>` + git HEAD 重新生成程序集的
  `AssemblyVersion`/`InformationalVersion`。否则旧 `obj/` 残留可能把**过期版本号**打进
  本地部署的 dll，导致 OpenMod `[loading] <name> v<version>` 日志显示错误版本（NuGet 包
  版本不受影响；CI 全新 checkout 无此问题）。

## 6. 文档同步（强制）

**凡改动任一插件的「面向用户的行为」——命令、配置项、权限、Web 面板模块、依赖关系——
都必须在同一次提交内同步更新相关文档**,文档过时视为未完成:

1. 该插件自己的 `src/plugins/<PluginId>/README.md`(它会被打进 NuGet 包,见 §7)。
2. `docs/<PluginId>.md`(详细参考)。
3. 根 `README.md` 与 `docs/README.md` 的插件总表(新增/删除插件或依赖变化时)。

新插件由 `scripts/new-plugin.sh` 自动生成 README 骨架(来自
`build/templates/plugin/README.md.template`),需在开发时填实,并保持「插件家族」表与
其他插件一致。

## 7. 打包规则(NuGet)

- **每个插件随包发布自己的 `README.md`**(项目目录下),由 `Directory.Build.props` 选取为
  `PackageReadmeFile`;无自带 README 的项目回退到仓库根 README。本地 `dotnet pack` 与 CI
  `publish.yml` 走同一套 props,行为一致。README 内的链接用**绝对 URL**(nuget.org 不渲染
  相对路径)。
- **引用 `UnturnedMods.Shared`(未发布到 NuGet 的共享库)必须**:`ProjectReference` 加
  `PrivateAssets="all"`(避免在 nuspec 里产生无法解析的依赖)**且**用
  `IncludeSharedInPackage` 目标把 `UnturnedMods.Shared.dll` 打进 `lib/`。否则
  `openmod install` 会因解析不到 `UnturnedMods.Shared` 而失败。模板已内置正确写法。
- **表达「插件依赖另一个已发布插件」**(如 Shop 依赖 Economy):对那个可打包项目用
  **不带 PrivateAssets** 的 `ProjectReference`,NuGet 会自动在 nuspec 写入
  `<dependency>`(版本下限 = 被引用项目当前 `<Version>`),`openmod install` 据此自动级联
  安装。**同时发版时先发被依赖者**(如先 Economy 后 Shop),否则依赖在 NuGet 上不可解析。
