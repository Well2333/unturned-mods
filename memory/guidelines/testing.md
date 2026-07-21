# 测试规范（一级记忆）

OpenMod 插件大量逻辑依赖 Unturned 游戏运行时（无法在 CI / 本地无游戏环境跑），
因此**单元测试只覆盖与游戏运行时无关的纯逻辑**；其余靠编译期检查 + 真实服务器手测。

## 用户控制的本地验证节奏（硬性）

- 日常修改、代码审核和迭代修复完成后，默认只执行与改动范围相称的 `dotnet build`
  （通常为 Release 构建），确认代码能够编译和打包；**构建请求不等于测试授权**。
- 除非用户在当前任务中明确要求“测试 / 详细测试 / 服务器验证”，否则不要自行运行
  `dotnet test`、完整测试套件、浏览器脚本测试或 `.localserver` 真实服务器测试。
- 用户明确要求测试时，再按风险选择目标单元测试、全解决方案测试和真实服务器验证，并记录
  已覆盖范围；没有改动过的代码不重复测试。
- CI 中既有的自动测试规则不受本节影响；本节约束的是 AI 在本地迭代期间主动触发测试的节奏。

## 浏览器 UI 发布前验收（硬性）

- 只要修改 WebPanel、插件自建 HTML/CSS/JavaScript 或浏览器交互，发布或部署前都必须在真实浏览器
  中加载实际页面并逐项操作本次修改过的路径；仅做 JavaScript 语法检查、接口返回检查或确认控制台
  “没有报错”不算完成。
- 验收至少覆盖：主要桌面宽度下的布局与信息层级、标签切换、弹窗打开/关闭、表单输入与提交、
  删除/危险操作确认、自动刷新后的状态保持、空数据和长文本，以及本次新增的增删改查完整闭环。
- 必须检查浏览器控制台、网络请求和页面可见结果是否同时正确，并对明显拥挤、过宽、过高、错位、
  难以辨识或与同类玩家页面风格不一致的 UI 主动修正；“功能能调用”不等于 UI 合格。
- 发布前在测试记录或 changelog 中写清实际操作过的页面和场景。无法自动完成的浏览器验收必须明确
  列为待用户检查项，不能用“构建通过”代替，也不能在未说明的情况下直接发布。

## 测试环境选择与关停（最高优先级硬性规则）

- 远程 `TestServer` **只能**用于明确需要用户本人参与的实机检查，例如用户进服操作、观察游戏内
  行为或亲自查看只能在远程环境访问的界面。若测试内容不需要用户参与，绝对不得启动
  `TestServer`。
- 插件加载、日志、HTTP/API、控制台命令、配置读写、自动化脚本等无需用户参与的验证，一律优先
  使用仓库本地 `.localserver`；可由单元测试覆盖的继续优先用单元测试，不得无故升级到服务器。
- 任意测试环境服务器（包括 `.localserver` 与远程 `TestServer`）只要当前没有正在等待用户参与的
  检查步骤，测试结束后必须立即关闭，禁止为了“后续可能还要用”而长时间常驻。
- 用户明确要求稍后参与检查时，才可让对应测试服在约定的短期等待窗口内保持运行；用户完成、取消
  或该人工检查不再需要后立即关闭。

## 放置与命名

- 测试项目放在仓库根的 `tests/` 下，命名 `<Something>.Tests`，如
  `tests/well404.Economy.Tests/`。
- 加入 `UnturnedMods.sln`；CI 的 `dotnet test` 会自动纳入。

## 测试项目的 csproj 约定

由于 `Directory.Build.props` 把**所有**项目默认设成 `netstandard2.1`、并打包元数据，
测试项目需在自己的 `.csproj` 覆盖：

- `<TargetFramework>net8.0</TargetFramework>`（真正的测试宿主，而非插件的
  `netstandard2.1`）。
- `<IsPackable>false</IsPackable>`、`<GeneratePackageOnBuild>false</GeneratePackageOnBuild>`
  （测试**不**发布）。
- `<IsTestProject>true</IsTestProject>`。
- 测试栈版本仍走 CPM（`Directory.Packages.props` 里已加 `xunit` /
  `xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk`）。
- `NU1701` 已在根 `Directory.Build.props` 的 `NoWarn` 里抑制：net8 测试项目引用
  netstandard2.1 插件（其再传递引用 net461 的游戏程序集）不会因此报错。

## 只测"游戏无关"的类型

- 可测：纯逻辑/数据层。例如 `SqliteCurrencyBackend`（仅依赖 SQLite ADO.NET provider + 经济抽象）、
  `DiscountService.GetMultiplierAsync` / `ApplyDiscount`（仅依赖 OpenMod 抽象 +
  本地 config 绑定）。这些类型**不触碰** `SDG.Unturned` / `UnityEngine`，故能在
  纯 .NET 宿主里实例化运行。
- **不要**在测试里实例化 `UnturnedPlayer` / 物品 / 玩家经验等游戏类型——会触发加载
  net461 游戏程序集而失败。需要这些依赖时，用手写的轻量 fake/接口实现替身
  （如测试里对 `IPermissionChecker` / `IPermissionActor` 的 fake）。

## 运行

```bash
dotnet test UnturnedMods.sln -c Release
```

> 设计上：把可测的纯逻辑从依赖游戏运行时的代码里**分离出来**（如把余额计算、折扣
> 计算独立成类），就能最大化可测面、最小化"只能上服务器手测"的部分。
