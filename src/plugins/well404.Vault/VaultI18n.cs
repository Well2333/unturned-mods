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
            ["Hands"] = "手部",
            ["Vest"] = "胸挂",
            ["Shirt"] = "上衣",
            ["Pants"] = "裤子",
            ["Store"] = "存入",
            ["Take"] = "取出",
            ["Amount to store"] = "存入数量",
            ["Amount to take"] = "取出数量",
            ["All"] = "全部",
            ["Amount"] = "数量",
            ["One"] = "一件",
            ["Store every copy of this item?"] = "确定存入这种物品的全部数量吗？",
            ["Take every copy of this item?"] = "确定取出这种物品的全部数量吗？",
            ["Amount {0}"] = "数量 {0}",
            ["Vault: {0} / {1} slots"] = "仓库：{0} / {1} 格",
            ["Personal vault: {0} / {1} slots"] = "个人仓库：{0} / {1} 格",
            ["Team vault"] = "小队仓库",
            ["Team vault {0}: {1} / {2} slots"] = "小队仓库 {0}：{1} / {2} 格",
            ["Join or create a party to use the team vault."] = "加入或创建小队后才能使用小队仓库。",
            ["Buy personal vault capacity"] = "购买个人仓库容量",
            ["Buy team vault capacity"] = "购买小队仓库容量",
            ["Buy capacity"] = "购买容量",
            ["Add {0} slots for {1}"] = "花费 {1} 增加 {0} 格",
            ["Maximum {0} slots"] = "最高 {0} 格",
            ["Spend {0} from your balance to add {1} slots to the team vault? Capacity belongs to the team and is not refunded when you leave."]
                = "确定从你的余额中花费 {0}，为小队仓库增加 {1} 格吗？容量归小队所有，离队后不退款。",
            ["Spend {0} from your balance to add {1} vault slots?"] = "确定从余额中花费 {0} 购买 {1} 格仓库容量吗？",
            ["Bought {0} personal vault slots for {1}. New capacity: {2}."] = "已花费 {1} 购买 {0} 格个人仓库容量，新容量为 {2} 格。",
            ["Bought {0} team vault slots for {1}. New capacity: {2}."] = "已花费 {1} 购买 {0} 格小队仓库容量，新容量为 {2} 格。",
            ["The economy plugin is required to buy vault capacity."] = "购买仓库容量需要经济插件。",
            ["The personal vault is already at its maximum capacity."] = "个人仓库已达到容量上限。",
            ["The personal vault changed; refresh and try again."] = "个人仓库状态已变化，请刷新后重试。",
            ["Personal vault purchasing is not configured correctly."] = "个人仓库购买配置不正确。",
            ["Personal vault capacity purchasing is disabled."] = "个人仓库容量购买已关闭。",
            ["The personal vault capacity purchase failed."] = "购买个人仓库容量失败。",
            ["The economy plugin is required to buy team vault capacity."] = "购买小队仓库容量需要经济插件。",
            ["The team vault is already at its maximum capacity."] = "小队仓库已达到容量上限。",
            ["The team vault changed; refresh and try again."] = "小队仓库状态已变化，请刷新后重试。",
            ["Team vault purchasing is not configured correctly."] = "小队仓库购买配置不正确。",
            ["Team vault capacity purchasing is disabled."] = "小队仓库容量购买已关闭。",
            ["The team vault capacity purchase failed."] = "购买小队仓库容量失败。",
            ["{0} slots"] = "占 {0} 格",
            ["You must be online to store or withdraw."] = "你需要在线才能存入或取出物品。",
            ["You don't have permission to use the vault."] = "你没有使用仓库的权限。",
            ["Item not found."] = "找不到该物品。",
            ["Enter a valid quantity."] = "请输入有效的数量。",
            ["Unknown action."] = "未知操作。",
            ["Vault is full."] = "仓库已满。",
            ["You don't have {0} in your backpack."] = "你的背包里没有 {0}。",
            ["Stored {0}× {1}."] = "已存入 {0}× {1}。",
            ["You have no {0} in the vault."] = "仓库里没有 {0}。",
            ["Took {0}× {1}."] = "已取出 {0}× {1}。",
            ["Destination vault is full."] = "目标仓库已满。",
            ["Moved {0}× {1} to the other vault."] = "已将 {0}× {1} 转移到另一个仓库。",
            ["Durability {0}%"] = "耐久 {0}%",

            // ---- admin module chrome ----
            ["Capacity (grid cells)"] = "容量（格子数）",
            ["e.g. 200 — each item costs its size_x×size_y footprint"] = "如 200——每个物品按其 宽×高 格数占用",
            ["Enter a valid capacity (a positive whole number)."] = "请输入有效的容量（正整数）。",
            ["Bad tier format (expected permission=capacity)."] = "档位格式有误（应为 权限=容量）。",
            ["Invalid tier capacity (a positive whole number)."] = "档位容量无效（应为正整数）。",
            ["Saved."] = "已保存。",
            ["Deleted."] = "已删除。",
            ["Not found."] = "未找到。",
            // capacity tiers (per permission)
            ["Capacity tiers"] = "容量档位",
            ["permission=capacity, comma-separated: e.g. well404.vault.size.vip=400, well404.vault.size.mvp=600"]
                = "权限=容量，逗号分隔：如 well404.vault.size.vip=400, well404.vault.size.mvp=600",
            ["Players can buy personal capacity"] = "允许玩家购买个人仓库容量",
            ["Personal maximum capacity"] = "个人仓库容量上限",
            ["Personal slots per purchase"] = "个人仓库每次购买格数",
            ["Personal price per purchase"] = "个人仓库每次购买价格",
            ["Base personal capacity, permission tiers, and purchasable personal/team capacity. Administrators edit a currently viewed vault capacity in Vault inspection."]
                = "设置个人基础容量、权限档位，以及个人/小队容量购买；管理员可在仓库查看中直接修改当前仓库容量。",
            ["Team vault enabled"] = "启用小队仓库",
            ["Team base capacity"] = "小队基础容量",
            ["Team maximum capacity"] = "小队容量上限",
            ["Default: 5000"] = "默认：5000",
            ["Members can buy capacity"] = "允许成员购买容量",
            ["Slots per purchase"] = "每次购买格数",
            ["Price per purchase"] = "每次购买价格",
            ["Enter valid team vault capacities and purchase price."] = "请输入有效的小队仓库容量与购买价格。",
            ["Team vaults"] = "小队仓库",
            ["Party-owned shared vaults, including their stable owner key, capacity and current usage."]
                = "小队拥有的共享仓库，包括稳定的小队标识、容量与当前占用。",

            // ---- player command help (intro page); keys are the English descriptions ----
            ["Open the personal vault — store, take and list your items."] = "打开私人仓库——存入、取出、查看你的物品。",
            ["Store items from your backpack into the vault (by item id)."] = "把背包里的物品（按物品 ID）存入仓库。",
            ["Withdraw items from the vault into your backpack (by item id)."] = "把仓库里的物品（按物品 ID）取回背包。",
            ["List your vault contents and how full it is."] = "列出你的仓库内容与占用情况。",
            ["Spend your own balance to buy personal vault capacity."] = "使用自己的余额购买个人仓库容量。",
            ["Store backpack items in your current party's shared vault."] = "把背包物品存入当前小队的共享仓库。",
            ["Take items from your current party's shared vault."] = "从当前小队的共享仓库取出物品。",
            ["List the current party's shared vault."] = "列出当前小队的共享仓库内容。",
            ["Spend your own balance to buy shared vault capacity for the party."] = "使用自己的余额为小队购买共享仓库容量。",
        };
    }
}
