# 本地服务器自测与待测报告（一级记忆）

> 适用于**任何大规模改动**：新增插件、跨插件接线（全局服务 / 注册式扩展）、
> 改动插件加载 / DI / 配置 / 构建部署、或任何可能影响「能否加载、控制台行为、
> 对外接口」的修改。改动完成后**必须**在本地服务器自测，再视为完成。

## 为什么

编译通过 ≠ 运行可用。OpenMod 的很多错误是**运行期**才暴露的（DI 解析失败、
`[ServiceImplementation]` 被跳过、配置/本地化解析失败、插件静默不加载、端口冲突
等，见 architecture.md 的两条硬规则）。仓库自带本地测试服 `.localserver`，应在
提交前用它把「非游戏内即可验证」的部分跑一遍。

## 本地测试服位置与控制

- 目录：`/home/well404/unturned-mods/.localserver`（**gitignored**，不入版本控制）。
- 控制脚本：`.localserver/server-ctl.sh`
  - `start` — 在 tmux 会话 `u3ds` 里启动 LAN 服 “Test”
  - `stop` — 关服
  - `cmd <...>` — 向控制台发命令（如 `cmd balance 7656...`、`cmd eco give 7656... 500`）
  - `log` — tail 捕获的 `server.log`
  - `attach` — 进入实时控制台（`Ctrl-b d` 脱离）
- 插件部署目录（**平铺**）：`.localserver/u3ds/Servers/Test/OpenMod/plugins/`
  把插件 dll 及其非宿主依赖（含 `UnturnedMods.Shared.dll`）拷进去后**完整重启测试服**。
  只有未替换 DLL、单纯验证同版本生命周期或配置重载时才执行 `openmod reload`。可用 `scripts/build.sh <PluginId> -d .localserver/u3ds/Servers/Test/OpenMod/plugins`。
- 沙箱注意：本仓库 AI 运行环境对 `/tmp` 与套接字有限制。跑构建/服务器时用
  `dangerouslyDisableSandbox` 并把 `TMPDIR` 指到可写目录（如 `~/.tmpbuild`）；若联网
  受限，离线构建用 `dotnet restore --source ~/.nuget/packages` + `dotnet build --no-restore`，
  必要时加 `-m:1 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0` 规避
  MSBuild 子节点命名管道被拦。

## 自测范围（能在游戏外验证的，全部要测）

1. **加载成功**：`server.log` 里目标插件出现加载日志、**无** `error` / 异常堆栈 /
   “Unable to resolve service” / “does not inherit any services” / 端口占用等。
2. **控制台可操作内容**：所有不需要在线玩家即可执行的命令（如 `eco give/take/set`、
   `balance <id>`、`shop`、`openmod plugins`、自定义管理命令）。
3. **其他对外接口**：不依赖游戏客户端即可验证的面（如 `well404.WebPanel` 的 HTTP API，
   用 `curl http://127.0.0.1:<port>/api/modules` 及对动作端点 `POST` 表单，校验注册、
   鉴权、返回结构、增删改是否落库/落配置）。
4. **干净卸载/关服**：`stop` 后无报错、无残留锁文件（如 LiteDB 文件被占用）。

## 必须产出：待测报告

自测完成后，在 `.localserver/` 写一份**待测报告** `PENDING-INGAME-TESTS.md`，
内容是「**只能在游戏内 / 需要在线玩家或实体世界才能验证**」的项，逐条给出：
- 前置条件（如：1 名在线玩家、背包有某物品）
- 操作步骤
- 预期结果
- 关联的本次改动点

报告随 `.localserver` 留在本地给人看（gitignored，不提交）。每次大改后**覆盖更新**它。

## 已测试记录与「不重复测试」原则

- 自测通过后，在 `.localserver/TESTED-LOG.md` 记录**每个测试项及其覆盖的代码文件**
  （连同结果、日期、所用环境）。
- **再次自测前必须先查**：某测试项的「覆盖代码」自上次验证后**是否被改动**。
  - **未改动 → 不重测，更不要再请用户测同样的东西**（尤其是需要在线玩家配合的项）。
  - 改动了 → 只重测受影响的项。
- 需要玩家在线/实体世界才能验证的项尤其要珍惜：用户配合一次后，除非相关代码变化，
  不应再要求重复同一操作。

## 与提交流程的关系

- 自测属于改动的一部分，应在「功能提交」之前完成；自测发现的问题要在同一改动里修掉。
- 自测做了什么、结论如何，简述进 `memory/changelog/` 的变更记录「注意事项」一节。
- 若自测暴露出新的硬性约束/坑，按规则同步更新对应 guideline（多为 architecture.md）。
