using System.Collections.Generic;

namespace well404.Vault
{
    /// <summary>Chinese translations for Vault's web strings (English source strings are the keys).</summary>
    internal static class VaultI18n
    {
        public const string Zh = "zh";

        public static readonly IReadOnlyDictionary<string, string> ZhTable = new Dictionary<string, string>
        {
            // ---- player vault menu ----
            ["Vault"] = "仓库",
            ["Backpack"] = "背包",
            ["Store"] = "存入",
            ["Take"] = "取出",
            ["Amount to store"] = "存入数量",
            ["Amount to take"] = "取出数量",
            ["Vault: {0} / {1} slots"] = "仓库：{0} / {1} 格",
            ["{0} slots"] = "占 {0} 格",
            ["You must be online to use the vault."] = "你需要在线才能使用仓库。",
            ["Item not found."] = "找不到该物品。",
            ["Enter a valid quantity."] = "请输入有效的数量。",
            ["Unknown action."] = "未知操作。",
            ["Vault is full."] = "仓库已满。",
            ["You don't have {0} in your backpack."] = "你的背包里没有 {0}。",
            ["Stored {0}× {1}."] = "已存入 {0}× {1}。",
            ["You have no {0} in the vault."] = "仓库里没有 {0}。",
            ["Took {0}× {1}."] = "已取出 {0}× {1}。",

            // ---- admin module chrome ----
            ["Capacity (grid cells)"] = "容量（格子数）",
            ["e.g. 200 — each item costs its size_x×size_y footprint"] = "如 200——每个物品按其 宽×高 格数占用",
            ["Total vault capacity in inventory grid cells. Each stored item costs its grid footprint (e.g. a 2×2 ammo box = 4); an item's internal stack/ammo count never counts."]
                = "仓库总容量（背包格子数）。每个物品按其网格尺寸占用（如 2×2 的弹药箱 = 4 格）；物品内部的堆叠/弹药数不计入。",
            ["Enter a valid capacity (a positive whole number)."] = "请输入有效的容量（正整数）。",
            ["Saved."] = "已保存。",

            // ---- player command help (intro page); keys are the English descriptions ----
            ["Open the personal vault — store, take and list your items."] = "打开私人仓库——存入、取出、查看你的物品。",
            ["Store items from your backpack into the vault (by item id)."] = "把背包里的物品（按物品 ID）存入仓库。",
            ["Withdraw items from the vault into your backpack (by item id)."] = "把仓库里的物品（按物品 ID）取回背包。",
            ["List your vault contents and how full it is."] = "列出你的仓库内容与占用情况。",
        };
    }
}
