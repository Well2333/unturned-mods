using System.Collections.Generic;

namespace well404.AdminTools
{
    /// <summary>Chinese translations for AdminTools' web strings (English source strings are the keys).</summary>
    internal static class WebI18n
    {
        public const string Zh = "zh";

        public static readonly IReadOnlyDictionary<string, string> ZhTable = new Dictionary<string, string>
        {
            // ---- module chrome ----
            ["Admin tools"] = "管理员工具",
            ["Online players"] = "在线玩家",
            ["Name"] = "名字",
            ["SteamID"] = "SteamID",
            ["Godmode"] = "无敌",
            ["Player"] = "玩家",
            ["Name or SteamID"] = "名字或 SteamID",
            ["Invincible"] = "无敌",
            ["Make an online player invincible (or turn it off). Cleared on restart."] = "让在线玩家无敌（或关闭）。重启后清除。",
            ["Kick"] = "踢出",
            ["Reason"] = "原因",
            ["Disconnect an online player."] = "将在线玩家断开连接。",
            ["Ban"] = "封禁",
            ["Minutes"] = "分钟",
            ["Empty = permanent"] = "留空=永久",
            ["Ban a player. Leave minutes empty for a permanent ban."] = "封禁玩家。分钟留空即永久封禁。",
            ["Unban"] = "解封",
            ["Lift a ban by SteamID."] = "按 SteamID 解除封禁。",

            // ---- result messages (localized from the AdminResult template) ----
            ["Enter a player."] = "请输入玩家。",
            ["Enter a SteamID."] = "请输入 SteamID。",
            ["Player not online: {0}"] = "玩家不在线：{0}",
            ["Player not found: {0}"] = "找不到玩家：{0}",
            ["Godmode ON for {0}."] = "已为 {0} 开启无敌。",
            ["Godmode OFF for {0}."] = "已为 {0} 关闭无敌。",
            ["Kicked {0}."] = "已踢出 {0}。",
            ["Banned {0} for {1} min."] = "已封禁 {0} {1} 分钟。",
            ["Banned {0} permanently."] = "已永久封禁 {0}。",
            ["Unbanned {0}."] = "已解封 {0}。",
            ["{0} was not banned."] = "{0} 并未被封禁。",
            ["Invalid SteamID: {0}"] = "无效的 SteamID：{0}",
        };
    }
}
