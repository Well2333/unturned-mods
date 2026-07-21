# well404.Shop

玩家商店与管理员目录采用 Shop 自带的 Web UI，并通过 WebPanel 的 Shadow DOM 扩展挂载。
这让分组、六列紧凑商品网格、库存状态、快捷购买/售卖、备注与排序不再依赖宿主业务特判。

配置驱动的物品商店，并支持分组目录、备注、排序和按权限组的购买折扣
（如 VIP 9 折，默认关闭）。货币来自 `IEconomyProvider`。

Shop 会读取每个原版或创意工坊物品资源目录旁的 `English.dat` 与简体中文
`sChinese.dat`（并兼容常见文件名）。服务器装有对应物品汉化时，管理目录、玩家商店和
物品检索与商品卡会读取当前面板语言：中文界面以中文名为主标题，英文名换行并以灰色小字
作为参考；英文界面只显示英文名。检索同时匹配中英文，缺少翻译时自动回退英文。
若创意工坊错误地给多个不同资源配置了相同数字 ID，则以 Unturned 自身
`Assets.find(EAssetType.ITEM, id)` 实际选中的资源为准，避免商品名称和元数据串到另一个同 ID 物品。

## 依赖

**硬依赖 [`well404.Economy`](well404.Economy.md)**:商店通过它提供的 `IEconomyProvider`
结算买卖。该依赖已写入 Shop 的 NuGet 包,因此 `openmod install well404.Shop` 会**自动
一并安装** well404.Economy,无需手动先装。

```bash
openmod install well404.Shop   # 自动带上 well404.Economy
```

安装或升级 DLL 后必须完整重启服务器；`openmod reload` 只适合未替换二进制时的配置重载。

> 同时升级两者并自行发版时,**先发布 Economy 再发布 Shop**:Shop 的依赖下限跟随
> Economy 当前版本,Economy 须先在 NuGet 上可解析。

## 配置（`config.yaml`）

首次加载后生成于 `openmod/plugins/well404.Shop/config.yaml`：

```yaml
discounts:
  # 默认关闭。开启后，玩家被授予的权限里「最优（最低）的乘数」作用于买入价。
  # 例：0.9 = 9 折。
  enabled: false
  tiers: {}
  # 示例：
  # tiers:
  #   well404.shop.vip: 0.9
  #   well404.shop.mvp: 0.8

# items 中每一项对应一个游戏物品，用「游戏物品 ID」买卖；显示名由游戏自动解析。
# buyPrice 为 0 = 不可购买；sellPrice 为 0 = 不可出售。
groups:
  - id: "default"
    name: "default"

items:
  - { itemId: 14,  buyPrice: 10,   sellPrice: 4,   group: "default", note: "", order: 1 } # Bottled Water
  - { itemId: 13,  buyPrice: 15,   sellPrice: 6,   group: "default", note: "", order: 2 } # Canned Beans
  - { itemId: 116, buyPrice: 1000, sellPrice: 400, group: "default", note: "", order: 3 } # PDW
  - { itemId: 6,   buyPrice: 100,  sellPrice: 40,  group: "default", note: "", order: 4 } # Military Magazine
  - { itemId: 43,  buyPrice: 200,  sellPrice: 80,  group: "default", note: "", order: 5 } # Low Caliber Military Ammunition Crate
  - { itemId: 95,  buyPrice: 20,   sellPrice: 8,   group: "default", note: "", order: 6 } # Bandage
  - { itemId: 391, buyPrice: 30,   sellPrice: 12,  group: "default", note: "", order: 7 } # Vitamins
  - { itemId: 68,  buyPrice: 50,   sellPrice: 20,  group: "default", note: "", order: 8 } # Metal Sheet
```

## 命令

| 命令 | 说明 | 权限 |
| --- | --- | --- |
| `/shop` | 列出商品与买入/卖出价。别名 `/market`。 | `well404.Shop:commands.shop` |
| `/buy <id> [数量]` | 按**游戏物品 ID**购买；省略数量时默认为 1（仅玩家）。 | `well404.Shop:commands.buy` |
| `/sell <id> [数量]` | 按**游戏物品 ID**出售；省略数量时默认为 1（仅玩家）。 | `well404.Shop:commands.sell` |

- 购买时若余额不足会被经济系统拦下并提示；发放物品时背包满则物品掉落在地。
- 出售前会校验库存是否足够，不足则提示且不扣物。
- 商品数量按背包中的独立物品实例计算；弹匣、弹药箱等物品内部的弹药量不作为商品件数。

示例：

```
/shop
/buy 14          # 普通商品：按物品 ID
/buy 14 3
/sell 14 2
```

## 等级折扣（按权限组）

1. 在 `config.yaml` 打开 `discounts.enabled: true`。
2. 在 `discounts.tiers` 配置「权限 → 乘数」，例如：

   ```yaml
   discounts:
     enabled: true
     tiers:
       well404.shop.vip: 0.9    # 9 折
       well404.shop.mvp: 0.8    # 8 折
   ```

3. 在 OpenMod 权限里把对应权限授予某角色（如 VIP 角色），例如
   `openmod/permissionRoles.yaml`：

   ```yaml
   - id: vip
     displayName: VIP
     permissions:
       - "well404.shop.vip"
   ```

4. 玩家若同时拥有多个折扣权限，取**最优（乘数最低）**的那个。折扣只作用于**买入**。

> 折扣的权限串是你在 `tiers` 里自定义的（与命令权限无关），可任意命名。

## Web 管理面板（可选）

安装 [`well404.WebPanel`](well404.WebPanel.md) 后,Shop 管理页提供：

- **商店分组**：商品目录标题右侧直接新增分组或编辑当前分组；组名留空时自动使用组 ID。`default` 始终存在且不能删除，删除其他分组时商品自动回到 `default`。
- **玩家面板式管理布局**：管理目录直接复用玩家页的分组标签、六列紧凑商品卡和响应式断点，不再为“分组”和“添加商品”各占一个顶部页面。
- **玩家商店目录**：点击紧凑的编辑按钮可修改商品分组、备注、买价和卖价。
- **拖拽排序**：在当前分组内拖动商品卡片即写回全局 `order`；玩家页面严格使用相同顺序。
- **检索游戏物品**：目录下方提供名称/数字 ID 输入框，结果区明确显示 ID；点“添加”后在弹窗设置 `buyPrice`、`sellPrice`、分组和备注。
- **折扣**：开关折扣并配置 `权限=乘数`。

## 玩家网页商店（可选）

玩家输入 `/menu shop` 后：

- 使用二级 header 标签切换管理员配置的分组，未分组旧商品位于 `default`。
- 商品在常规桌面宽度使用六列紧凑布局，并随窗口依次降为 5/4/3/2/1 列；备注和当前背包可用数量直接显示在卡片上。
- 点击购买或出售会打开交易弹窗；弹窗再次显示商品名、备注和库存，并提供 **1 / 5 / 10 / 全部** 快捷按钮以及自定义数量输入。
- 「全部购买」按余额和折后单价计算最大可买数量；出售输入大于持有数量时按实际持有量成交，而不是报错。`/sell` 命令也采用相同截断规则。
- 每个分组标签右侧有紧凑的「售卖本组全部」按钮，并要求二次确认。批量售卖处理该组所有可售商品。

## 旧配置迁移

旧 `items` 配置继续可读。首次构建 Web 管理模块时插件会重写配置：创建 `groups` 中的 `default`，把缺少或无效 `group` 的商品移入 `default`，将空 `note` 设为空字符串，并生成不重复的 `order`。不需要管理员手工迁移。

## 文案（`translations.yaml`）

所有提示文本在 `translations.yaml`，可自行汉化/改写。
