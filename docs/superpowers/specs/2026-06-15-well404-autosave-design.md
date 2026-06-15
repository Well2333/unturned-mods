# well404.AutoSave — 设计规范

> 状态：已与用户确认（2026-06-15）。本文件是实现的依据；面向用户的文档以
> `src/plugins/well404.AutoSave/README.md` / `docs/well404.AutoSave.md` 为准。

## 1. 目标

一个 OpenMod Unturned 插件，**按 crontab 墙钟定时保存游戏，并周期性地把存档高压缩备份**：

- 默认每 10 分钟保存一次（`SaveManager.save()`），**按墙钟整点对齐**触发（cron 语义，
  不随服务器启动计时）。
- 每保存 N 次（默认 6）在**同一次保存后**做一次备份。
- 备份目录可自定义。
- 备份用 **LZMA 实体（solid）压缩**，尽量节约体积。
- 备份保留策略：最大数量 + 最大总体积，任一超限即删最旧；**默认均不限制**。
- **无游戏内指令**；全部配置走 `config.yaml`，并在装了 `well404.WebPanel` 时于 WebUI 可编。

## 2. 插件标识

- Id / AssemblyName / RootNamespace：`well404.AutoSave`
- DisplayName：`Auto Save`
- 主类：`AutoSavePlugin`
- WebPanel 模块 id：`well404.autosave`

## 3. 备份范围（实测 `Servers/<id>/` 布局后的默认策略）

模型：**默认备份整个 `Servers/<id>/`，再减去一组「可下载/可再生」的排除 glob**。未知的新
目录默认**包含**（宁可多备，不丢数据）。备份目录始终被自动排除（避免备份套备份）。

默认排除（`backup.excludePatterns`，可在配置/WebUI 改）：

| glob | 排除原因 |
| --- | --- |
| `Workshop/**`、`Steam/**` | 创意工坊下载内容（地图/Mod/Steam 缓存），可重新下载 |
| `Bundles/**` | 资源缓存，可再生 |
| `OpenMod/packages/**` | OpenMod 下载的 NuGet 包缓存（实测约 38M，可重新下载） |
| `**/logs/**`、`**/Logs/**` | 日志 |
| `**/*~`、`**/*.bak` | 临时/备份残留 |

被保留（即真正「必要内容」）：`Level/`（世界）、`Players/`（玩家存档）、`Server/`
（管理员/封禁/白名单）、`Config.txt`、`OpenMod/` 下的角色/用户/各插件配置与数据。
排除掉 38M 的包缓存后，单次备份主要就是真正的存档（压缩前数百 KB 量级）。

匹配规则：glob 相对于存档根（`Servers/<id>/`），`/` 分隔，`*` 不跨目录、`**` 跨目录，
大小写不敏感（Windows/Linux 服务器都跑）。

## 4. 路径解析

- 存档根：`ReadWrite.PATH + ServerSavedata.directory`（即 `<u3ds>/Servers/<serverId>`）。
- 备份目录默认：`ReadWrite.PATH + "/Backups/" + <serverId>`（在 `Servers/` **之外**），
  `backup.directory` 非空则用它（绝对路径原样用；相对路径相对 `ReadWrite.PATH`）。
- 保存计数状态：插件 `WorkingDirectory` 下 `state.json`（跨重启延续 N 次→备份的节奏）。

## 5. 组件

每个单元单一职责、可独立测试：

- **AutoSaveSettings** — `config.yaml` 强类型视图（含默认值）。
- **AutoSaveConfigStore** — 线程安全；WebUI/命令的写入统一重写 `config.yaml`
  （照搬 `VaultConfigStore` 模式）。
- **CronSchedule**（纯逻辑）— 包装 Cronos `CronExpression`，给定「上次/当前」算「下次触发」
  （按 `TimeZoneInfo`）。可单测。
- **SchedulerLoop** — 后台循环：算下次→`Task.Delay`→回调→重复；`CancellationToken` 退出。
- **ExcludeMatcher**（纯逻辑）— glob 列表匹配，相对存档根。可单测。
- **BackupArchiver**（`IBackupArchiver`）— 把「(相对路径, 文件)」流式写入 `TarWriter`
  over `LZipStream`（LZMA solid），产出 `.tar.lz`。封装 SharpCompress。
- **RetentionPolicy**（纯逻辑）— 给定备份清单（名/大小/时间）+ 上限，算出要删除的最旧若干个。可单测。
- **BackupService** — 编排：枚举存档根、按 ExcludeMatcher 过滤、写临时文件再原子改名为
  `autosave-yyyyMMdd-HHmmss.tar.lz`、跑 RetentionPolicy 删旧。`SemaphoreSlim(1,1)` 防并发
  （上次没跑完则跳过本次备份并告警，但保存照常）。
- **SaveService** — 触发时 `await UniTask.SwitchToMainThread(); SaveManager.save();`，
  自增并持久化保存计数；当 `enabled && count % everyNSaves == 0` 时在后台触发 BackupService。
- **SaveStateStore** — `state.json` 读写保存计数。
- **AutoSaveWebPanelModule** — `Settings`（编辑全部配置）+ `Table`（列出备份：名/大小/时间，
  行内删除）+ `Form`（立即保存+备份）。英文源串 + `AutoSaveI18n.ZhTable`。
- **AutoSaveContainerConfigurator** — 把无 `[Service]` 接口的具体类注册进容器。
- **AutoSavePlugin** — `OnLoadAsync`：切主线程、读配置、解析服务、起 SchedulerLoop、（可选）注册
  WebPanel + i18n bundle；`OnUnloadAsync`：取消循环并 await、注销 WebPanel。

## 6. 配置项（全部 WebUI 可编）

```yaml
schedule:
  cron: "*/10 * * * *"   # 标准 5 段 cron；默认每 10 分钟（按墙钟对齐）
  timeZone: ""            # 空=服务器本地时区；否则 IANA/Windows 时区 id
backup:
  enabled: true
  everyNSaves: 6          # 每 N 次保存后备份一次；<=0 视为关闭备份
  directory: ""           # 空=<u3ds>/Backups/<serverId>
  excludePatterns: [ ... 见 §3 默认 ... ]
retention:
  maxCount: 0             # 0=不限
  maxTotalSizeMB: 0       # 0=不限
```

WebUI 字段：cron(Text)、timeZone(Text)、enabled(Boolean)、everyNSaves(Number)、
directory(Text)、excludePatterns(TextArea，逐行)、maxCount(Number)、maxTotalSizeMB(Number)。

## 7. 依赖与打包

- 新增（集中在 `Directory.Packages.props`）：
  - `Cronos` 0.8.4（零依赖，netstandard2.0）— cron 解析。
  - `SharpCompress` 0.30.1（netstandard2.1 闭包最小：仅额外 `System.Text.Encoding.CodePages`）。
  - `System.Text.Encoding.CodePages` 5.0.0 — 显式引用，使 `build.sh` 的 `extra_deps`
    把它平铺到 `openmod/plugins/`（SharpCompress 的传递依赖；flat 部署需要）。
- `openmod install` 路径自动从 NuGet 解析上述依赖闭包；flat 部署（build.sh / 本地服）由上面的
  显式引用保证拷全。**实现时核对** `dotnet publish` 暂存目录的非宿主 dll 集合与拷贝结果一致。
- Shared 引用、nupkg 打包沿用模板写法（`PrivateAssets="all"` + `IncludeSharedInPackage`）。

## 8. 线程模型

- `SaveManager.save()` 必须主线程：保存回调先 `SwitchToMainThread()`。
- 备份是纯文件 IO，放后台线程（save 已同步落盘，文件一致）。
- WebPanel handler 在 HttpListener 线程：触碰 Unturned API 前 `SwitchToMainThread()`；
  纯文件操作（列备份/删备份/触发备份）不需要切主线程，但「立即保存」要切。

## 9. 测试

- 纯逻辑单测（`tests/well404.AutoSave.Tests`）：
  - CronSchedule：给定 cron + 起点算下次时间。
  - ExcludeMatcher：`*`/`**`/大小写/相对根 各分支。
  - RetentionPolicy：仅数量超限 / 仅体积超限 / 都超 / 都不超 / 边界。
  - BackupArchiver：写入若干文件→读回（SharpCompress 解 `.tar.lz`）校验内容往返。
- 游戏相关（`SaveManager.save`、路径解析、WebPanel HTTP）走 `.localserver` 自测，产出
  游戏内待测报告（`PENDING-INGAME-TESTS.md`）。

## 10. 文档同步（强制）

新增插件须同时写：`src/plugins/well404.AutoSave/README.md`、`docs/well404.AutoSave.md`、
根 `README.md` 与 `docs/README.md` 的插件总表；并写 `memory/changelog/` 变更记录。
```
