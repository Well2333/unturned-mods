# 发布规范：NuGet 与 GitHub（一级记忆）

## 仓库

- GitHub：`https://github.com/Well2333/unturned-mods`（public）。
- 默认分支 `main`。`Directory.Build.props` 中的 `RepositoryUrl` /
  `PackageProjectUrl` 指向该仓库；更名/迁移仓库时须同步修改。

## CI（`.github/workflows/ci.yml`）

- 触发：push / PR 到 `main`，以及手动。
- 步骤：restore → `dotnet build -c Release` → `dotnet test` → `dotnet pack`（打包
  校验，产物丢弃，用于在发布前发现打包错误）。
- 测试项目放在 `tests/`，命名 `*.Tests`，会被 `dotnet test` 自动纳入（见
  [testing.md](testing.md)）。

## NuGet 发布（`.github/workflows/publish.yml`）— **按插件独立发布**

本仓库是多插件 monorepo，发布按**单个插件**粒度进行，每个插件独立维护版本号。

- **触发：发布 GitHub Release 时**（也支持 Actions 页手动 `workflow_dispatch`，输入
  要发布的 `PluginId` 或 `all`）。
- **Release tag 约定 `<PluginId>/v<Version>`**（如 `well404.Economy/v0.2.0`）：
  workflow 解析 tag，**只 build/pack/push 该插件**，并校验 tag 版本与该插件 `.csproj`
  的 `<Version>` 一致（不一致直接报错，防止发布过期版本）。
- tag **不**符合该格式时（如 `v1.0.0`）→ 回退为**发布所有**可打包插件（用于一次性
  协调发布全仓库）。
- `--skip-duplicate`：只有 nuget.org 上尚不存在的版本才会真正发布。**发布前必须在
  对应插件 `.csproj` 中递增 `<Version>`**。
- 需要仓库 Secret **`NUGET_API_KEY`**（见下，尚未配置时发布会失败并给出提示）。

### 版本与发布脚本

- `scripts/plugin-version.sh <PluginId> [newVersion]`：读取 / 设置某插件 `<Version>`
  （SemVer 校验）。
- `scripts/release-plugin.sh <PluginId> [--notes "..."]`：读取该插件版本，创建
  tag `<PluginId>/v<Version>` 与对应 GitHub Release（用 `gh`），触发发布。要求工作区
  干净（版本递增应已提交）。

### 发布一个插件新版本的标准动作

1. `scripts/plugin-version.sh <PluginId> <newVersion>` 递增版本（SemVer）。
2. 提交版本递增，并按两步提交流程写变更记录。
3. `scripts/release-plugin.sh <PluginId> --notes "..."` 创建 Release（tag
   `<PluginId>/v<newVersion>`）。
4. publish workflow 自动只 pack & push 该插件（版本不匹配会报错）。

### 配置 NUGET_API_KEY（一次性，密钥不要写进仓库/对话）

```bash
gh secret set NUGET_API_KEY --repo Well2333/unturned-mods
# 然后按提示粘贴 nuget.org 的 API Key
```

## 包元数据（集中在 `Directory.Build.props`）

- `Authors=well404`、**CC BY-NC-SA 4.0** 许可。该许可非 OSI/FSF 许可，NuGet 的
  `PackageLicenseExpression` 不接受，故以**捆绑许可文件**方式发布：根目录 `LICENSE`
  打包进每个包，并用 `PackageLicenseFile=LICENSE` 指向它。
- 仓库内 `README.md` 作为包说明（`PackageReadmeFile`）。
- **许可含义**：署名-非商业性使用-相同方式共享。允许复制、修改、再发布，但须署名、
  不得用于商业目的、衍生作品须以相同许可发布。更换许可须同步改 `LICENSE`、
  `Directory.Build.props` 与本规范。
- 启用 SourceLink（`Microsoft.SourceLink.GitHub`）+ 符号包（`.snupkg`），
  发布的包可调试、可溯源到 GitHub 源码。
- 插件包 Id = 插件项目名 = 插件 Id（如 `well404.AutoMessage`）。NuGet Id 全局唯一，
  统一用 `well404.` 前缀。

## 已知注意事项

- **共享库 `UnturnedMods.Shared` 标记 `IsPackable=false`，不单独发布。** 一旦某个
  插件通过 `ProjectReference` 引用它并要打包发布，需要把 `UnturnedMods.Shared.dll`
  **嵌入插件包**（而非作为 NuGet 依赖），否则安装方会缺少该依赖。届时在该插件
  `.csproj` 处理（如把引用程序集加入包的 lib，或改为源码共享），并更新本规范。
- 部署到服务器仍是 `openmod install <PackageId>` 或手动放 dll 到 `openmod/plugins`，
  与发布到 NuGet 是两件事。
- **第三方 NuGet 依赖**（如 `well404.Economy` 依赖 `LiteDB`）会作为包依赖写入
  `.nuspec`，`openmod install` 会自动拉取。但**手动 dll 部署**时必须把这些依赖 dll
  （如 `LiteDB.dll`）一并放入 `openmod/plugins`，否则运行时缺失。
- **插件间依赖走抽象、不走 ProjectReference**：`well404.Shop` 不引用
  `well404.Economy`，而是注入共享抽象 `IEconomyProvider`（由 Economy 以全局服务实现）。
  因此 Shop 运行时需要任一实现了 `IEconomyProvider` 的经济插件在场（见
  [architecture.md](architecture.md)）。
