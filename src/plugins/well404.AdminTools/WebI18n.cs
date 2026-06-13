using System.Collections.Generic;

namespace well404.AdminTools
{
    /// <summary>Chinese translations for AdminTools' web strings (English source strings are the keys).</summary>
    internal static class WebI18n
    {
        public const string Zh = "zh";

        public static readonly IReadOnlyDictionary<string, string> ZhTable = new Dictionary<string, string>
        {
            ["Admin tools"] = "管理员工具",
            ["Online players"] = "在线玩家",
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
            ["SteamID"] = "SteamID",
            ["Lift a ban by SteamID."] = "按 SteamID 解除封禁。",
            ["Roles"] = "权限组",
            ["Player roles"] = "玩家权限组",
            ["Role ID"] = "权限组ID",
            ["Name or SteamID (17 digits for offline)"] = "名字或 SteamID（离线用17位 SteamID）",
            ["see the Roles table (e.g. vip)"] = "见上方权限组表（如 vip）",
            ["Action"] = "操作",
            ["Add or remove a permission role (e.g. VIP) for a player."] = "为玩家添加或移除权限组（如 VIP）。",
            ["Find commands"] = "查找指令",
            ["Command name or ID"] = "指令名或ID",
            ["Type to filter; empty lists all"] = "输入以筛选；留空列出全部",
            ["Look up a command's permission node to grant it to a role below."] = "查出某指令的权限节点，便于下面授予给权限组。",
            ["Role commands"] = "权限组指令",
            ["Command or permission"] = "指令或权限节点",
            ["command id (e.g. buy) or a permission node"] = "指令ID（如 buy）或权限节点",
            ["Grant"] = "授予",
            ["Grant (or revoke) a command for a role. Use «Find commands» to look up names."] = "为权限组授予（或撤销）某指令。用「查找指令」查名称。",
            ["A role's commands"] = "权限组已有指令",
            ["e.g. vip"] = "如 vip",
            ["List the permissions (including commands) currently granted to a role."] = "列出某权限组当前已授予的权限（含指令）。",
        };
    }
}
