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
            ["Buy price: {0}{1} each"] = "买价：{0}{1} / 个",
            ["Buy price: {0}{1} each (was {0}{2})"] = "买价：{0}{1} / 个（原价 {0}{2}）",
            ["Sell price: {0}{1} each"] = "卖价：{0}{1} / 个",
            ["Quantity to buy"] = "购买数量",
            ["Quantity to sell"] = "出售数量",
            ["Bundle"] = "礼包",
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
            ["Shop / Items"] = "商店 / 商品",
            ["Items"] = "商品",
            ["Shop ID"] = "商品ID",
            ["Unique ID used by /buy /sell"] = "/buy /sell 用的唯一ID",
            ["Display name"] = "显示名",
            ["Contents"] = "物品",
            ["itemId×amount, comma-separated. One = item, several = bundle. e.g. 15x2 or 15x2, 81x1"]
                = "物品ID×数量，逗号分隔。单个=物品，多个=礼包。如 15x2 或 15x2, 81x1",
            ["Buy price"] = "买价",
            ["0 = not buyable"] = "0=不可买",
            ["Sell price"] = "卖价",
            ["0 = not sellable"] = "0=不可卖",
            ["Click an item to edit, Add to create. «Items»: one entry = plain item, several (comma-separated) = bundle; format itemId×amount (id only = amount 1), e.g. 15x2 or 15x2, 81x1. Look up item IDs with the search below."]
                = "点商品编辑，「新增」添加。「物品」填一条=普通物品，多条（逗号分隔）=礼包；格式 物品ID×数量（只写ID则数量1），如 15x2 或 15x2, 81x1。物品ID 可用下方检索。",
            ["Search game items"] = "检索游戏物品",
            ["Item name or ID"] = "物品名或ID",
            ["Type a keyword or numeric ID…"] = "输入关键词或数字ID…",
            ["Fuzzy-search all game items by name or ID; take the item ID into the form above."]
                = "在全部游戏物品中按名称或 ID 模糊检索，拿到「物品ID」填到上面的表单。",
            ["VIP discount"] = "VIP 折扣",
            ["Discount master switch"] = "折扣总开关",
            ["Tiers"] = "档位",
            ["permission=multiplier, comma-separated: e.g. well404.shop.vip=0.9, well404.shop.mvp=0.8"]
                = "权限=倍率，逗号分隔：如 well404.shop.vip=0.9, well404.shop.mvp=0.8",
            ["Discounts apply to buy prices; a player gets the lowest (best) multiplier among their granted permissions. Tiers format: permission=multiplier (0<m≤1, comma-separated); empty clears all tiers."]
                = "折扣作用于买价；玩家取其拥有权限中最低(最优)的倍率。「档位」格式 权限=倍率（0<倍率≤1，逗号分隔）；清空即取消所有档位。",

            // ---- player command help (intro page) ----
            ["shop.group"] = "商店",
            ["shop.cmd.buy"] = "从商店购买物品 / 礼包",
            ["shop.cmd.sell"] = "把物品 / 礼包卖给商店",
            ["shop.cmd.shop"] = "查看商店在售商品",
        };
    }
}
