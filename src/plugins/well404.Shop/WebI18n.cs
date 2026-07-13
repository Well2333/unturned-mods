using System.Collections.Generic;

namespace well404.Shop
{
    /// <summary>Chinese translations for Shop's web strings (English source strings are the keys).</summary>
    internal static class WebI18n
    {
        public const string Zh = "zh";

        public static readonly IReadOnlyDictionary<string, string> ZhTable = new Dictionary<string, string>
        {
            // ---- player shop menu ----
            ["Shop"] = "商店",
            ["Balance: {0}{1}"] = "余额：{0}{1}",
            ["Buy"] = "购买",
            ["Sell"] = "出售",
            ["Quantity to buy"] = "购买数量",
            ["Quantity to sell"] = "出售数量",
            ["Items"] = "商品",
            ["All"] = "全部",
            ["In inventory: {0}"] = "当前库存：{0}",
            ["Choose purchase quantity"] = "选择购买数量",
            ["Choose sale quantity"] = "选择出售数量",
            ["Sell all in this group"] = "售卖本组全部",
            ["Sell every sellable item in this group? This cannot be undone."]
                = "确认出售本组内背包中的全部可售商品？此操作无法撤销。",
            ["This group has no items eligible for quick sell."] = "本分组没有可批量出售的商品。",
            ["Quick sell"] = "一键售卖",
            ["Sell all"] = "全部售卖",
            ["Quick actions"] = "快捷操作",
            ["You must be online to buy or sell."] = "你需要在线才能购买或出售物品。",
            ["Item not found."] = "找不到该商品。",
            ["Enter a valid quantity."] = "请输入有效的数量。",
            ["You must be online to trade."] = "你需要在线才能交易。",
            ["Unknown action."] = "未知操作。",
            ["{0} is not buyable."] = "{0} 不可购买。",
            ["Insufficient balance."] = "余额不足。",
            ["Bought {0}× {1} for {2}."] = "已购买 {0}× {1}，花费 {2}。",
            ["{0} is not sellable."] = "{0} 不可出售。",
            ["You don't have enough {0} in your inventory."] = "你的背包里没有足够的 {0}。",
            ["Sold {0}× {1} for {2}."] = "已出售 {0}× {1}，获得 {2}。",
            ["The shop has no items eligible for quick sell."] = "商店中没有可一键售卖的商品。",
            ["No sellable items were found in your inventory."] = "你的背包中没有可出售的商品。",
            ["Sold {0} item(s) for {1}."] = "已一键出售 {0} 件物品，获得 {1}。",

            // ---- admin module chrome ----
            ["Shop groups"] = "商店分组",
            ["Group ID"] = "分组 ID",
            ["Group name"] = "分组名称",
            ["Player shop catalog"] = "玩家商店目录",
            ["Note"] = "备注",
            ["Create the second-level tabs shown to players. The default group always exists."]
                = "管理玩家商店的二级标签栏；default 分组始终存在。",
            ["This preview uses the same groups and order as the player shop. Drag cards inside a group to reorder them."]
                = "此处的分组和顺序与玩家商店完全同步；在分组内拖动商品卡片即可排序。",
            ["Enter a valid group ID and name."] = "请输入有效的分组 ID 和名称。",
            ["The default group cannot be deleted."] = "default 分组不能删除。",
            ["Order saved."] = "排序已保存。",
            ["The group order is stale; refresh and try again."] = "分组列表已变化，请刷新后重试。",
            ["The selected group does not exist."] = "选择的分组不存在。",
            ["The catalog order is stale; refresh and try again."] = "商品列表已变化，请刷新后重试。",
            // Plain items (referenced by game item id; name auto-resolved)
            ["Plain items"] = "普通商品",
            ["Item ID"] = "物品ID",
            ["Buy price"] = "买价",
            ["0 = not buyable"] = "0=不可买",
            ["Sell price"] = "卖价",
            ["0 = not sellable"] = "0=不可卖",
            ["Click to edit, Add to create. A plain item is bought and sold by its game item id; its display name comes from the game. Look up ids with the search below."]
                = "点击编辑，「新增」添加。普通商品按游戏物品 ID 买卖，显示名由游戏自动解析。物品 ID 可用下方检索。",
            // Search + quick-add
            ["Search game items"] = "检索游戏物品",
            ["Item name or ID"] = "物品名或ID",
            ["Type a keyword or numeric ID…"] = "输入关键词或数字ID…",
            ["Search any game item by name or ID, then click + to add it to the shop as a plain item (set its prices afterwards)."]
                = "按名称或 ID 检索任意游戏物品，点「＋」即把它作为普通商品加入商店（之后再填买卖价）。",
            ["Search any game item by name or ID, then click + to add it with prices, group and note."]
                = "按名称或 ID 检索物品，点击＋并填写买价、卖价、分组和备注后加入商店。",
            ["Add to shop"] = "加入商店",
            ["Added to the shop."] = "已加入商店。",
            ["Already in the shop."] = "已在商店中。",
            // admin result messages (fixed, arg-free so they localize server-side)
            ["Saved."] = "已保存。",
            ["Deleted."] = "已删除。",
            ["Not found."] = "未找到。",
            ["Enter a valid item ID."] = "请输入有效的物品 ID。",
            ["Saved discount settings."] = "已保存折扣设置。",
            // Discounts
            ["VIP discount"] = "VIP 折扣",
            ["Discount master switch"] = "折扣总开关",
            ["Tiers"] = "档位",
            ["permission=multiplier, comma-separated: e.g. well404.shop.vip=0.9, well404.shop.mvp=0.8"]
                = "权限=倍率，逗号分隔：如 well404.shop.vip=0.9, well404.shop.mvp=0.8",
            ["Discounts apply to buy prices; a player gets the lowest (best) multiplier among their granted permissions. Tiers format: permission=multiplier (0<m≤1, comma-separated); empty clears all tiers."]
                = "折扣作用于买价；玩家取其拥有权限中最低(最优)的倍率。「档位」格式 权限=倍率（0<倍率≤1，逗号分隔）；清空即取消所有档位。",

            // ---- player command help (intro page); keys are the English descriptions ----
            ["Browse the server shop and see item prices."] = "浏览服务器商店，查看各商品的价格。",
            ["Buy an item by its game item id with your money."] = "用你的金钱按游戏物品 ID 购买商品。",
            ["Sell an item by its game item id back to the shop for money."] = "按游戏物品 ID 把商品卖回商店换取金钱。",

            // ---- result table column headers / messages (localized server-side) ----
            ["Name"] = "名称",
            ["Type an item name or ID to search."] = "输入物品名称或 ID 进行检索。",
            ["No matching items."] = "没有匹配的物品。",
        };
    }
}
