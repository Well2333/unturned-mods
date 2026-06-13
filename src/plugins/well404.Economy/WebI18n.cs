using System.Collections.Generic;

namespace well404.Economy
{
    /// <summary>
    /// Chinese translations for Economy's web strings (English source strings are the keys).
    /// Registered into the global web-translation registry on load. English is the default and
    /// needs no table.
    /// </summary>
    internal static class WebI18n
    {
        public const string Zh = "zh";

        public static readonly IReadOnlyDictionary<string, string> ZhTable = new Dictionary<string, string>
        {
            // ---- player wallet menu ----
            ["Wallet"] = "钱包",
            ["Balance: {0}{1} {2}"] = "余额：{0}{1} {2}",
            ["Transfers are currently disabled."] = "转账功能当前已关闭。",
            ["Send to this player"] = "向该玩家转账",
            ["Send to this player (fee {0}%)"] = "向该玩家转账（手续费 {0}%）",
            ["Transfer"] = "转账",
            ["Amount to transfer (min {0}{1})"] = "转账金额（最低 {0}{1}）",
            ["No other players are online to transfer to."] = "当前没有其他在线玩家可转账。",
            ["Unknown action."] = "未知操作。",
            ["Enter a valid amount."] = "请输入有效的金额。",
            ["Minimum transfer is {0}{1}."] = "单次转账至少 {0}{1}。",
            ["You can't transfer to yourself."] = "不能转账给自己。",
            ["Insufficient balance."] = "余额不足。",
            ["Transferred {0} to {1}. They received {2} (fee {3})."] = "已向 {1} 转账 {0}（对方实收 {2}，手续费 {3}）。",

            // ---- admin module chrome ----
            ["Economy / Balances"] = "经济 / 余额",
            ["All balances"] = "所有余额",
            ["Player"] = "玩家",
            ["Balance"] = "余额",
            ["Name or SteamID (17 digits)"] = "名字或 SteamID（17位）",
            ["The database backend lists every account; the experience backend lists online players only. Click a row to edit its balance; Add sets a player's balance."]
                = "数据库后端列全部账户，经验后端仅在线玩家。点条目编辑其余额，「新增」设置某玩家余额。",
            ["Currency & backend"] = "货币与后端",
            ["Currency name"] = "货币名",
            ["Symbol"] = "符号",
            ["Starting balance"] = "初始余额",
            ["Backend"] = "后端",
            ["Change the currency display and storage backend."] = "修改货币显示与存储后端。",
            ["Kill rewards (income)"] = "击杀奖励（经济来源）",
            ["Master switch"] = "总开关",
            ["Kill a player"] = "击杀玩家",
            ["Kill a zombie"] = "击杀僵尸",
            ["Kill a mega zombie"] = "击杀Boss僵尸",
            ["Kill an animal"] = "击杀动物",
            ["0 = disabled"] = "0=禁用",
            ["How much currency each kill source grants. 0 disables that source."] = "击杀获得货币的各来源金额。设为 0 禁用该来源。",
            ["Transfers"] = "转账设置",
            ["Min transfer"] = "最小转账额",
            ["Tax (%)"] = "税率(%)",
            ["The /pay toggle, minimum amount and tax percentage."] = "玩家 /pay 转账的开关、最小额与税率比例。",

            // ---- player command help (intro page) ----
            ["economy.group"] = "经济",
            ["economy.cmd.balance"] = "查看你的余额",
            ["economy.cmd.pay"] = "向其他玩家转账",
        };
    }
}
