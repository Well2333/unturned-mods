# 发布规范：NuGet 与 GitHub（一级记忆）

## 仓库

- GitHub：`https://github.com/Well2333/unturned-mods`（public）。
- 默认分支 `main`。`Directory.Build.props` 中的 `RepositoryUrl` /
  `PackageProjectUrl` 指向该仓库；更名/迁移仓库时须同步修改。

## CI（`.github/workflows/ci.yml`）

- 触发：push / PR 到 `main`，以及手动。
- 步骤：restore → `dotnet build -c Release` →（预留）`dotnet test`。
- 新增测试项目命名为 `*.Tests` 即会被 `dotnet test` 自动纳入。

## NuGet 发布（`.github/workflows/publish.yml`）

- **触发：发布 GitHub Release 时**（也支持 Actions 页手动 `workflow_dispatch`）。
- 步骤：restore → `dotnet pack -c Release` → `dotnet nuget push *.nupkg
  --skip-duplicate`。
- `--skip-duplicate`：只有 nuget.org 上尚不存在的版本才会真正发布。**因此发布前
  必须在对应插件 `.csproj` 中递增 `<Version>`**，否则该插件会被跳过。
- 需要仓库 Secret **`NUGET_API_KEY`**（见下，尚未配置时发布会失败并给出提示）。

### 发布一个新版本的标准动作

1. 在要发布的插件 `.csproj` 里递增 `<Version>`（遵循 SemVer）。
2. 提交并按两步提交流程写变更记录。
3. 在 GitHub 上创建 Release（tag 建议 `<PluginId>-vX.Y.Z` 或 `vX.Y.Z`）。
4. CI 自动 pack 并 push；只有版本号更新过的插件会被发布。

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
