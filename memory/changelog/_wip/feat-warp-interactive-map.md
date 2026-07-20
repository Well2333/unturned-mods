---
date: 2026-07-19
branch: feat/team-vault
merge_commit: pending
type: feat
scope: well404.Essentials, well404.WebPanel, UnturnedMods.Shared
guideline_changed: true
---

# 玩家与管理员互动传送地图

## 做了什么

- Essentials 为传送点增加明确的地图归属；游戏内 `/warp set` 和管理员网页新建时会记录当前地图，玩家命令与网页只接受当前地图的点。
- 玩家传送点页面提供 GPS / Chart / 列表三视图、标签联动、滚轮与按钮缩放、拖动平移和复位；任一底图不可用时仍可打开查看原因，另一底图与列表保持可用。
- 地图视图临时暂停 WebPanel 的 5 秒自动刷新，离开地图后恢复；画布提供服务器持久化的紧凑/宽大偏好；紧凑按浏览器内容视口高度自适应，宽大按容器宽度展开。
- 回家与返回死亡点只在列表显示紧凑命令条，地图中仅显示 `🏠` / `💀`；玩家点击传送点图钉直接弹出二次确认，确认后传送。
- 图钉位于独立叠加层并保持固定尺寸，缩放只改变位置；标签定义分预设与自定义两类独立保存在配置中，地图和列表使用第一个有 emoji 的标签。
- 管理员传送点编辑器提供标签下拉复选框与自定义 ID，标签库可编辑稳定 ID、英文名、中文名、emoji 和预设/自定义分类。
- Essentials 管理员面板显示同一张当前地图，叠加坐标有效的当前地图传送点；点击标记打开原编辑弹窗，列表继续管理所有地图的数据。
- 地图使用原生 `Map.png`（GPS）与 `Chart.png` 双底图和同一套游戏坐标换算；兼容主 Cartography Volume，普通地图按有效 Level 尺寸换算。
- Shared/WebPanel 增加通用、不认识具体业务的只读图片提供者与自建 UI `assetUrl` 能力；玩家资源走玩家会话，管理员资源走管理 token，并限制格式、16 MiB、私有缓存、ETag 与 `nosniff`。
- 玩家 GPS 与 Chart 分别遵循原生 Satellite/enablesMap 和 Chart/enablesChart 查看规则，可配置为始终显示；管理员地图不依赖玩家背包资格。

## 为什么

- 传送点列表难以表达地点之间的空间关系；复用地图自带二维 Chart 能让玩家直观看到地点，同时避免引入三维渲染、外部地图服务或公开静态文件目录。
- 传送点坐标必须绑定地图，否则服务器换图后同一数字坐标没有可靠含义，可能把玩家传到错误区域。

## 影响的文件 / 范围

- `src/plugins/well404.Essentials/`：传送点模型、命令、地图服务、配置/YAML、玩家与管理员自建 UI、翻译和文档。
- `src/Shared/UnturnedMods.Shared/WebPanel/` 与 `src/plugins/well404.WebPanel/`：通用图片扩展接口、受鉴权 HTTP 路由和能力 URL。
- `tests/well404.Essentials.Tests/`、`tests/well404.WebPanel.Tests/`：投影、地图身份和前端能力契约。

## 注意事项 / 后续

- 旧传送点没有 `map` 时不会自动猜测；它们仍在管理员列表中，但玩家不可见且不可传送，需管理员按实际用途填写。
- 本分支仅提交并推送供审查，不合并、不发版；远程部署与生命周期记录只保存在 .local-memory/changelog.md。
- Essentials 与 WebPanel Release 构建成功；两份地图 JavaScript 通过语法检查；全解决方案 211/211 项测试通过。
- 本地服三包冷加载成功，PEI 地图状态、Cartography Volume 投影、PNG、鉴权、未知 id、ETag 304 与安全响应头通过真实 API 验证。
- 真实 Chrome 完成管理员地图的底图、标记、缩放、标记编辑、地图/列表、标签筛选与窄屏验收；0 控制台异常、0 失败请求。临时传送点和测试配置已清理，Chrome 与本地服已关闭。
- 玩家原生 Chart 资格和实体落点需要在线玩家，已写入 `.localserver/PENDING-INGAME-TESTS.md`，不影响本地交付但部署前需实机验收。
- 实机验收发现地图不可用状态曾把切换按钮禁用，导致已有原因提示无法到达；已改为按钮始终可切换并显示原因。Essentials Release 测试 56/56 通过，真实 Chrome 覆盖玩家可用/锁定地图、标记详情、缩放、地图/列表往返、桌面紧凑宽度、窄屏堆叠与管理员缺图回退。
- GPS / Chart 双底图与第二轮实机 UI 重写通过 Release 单测 58/58、真实 Chrome 交互 8/8；覆盖 1042px 大视口、固定 26×32px 图钉、切换和整页刷新保持缩放。
- 最终收敛为“紧凑/宽大”两档，旧 `auto` 与未知值自动迁移到紧凑；Release 构建 0 warning / 0 error，全解决方案 222/222、Essentials 66/66、WebPanel 19/19 通过，全部插件 JavaScript 语法检查通过，1500×1000 与 480×900 两档真实 Chrome 回归均为 PASS。


## 规范同步

- 更新 `DESIGN.md`：明确重复工具操作使用内容宽度命令条；地图图钉必须是固定尺寸独立叠加层；空间画布操作期间暂停轮询；标记点击直接进入确认；地图框应自适应视口并可保存尺寸偏好。
