# 测试规范（一级记忆）

OpenMod 插件大量逻辑依赖 Unturned 游戏运行时（无法在 CI / 本地无游戏环境跑），
因此**单元测试只覆盖与游戏运行时无关的纯逻辑**；其余靠编译期检查 + 真实服务器手测。

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
