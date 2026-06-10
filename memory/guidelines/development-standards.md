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
- 本地构建/测试/部署封装在 **`scripts/build.sh`**（`--test` 跑测试、`--deploy <dir>`
  发布并只复制「插件 dll + 非宿主第三方依赖」到本地服务器 plugins 目录）。详见
  `docs/README.md`。
- 部署二选一：`openmod install <PackageId[@Version]>`，或把插件 `.dll` 及其
  全部依赖 dll 放入服务器的 `openmod/plugins/`；之后 `openmod reload`。
