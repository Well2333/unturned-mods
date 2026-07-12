# 发布规范：NuGet 与 GitHub（一级记忆）

> ⚠️ **发版需用户当次明确授权**：本文件所有「创建 Release / 触发发布 / 推送」动作，
> 只能在用户当次明确要求时执行，不得擅自进行（详见
> [commit-and-memory-workflow.md](commit-and-memory-workflow.md) 的最高红线）。

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
- **认证用 Trusted Publishing（OIDC，无长期密钥）**，不再需要 `NUGET_API_KEY`
  Secret（见下）。

### 版本与发布脚本

- `scripts/plugin-version.sh <PluginId> [newVersion]`：读取 / 设置某插件 `<Version>`
  （SemVer 校验）。
- `scripts/release-plugin.sh <PluginId> [--notes "..."]`：读取该插件版本，创建
  tag `<PluginId>/v<Version>` 与对应 GitHub Release（用 `gh`），触发发布。要求工作区
  干净（版本递增应已提交）。

### 发布一个插件新版本的标准动作

> 若插件依赖的新 Shared 版本尚未发布，先发布 `well404.UnturnedMods.Shared`，等待 NuGet 可解析后再发布插件。

1. `scripts/plugin-version.sh <PluginId> <newVersion>` 递增版本（SemVer）。
2. 提交版本递增，并按两步提交流程写变更记录。
3. `scripts/release-plugin.sh <PluginId> --notes "..."` 创建 Release（tag
   `<PluginId>/v<newVersion>`）。
4. publish workflow 自动只 pack & push 该插件（版本不匹配会报错）。

### 认证：Trusted Publishing（OIDC，免长期密钥）

`publish.yml` 通过 NuGet **Trusted Publishing** 认证：用 GitHub Actions 的 OIDC
令牌向 nuget.org 换取**有效期 1 小时、一次性**的临时 API Key，**无需配置
`NUGET_API_KEY` 长期密钥**。要点：

- workflow 中 job 需 `permissions: id-token: write`；用 `NuGet/login@v1`
  （`user: Well404` 为 nuget.org 用户名 / profile name，非邮箱）换取临时 key，
  紧接着 `dotnet nuget push --api-key <临时key>`（login 必须紧邻 push，1 小时即过期）。
- **一次性前置（网页操作）**：登录 nuget.org → 用户名菜单 → **Trusted Publishing**
  → 新建策略：Repository Owner=`Well2333`、Repository=`unturned-mods`、
  Workflow File=`publish.yml`（**只填文件名**）、Environment 留空。策略按**账号
  所有者**维度生效，覆盖名下所有包 ID（含全新包 ID 的首发）。
- 仓库为 **public**，故策略首发即永久生效（私有库才有 7 天待激活窗口）。
- 该特性 nuget.org 灰度发布；账号用户名菜单里看不到 **Trusted Publishing** 即尚未
  开放，届时回退到 API Key 方式（`gh secret set NUGET_API_KEY ...` + workflow 改回
  用 `secrets.NUGET_API_KEY`）。
- 改 nuget.org 用户名 / 仓库 owner / workflow 文件名时，须同步更新 nuget.org 策略
  与 `publish.yml` 里的 `user:`。

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

- **共享库以 `well404.UnturnedMods.Shared` 独立发布**。所有插件通过普通 ProjectReference
  生成真实 NuGet 依赖；Shared 必须先于依赖它的新插件版本发布。严禁把 Shared.dll 分别内嵌
  到插件包，否则 OpenMod 逐包加载时会产生重复程序集身份并破坏跨插件服务/registry。
- 部署到服务器仍是 `openmod install <PackageId>` 或手动放 dll 到 `openmod/plugins`，
  与发布到 NuGet 是两件事。
- **第三方 NuGet 依赖**（如 `well404.Economy` 依赖 `LiteDB`）会作为包依赖写入
  `.nuspec`，`openmod install` 会自动拉取。但**手动 dll 部署**时必须把这些依赖 dll
  （如 `LiteDB.dll`）一并放入 `openmod/plugins`，否则运行时缺失。
- **业务插件间优先走抽象**；确属硬依赖时用普通 ProjectReference 生成包依赖。当前 Shop
  对 Economy 是硬依赖，安装 Shop 会自动安装 Economy；Shared 是所有插件的运行时程序集依赖。
