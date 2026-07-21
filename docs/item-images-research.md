# WebPanel 背包二维物品图标研究（已存档）

> 状态：2026-07-19 存档，功能暂缓，不进入当前开发计划。本文所说的图片专指玩家在背包、
> 仓库和商店里看到的二维物品图标，不是让网页或专服展示可旋转的三维模型。

## 官方源码结论

本次以 Smartly Dressed Games 公布的
[`U3-SDK`](https://github.com/SmartlyDressedGames/U3-SDK) 为权威依据，核对快照：
`21dd044d31f15b92a79fc351432714c95305603e`。

仓库本地 `.localserver` 当前日志记录的游戏版本是 3.26.3.3（Unity 2022.3.62f3）；其随服
`Assembly-CSharp.xml` 也包含同一 `ItemAsset.shouldAlwaysLoadItemPrefab` 字段及 dedicated server
图标注释，因此关键开关并非只存在于较新的 U3-SDK `main`。本地启动脚本当前明确使用
`-batchmode -nographics`，后续原型必须把 Null graphics device 作为实际基线，而不是理论边角情况。

- Unturned 普通物品图标通常不是物品目录旁的一张现成 PNG。游戏客户端打开背包时，
  `SleekItem` 会让 `SleekItemIcon` 调用 `ItemTool.getIcon`，把物品模型从固定角度拍成一张
  `Texture2D`。最终显示给玩家的就是二维图片；三维模型只是生成这张二维图的原料。
- `ItemTool` 默认尺寸为 `ItemAsset.size_x * 50` × `size_y * 50`；调用方也可以指定统一尺寸。
  图标需要导出 PNG 时必须请求 `readableOnCPU: true`，再在回调中调用 `EncodeToPNG()`。
- 背包中的横竖摆放主要由界面旋转图标实现；数量、耐久、快捷键和稀有度边框等属于界面叠加信息，
  不应烧进共用图片。
- 官方图形客户端还提供隐藏的工坊图标工具：按 F1 后可以批量执行“All Item Icons”，把已加载
  物品导出到 `Extras/Icons`。批量图使用每格 512 像素的高分辨率，后续可统一缩小。
- `ItemAsset` 新增了专门面向插件的命令行开关 `-AlwaysLoadItemPrefab`，官方注释明确说明它用于
  dedicated server 捕获物品图标。没有该参数时，专服通常只加载枪械和近战物品的 Item Prefab，
  其他物品无法完整渲染。
- 该开关在 `ItemAsset` 静态初始化/资产加载阶段从进程命令行读取。OpenMod 插件加载后再改配置
  已经太晚，因此必须作为 U3DS 启动参数并完整重启才能覆盖所有物品。
- 捕获过程跨帧执行并触碰 Unity 对象，排队和回调都必须留在 Unity 主线程；PNG 字节落盘、HTTP
  响应和缓存查找则应移到后台，不能阻塞游戏主线程。

官方对应文件：

- [`ItemAsset.cs`](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Bundles/ItemAsset.cs)
- [`ItemTool.cs`](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Tools/ItemTool.cs)
- [`IconUtils.cs`](https://github.com/SmartlyDressedGames/U3-SDK/blob/main/Assets/Runtime/Assembly-CSharp/Unturned/Utils/IconUtils.cs)
- [官方 Items 入门文档](https://docs.smartlydressedgames.com/en/stable/items/introduction.html)

## 为什么不建议生产专服实时生成

`ItemTool.captureIcon` 依赖真实图形设备、Camera 和 RenderTexture。常见 U3DS 启动参数
`-nographics` 会使用 Null graphics device；即使物品 Prefab 因 `-AlwaysLoadItemPrefab` 已加载，
也不能假定 Null 设备能产出有效像素。最终实现必须先检查图形设备并验证输出不是全透明图，不能把
“回调返回了 Texture2D”误判为成功。

即使把这条路调通，生产专服也会承担额外模型加载、内存、图形环境、主线程排队和失败重试风险。
工坊更新还会造成缓存失效或图片错配。为了一个网页辅助图标去改变专服启动模式，收益不值得风险。

如果未来重新立项，图片功能至少要保留以下降级路径：

1. 优先读取服务端持久缓存或管理员提供的覆盖图；这条路径在 `-nographics` 下也可用。
2. 仅在图形设备可用且进程带 `-AlwaysLoadItemPrefab` 时按需生成缺失图标。
3. 无缓存且无法渲染时返回明确的占位状态，前端保留现有文字卡片，不显示破图。

不建议为了图片默认移除 `-nographics`。这会增加 GPU/显存占用，也可能重新暴露地图/工坊 Shader
报错。是否以有图形模式运行应由服务器管理员明确选择。

## 存档时的推荐结论

首选方案是用与服务器相同资源组合的普通图形客户端，在维护流程中离线批量导出背包二维图标，
再让 WebPanel 只负责读取、缩放和缓存。生产专服继续使用 `-nographics`，不承担实时作图任务。

图片必须按资产 GUID 和图片格式版本建立清单，不能只按数字物品 ID 保存。工坊包可能重复使用
同一个数字 ID，服务器最终采用哪一个资产由实际加载结果决定。

## 如果未来重启项目

### 1. 通用二进制资源注册

在 Shared 新增独立的 `IWebResourceRegistry`，由 WebPanel 实现。插件按 provider id 注册只读资源
处理器，宿主只负责：

- 管理端 token / 玩家 session 鉴权；
- 路由、Content-Type、ETag、Cache-Control 和错误隔离；
- 为插件自建 UI 注入 `panel.mediaUrl(provider, key)`。

宿主不得判断 `shop`、`vault` 或物品 ID。Shop/Vault 只把 `itemId` / 图片 key 放进自身卡片数据，
由各自 UI 调用通用 helper。这样以后礼包、管理员物品检索或其他插件也能复用图片通道。

### 2. 共用物品图标缓存

物品图片实现放在 Shared 的 Items 范围，按 Unturned 实际 `Assets.find` 选中的权威资产渲染，避免
重复数字 ID 串图。建议磁盘 key 至少包含资产 GUID 和图标格式版本；不能只用数字 ID，因为工坊
加载顺序或资源更新后同 ID 可能指向不同资产。

- 内存缓存：已编码的 PNG/WebP 字节和失败状态，限制总大小并使用 LRU。
- 磁盘缓存：插件数据目录的 `item-icons/`，原子写入；启动时不批量生成全部物品。
- 请求合并：同一资产同时只允许一个生成任务，其他 HTTP 请求等待同一个结果。
- 限流：每帧/每秒只处理少量新图标，页面首开不能一次占满 Unity 主线程。
- 安全：key 只能解析为已加载资产；禁止把 URL 片段直接拼成本地路径。

第一版只生成“物品类型的默认图”：quality 100 + `asset.getState()`，Shop、Vault 各状态变体共用。
枪械附件、弹药量、皮肤等逐实例图标会显著扩大缓存和攻击面，确认基础方案稳定后再评估。

### 3. UI 约束

- 卡片图片使用固定的紧凑媒体区，`object-fit: contain`，不得破坏现有六列密度。
- 使用 `loading="lazy"`、明确宽高和 skeleton/占位，避免图片加载造成卡片跳动。
- 图片是辅助辨识，名称、ID、数量、耐久和按钮仍必须完整可见；失败时自然回退纯文字卡。
- 弹窗可显示更大的同源图片，但不能为弹窗再次生成一份不同尺寸的服务端图片，浏览器负责缩放。
- 5 秒自动刷新不得改变图片 URL，否则会重复下载；成功图标使用 ETag 和长期缓存。

## 将来的推进顺序

1. 先实现通用 `IWebResourceRegistry`、鉴权路由和 `panel.mediaUrl`，用一张内嵌测试图片验证管理端、
   玩家端、Quick Tunnel/Named Tunnel 下路径都正确。
2. 用普通图形客户端加载目标服务器的地图与工坊资源，执行官方批量图标导出，生成 GUID 清单。
3. 接入离线图片、尺寸处理与缓存读取，在 `-nographics` 专服环境验证 Shop/Vault 的图片显示、
   文字降级和 HTTP 缓存。
4. 再把 `imageKey` 接入 Shop 管理/玩家目录和 Vault 管理/玩家仓库。

生产专服实时生成只能作为管理员明确选择的实验方案，不作为默认路线；在独立测试实例验证前，
不应要求生产实例修改启动参数。
