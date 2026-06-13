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
            ["Items"] = "单品",
            ["Bundles"] = "礼包",
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

            // ---- admin module chrome ----
            // Plain items (referenced by game item id; name auto-resolved)
            ["Plain items"] = "普通商品",
            ["Item ID"] = "物品ID",
            ["Buy price"] = "买价",
            ["0 = not buyable"] = "0=不可买",
            ["Sell price"] = "卖价",
            ["0 = not sellable"] = "0=不可卖",
            ["Click to edit, Add to create. A plain item is bought and sold by its game item id; its display name comes from the game. Look up ids with the search below."]
                = "点击编辑，「新增」添加。普通商品按游戏物品 ID 买卖，显示名由游戏自动解析。物品 ID 可用下方检索。",
            // Bundles (a named pack, referenced by their own id)
            ["Bundle ID"] = "礼包ID",
            ["Unique id used by /buy /sell"] = "/buy /sell 用的唯一ID",
            ["Display name"] = "显示名",
            ["Contents"] = "内容物",
            ["itemId×amount, comma-separated, e.g. 15x2, 81x1"] = "物品ID×数量，逗号分隔，如 15x2, 81x1",
            ["Click a bundle to edit, Add to create. A bundle is a named pack of items; contents format itemId×amount, comma-separated (id only = amount 1), e.g. 15x2, 81x1."]
                = "点击编辑，「新增」添加。礼包是一组物品的命名组合；内容格式 物品ID×数量，逗号分隔（只写ID则数量为1），如 15x2, 81x1。",
            // Search + quick-add
            ["Search game items"] = "检索游戏物品",
            ["Item name or ID"] = "物品名或ID",
            ["Type a keyword or numeric ID…"] = "输入关键词或数字ID…",
            ["Search any game item by name or ID, then click + to add it to the shop as a plain item (set its prices afterwards)."]
                = "按名称或 ID 检索任意游戏物品，点「＋」即把它作为普通商品加入商店（之后再填买卖价）。",
            ["Add to shop"] = "加入商店",
            ["Added to the shop — set its buy/sell price."] = "已加入商店——请设置它的买/卖价。",
            ["Already in the shop."] = "已在商店中。",
            // admin result messages (fixed, arg-free so they localize server-side)
            ["Saved."] = "已保存。",
            ["Deleted."] = "已删除。",
            ["Not found."] = "未找到。",
            ["Enter a valid item ID."] = "请输入有效的物品 ID。",
            ["Enter the bundle ID, name and contents."] = "请填写礼包 ID、名称与内容。",
            ["Contents cannot be empty, e.g. 15x2, 81x1."] = "内容不能为空，如 15x2, 81x1。",
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
            ["Buy a plain item by its item id, or a bundle by its id, with your money."] = "用你的金钱按物品 ID 购买单品，或按礼包 ID 购买礼包。",
            ["Sell a plain item by its item id, or a bundle by its id, back to the shop for money."] = "把单品（按物品 ID）或礼包（按礼包 ID）卖回商店换取金钱。",

            // ---- result table column headers / messages (localized server-side) ----
            ["Name"] = "名称",
            ["Type an item name or ID to search."] = "输入物品名称或 ID 进行检索。",
            ["No matching items."] = "没有匹配的物品。",
        };
    }
}
