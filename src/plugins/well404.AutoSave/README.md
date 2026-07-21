# well404.AutoSave

> 定时保存游戏并周期性高压缩备份存档。**无游戏内指令**——只通过 `config.yaml` 与
> （可选的）`well404.WebPanel` 管理面板配置。

`well404.AutoSave` 是 **well404 OpenMod 插件家族** 的一员。

## 功能

- **定时保存**：按 **cron 表达式**触发 `SaveManager.save()`，默认每 10 分钟一次。按**墙钟对齐**
  （像 crontab 一样在 `:00/:10/:20…` 触发），**不随服务器启动计时**。
- **周期备份**：有玩家时每保存 N 次（默认 6）在该次保存后顺带做一次备份。保存计数**持久化**，重启后延续。
- **空服限频**：最后一名玩家离线后的下一次常规备份照常执行，随后默认每 24 小时备份一次；
  首名玩家重新上线时只恢复普通备份周期，不立即保存或备份。**所有自动保存始终按 cron 执行，不会降频。**
- **高压缩**：备份为 **LZMA 实体压缩**的 `.tar.lz`（跨文件整体压缩，体积最小；可用 7-Zip / lzip 解开）。
- **自定义备份目录**：默认 `<安装目录>/Backups/<服务器id>`（在存档目录之外，避免备份套备份）。
- **智能排除**：默认只备份「真正的存档」，排除创意工坊下载、OpenMod 包缓存、日志、临时文件等
  可下载/可再生内容（规则可改）。
- **保留策略**：最大备份数 + 最大总体积，任一超限即删最旧；**默认均不限制**，且永远保留最新一份。
- **Web 面板集成**（可选）：装了 `well404.WebPanel` 时，可在面板里编辑全部配置、查看/删除备份、
  「立即备份」。中英双语。

## 安装

```
openmod install well404.AutoSave
```

安装或更新 DLL 后必须完整重启服务器；仅修改同版本配置时才使用 `openmod reload`。`openmod install` 会自动一并安装其依赖
（`Cronos`、`SharpCompress`）。

## 命令

本插件**不提供任何游戏内指令**。所有设置都在 `config.yaml`，或装了
[`well404.WebPanel`](https://www.nuget.org/packages/well404.WebPanel/) 时在管理面板里完成。

## 配置 (config.yaml)

```yaml
schedule:
  cron: "*/10 * * * *"   # 标准 5 段 cron；默认每 10 分钟（按墙钟对齐）
  timeZone: ""            # 空=服务器本地时区；否则 IANA/Windows 时区 id
backup:
  enabled: true
  everyNSaves: 6          # 每 N 次保存后备份一次；<=0 关闭备份
  directory: ""           # 空=<安装目录>/Backups/<服务器id>
  excludePatterns:        # 相对存档根的排除 glob（* 不跨目录、** 跨目录、大小写不敏感）
    - "Workshop/**"
    - "Steam/**"
    - "Bundles/**"
    - "OpenMod/packages/**"
    - "**/logs/**"
    - "**/Logs/**"
    - "**/*~"
    - "**/*.bak"
idleBackup:
  enabled: true            # 空服备份限频；不影响自动保存
  intervalHours: 24        # 空服首次常规备份后，每 24 小时备份一次（1–8760）
retention:
  maxCount: 0             # 最大备份数，0=不限
  maxTotalSizeMB: 0       # 最大总体积(MB)，0=不限
```

- **备份内容**：默认备份整个 `Servers/<id>/`，减去上面的排除项。保留世界（`Level/`）、玩家
  （`Players/`）、管理列表（`Server/`）、服务器配置（`Config.txt`）、OpenMod 角色/用户/各插件配置与数据。
- **空服状态机**：服务器启动/插件 reload 时若当前无人在线，会先等待并完成下一次常规备份，再进入长周期；
  `idleBackup.enabled: false` 可恢复旧版“无论在线人数都每 N 次保存备份”的行为。`backup.enabled: false` 或
  `everyNSaves <= 0` 仍会禁止自动归档，但不停止 cron 自动保存。
- **WebUI 可编**：以上每一项在装了 WebPanel 时都能在「Auto Save」面板里改；面板还能列出/删除备份、立即备份。

## well404 OpenMod 插件家族

| 插件 | 说明 |
| --- | --- |
| [well404.Economy](https://www.nuget.org/packages/well404.Economy/) | 货币经济核心,全局 `IEconomyProvider` 供币 |
| [well404.Shop](https://www.nuget.org/packages/well404.Shop/) | 物品商店,买卖物品 / 组合包,依赖 Economy |
| [well404.Essentials](https://www.nuget.org/packages/well404.Essentials/) | 面向玩家的 home/tp/warp/gift/sleep/back/party 实用合集 |
| [well404.AdminTools](https://www.nuget.org/packages/well404.AdminTools/) | 无敌、踢出、临时封禁/解封 |
| [well404.Vault](https://www.nuget.org/packages/well404.Vault/) | 玩家私人仓库,完整保真存取背包物品 |
| [well404.AutoSave](https://www.nuget.org/packages/well404.AutoSave/) | 定时保存 + 高压缩备份存档(本插件) |
| [well404.WebPanel](https://www.nuget.org/packages/well404.WebPanel/) | 通用 Web 管理面板,供各插件挂载可视化管理模块 |

完整文档见 [GitHub 仓库](https://github.com/Well2333/unturned-mods) 的 [`docs/`](https://github.com/Well2333/unturned-mods/tree/main/docs)。

## 许可

CC BY-NC-SA 4.0 © well404
