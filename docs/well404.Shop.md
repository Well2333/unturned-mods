# well404.Shop

配置驱动的商店：买卖单个物品或**自定义组合包（bundle）**，并支持按权限组的购买折扣
（如 VIP 9 折，默认关闭）。货币来自 `IEconomyProvider`。

## 依赖

**硬依赖 [`well404.Economy`](well404.Economy.md)**:商店通过它提供的 `IEconomyProvider`
结算买卖。该依赖已写入 Shop 的 NuGet 包,因此 `openmod install well404.Shop` 会**自动
一并安装** well404.Economy,无需手动先装。

```bash
openmod install well404.Shop   # 自动带上 well404.Economy
openmod reload
```

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

# 商品分两类，互不混淆：
#  items（普通商品）  -> 单个游戏物品，用它的「游戏物品 ID」买卖；显示名由游戏自动解析，
#                       所以只需填 itemId + 买价 + 卖价。一次购买给 1 个该物品。
#  bundles（礼包）    -> 自定义命名组合包，用它自己的 id 买卖；可整包出售（回收全部内容物）。
# buyPrice 为 0 = 不可购买；sellPrice 为 0 = 不可出售。
items:
  - itemId: 15          # 游戏物品 ID；/buy 15 /sell 15 即用它
    buyPrice: 100
    sellPrice: 40

bundles:
  - id: "starter"       # 礼包自己的 id（取名时避免纯数字，以免与物品 ID 混淆）
    name: "Starter Pack"
    buyPrice: 500
    sellPrice: 0        # 0 = 该礼包不可出售
    contents:
      - itemId: 15
        amount: 2
      - itemId: 81
        amount: 1
```

### 普通商品 vs 礼包

- **普通商品**（`items:`）：只需 `itemId` + 买/卖价；**用游戏物品 ID 引用**（`/buy 15`），
  显示名自动取自游戏。一次购买给 1 个，`/buy 15 3` 即买 3 个。
- **礼包**（`bundles:`）：有自己的 `id` 与显示 `name`；买入按 `contents` 一次性发放全部物品；
  若 `sellPrice > 0` 则可整包出售（需库存里有**全部**内容物，按比例回收）。

> 一条命令既能买普通商品也能买礼包：数字参数先按物品 ID 找普通商品，找不到再按 id 找礼包。
> 因此**礼包 id 不要起成纯数字**，否则可能与某个物品 ID 撞车。

## 命令

| 命令 | 说明 | 权限 |
| --- | --- | --- |
| `/shop` | 列出商品与买入/卖出价。别名 `/market`。 | `well404.Shop:commands.shop` |
| `/buy <id> [数量]` | 购买：普通商品填**物品 ID**，礼包填**礼包 id**；省略数量时默认为 1（仅玩家）。 | `well404.Shop:commands.buy` |
| `/sell <id> [数量]` | 出售：普通商品填**物品 ID**，礼包填**礼包 id**；省略数量时默认为 1（仅玩家）。 | `well404.Shop:commands.sell` |

- 购买时若余额不足会被经济系统拦下并提示；发放物品时背包满则物品掉落在地。
- 出售前会校验库存是否足够，不足则提示且不扣物。

示例：

```
/shop
/buy 15          # 普通商品：按物品 ID
/buy 15 3
/sell 15 2
/buy starter     # 礼包：按礼包 id
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

安装 [`well404.WebPanel`](well404.WebPanel.md) 后,Shop 自动注册「商店」模块(图标 🛒),
**普通商品与礼包分成两块独立管理**:

- **普通商品**(集合):增 / 删 / 改,字段仅 **物品 ID + 买价 + 卖价**(名称由游戏自动解析);每个条目
  瓦片上直接显示 `#物品ID` 与「买价 / 卖价」标签,一眼可见价格。
- **礼包**(集合):增 / 删 / 改,字段 **礼包 id + 显示名 + 内容(物品ID×数量,逗号分隔) + 买价 + 卖价**;
  瓦片显示内容胶囊与买/卖价标签。
- **检索游戏物品**:按名称或 ID 搜索(最多 100 条);输入**纯数字**时优先列出**该精确 ID**;每行带一个
  **「＋ 加入商店」**按钮,点击会**弹出买价 / 卖价输入框**,填好即把该物品作为**普通商品**建入(强制当场设价)。
- **折扣**(设置):开关折扣总开关并配置权限分级,格式 `权限=乘数, 权限=乘数`(如 `well404.shop.vip=0.9`)。

未安装面板时,以上均可通过 `config.yaml` + 命令完成,功能不受影响。

## 玩家网页商店（可选）

装了 `well404.WebPanel` 后,Shop 还会注册一个**面向玩家**的商店菜单(玩家面 `/p`,图标 🛒)。
玩家在游戏内输入 `/menu`(或 `/menu shop`)收到链接,在浏览器里:

- 顶部以一枚徽章显示自己的当前余额;
- **单品**与**礼包分区展示**(各带数量计数),采用紧凑的**列表**布局、响应式单/双列,适配 Steam
  内嵌浏览器的窄屏、尽量减少留白;
- **单品行** = `#物品ID │ 物品名 │ 购买 $买价 │ 出售 $卖价`(价格即按钮,买入为绿色;点击即买/卖);
- **礼包行**(整行更宽)= `礼包名 │ 内容胶囊标签(物品名 ×数量) │ 购买 │ 出售`;
- 点「购买」/「出售」并输入数量即可成交——沿用与 `/buy`、`/sell` 完全一致的折扣、扣费、
  失败退款与背包校验逻辑(玩家须在线)。

> 该页面完全由通用的玩家菜单模型(分区 / 列表布局 / 徽章 / 按钮)渲染;WebPanel 宿主不含任何
> 商店专属逻辑,样式全由 Shop 注册的数据驱动(见 [架构说明](../memory/guidelines/architecture.md))。

## 文案（`translations.yaml`）

所有提示文本在 `translations.yaml`，可自行汉化/改写。
