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

# 每个条目用 id 在 /buy /sell 中引用。
#   type: item   -> 单个 Unturned 物品（itemId、每单位数量 amount）
#   type: bundle -> 自定义组合包（contents，可整包出售：售出时回收全部内容物）
# buyPrice 为 0 = 不可购买；sellPrice 为 0 = 不可出售。
items:
  - id: "medkit"
    name: "Medkit"
    type: "item"
    itemId: 15          # Unturned 物品资源 ID
    amount: 1           # 每购买一单位给的数量
    buyPrice: 100
    sellPrice: 40

  - id: "starter"
    name: "Starter Pack"
    type: "bundle"
    buyPrice: 500
    sellPrice: 0        # 0 = 该组合包不可出售
    contents:
      - itemId: 15
        amount: 2
      - itemId: 81
        amount: 1
```

### 单物品 vs 组合包

- **单物品**（`type: item`）：买入给 `amount` 个 `itemId`；卖出回收等量。
- **组合包**（`type: bundle`）：买入按 `contents` 一次性发放全部物品；若 `sellPrice > 0`
  则可整包出售（需库存里有**全部**内容物，按比例回收）。

> `/buy <id> <数量>` 的「数量」指购买**单位**数。例如 `medkit` 的 `amount: 2` 时，
> `/buy medkit 3` 给 6 个物品、扣 3×单价。

## 命令

| 命令 | 说明 | 权限 |
| --- | --- | --- |
| `/shop` | 列出商品与买入/卖出价。别名 `/market`。 | `well404.Shop:commands.shop` |
| `/buy <id> [数量]` | 购买物品/组合包（仅玩家）。 | `well404.Shop:commands.buy` |
| `/sell <id> [数量]` | 出售物品/组合包（仅玩家）。 | `well404.Shop:commands.sell` |

- 购买时若余额不足会被经济系统拦下并提示；发放物品时背包满则物品掉落在地。
- 出售前会校验库存是否足够，不足则提示且不扣物。

示例：

```
/shop
/buy medkit
/buy medkit 3
/sell medkit 2
/buy starter
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

安装 [`well404.WebPanel`](well404.WebPanel.md) 后,Shop 自动注册「商店」模块(图标 🛒):

- **商品**(集合,网格布局):增 / 删 / 改商品目录条目(id、名称、物品×数量、买价、卖价),单物品与组合包均可。
- **搜索**:按名称或 ID 搜索 Unturned 游戏物品(最多 100 条),便于快速填入 `itemId`。
- **折扣**(设置):开关折扣总开关并配置权限分级,格式 `权限=乘数, 权限=乘数`(如 `well404.shop.vip=0.9`)。

未安装面板时,以上均可通过 `config.yaml` + 命令完成,功能不受影响。

## 文案（`translations.yaml`）

所有提示文本在 `translations.yaml`，可自行汉化/改写。
