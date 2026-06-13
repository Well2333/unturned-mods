using System.Collections.Generic;

namespace well404.Essentials
{
    /// <summary>Chinese translations for Essentials' web strings (English source strings are the keys).</summary>
    internal static class WebI18n
    {
        public const string Zh = "zh";

        public static readonly IReadOnlyDictionary<string, string> ZhTable = new Dictionary<string, string>
        {
            // ---- player "Utilities" menu ----
            ["Utilities"] = "实用工具",
            ["You must be online to use these tools."] = "你需要在线才能使用这些工具。",
            ["Home"] = "家",
            ["Go home"] = "回家",
            ["Set home here"] = "把家设在这里",
            ["No home set yet — tap “Set home here”."] = "尚未设置家——点「把家设在这里」即可设置。",
            ["Home set to your current location."] = "已把家设在你当前的位置。",
            ["Back to death point"] = "返回死亡点",
            ["Return"] = "返回",
            ["No death point yet — it appears after you die."] = "暂无死亡点——你死亡后会出现在这里。",
            ["Teleport"] = "传送",
            ["No warps are available to you right now."] = "当前没有你可用的传送点。",
            ["Other players"] = "其他玩家",
            ["No other players are online right now."] = "当前没有其他在线玩家。",
            ["You're not in a party yet — invite an online player below to start one."] = "你还没有队伍——在下方邀请一名在线玩家即可组队。",
            ["Gifts"] = "礼包",
            ["No gift packs are available to you right now."] = "当前没有你可领取的礼包。",
            ["Teleport request from {0}"] = "{0} 请求传送到你这里",
            ["Accept"] = "接受",
            ["Deny"] = "拒绝",
            ["Party invite from {0}"] = "{0} 邀请你组队",
            ["Party"] = "队伍",
            ["{0} (leader)"] = "{0}（队长）",
            ["Leave party"] = "退出队伍",
            ["Kick from party"] = "踢出队伍",
            ["Kicked {0} from the party."] = "已把 {0} 踢出队伍。",
            ["Only the party leader can kick members."] = "只有队长才能踢出队员。",
            ["You can't kick yourself."] = "你不能把自己踢出队伍。",
            ["That player is not in your party."] = "该玩家不在你的队伍中。",
            ["Request teleport"] = "请求传送",
            ["Invite to party"] = "邀请组队",
            ["Claim"] = "领取",
            ["Refreshes in {0}"] = "{0} 后刷新",
            ["Sleep vote"] = "睡觉投票",
            ["Vote to sleep"] = "投票睡觉",
            ["Tap to use; some teleports need you to stand still briefly."] = "点击即用；部分传送需短暂站立读条。",
            ["That player is no longer online."] = "该玩家已不在线。",
            ["You already have a teleport request to {0}."] = "你已经向 {0} 发起过传送请求。",
            ["Teleport request sent to {0}."] = "已向 {0} 发送传送请求。",
            ["That request is no longer pending."] = "该请求已不存在。",
            ["Accepted {0}'s teleport request."] = "已接受 {0} 的传送请求。",
            ["Teleport request denied."] = "已拒绝传送请求。",
            ["{0} is already in your party."] = "{0} 已在你的队伍中。",
            ["You already invited {0}."] = "你已经邀请过 {0}。",
            ["Party invite sent to {0}."] = "已向 {0} 发送组队邀请。",
            ["That invite is no longer pending."] = "该邀请已不存在。",
            ["That party is full."] = "队伍已满。",
            ["Could not join the party."] = "无法加入队伍。",
            ["Joined {0}'s party."] = "已加入 {0} 的队伍。",
            ["Party invite denied."] = "已拒绝组队邀请。",
            ["You left the party."] = "你已退出队伍。",
            ["You are not in a party."] = "你不在任何队伍中。",
            ["Claimed {0}."] = "已领取 {0}。",
            ["You can't claim that gift."] = "你无法领取该礼包。",
            ["Not ready yet — refreshes in {0}."] = "尚未就绪——{0} 后刷新。",
            ["Gift not found."] = "找不到该礼包。",
            ["Sleep voting is disabled."] = "睡觉投票已关闭。",
            ["You already voted to sleep."] = "你已经投过票了。",
            ["The vote passed — time changed."] = "投票通过——时间已切换。",
            ["Your sleep vote was counted."] = "你的睡觉投票已记录。",
            ["Unknown action."] = "未知操作。",
            ["You haven't set a home yet."] = "你还没有设置家。",
            ["No death point to return to."] = "没有可返回的死亡点。",
            ["Warp not found."] = "找不到该传送点。",
            ["You don't have access to that warp."] = "你没有权限使用该传送点。",
            ["Unknown destination."] = "未知目的地。",
            ["Teleported."] = "已传送。",
            ["Teleport didn't complete — check the in-game notice."] = "传送未完成——请查看游戏内提示。",

            // ---- admin module chrome ----
            ["Essentials"] = "实用功能 / Essentials",
            ["Teleport rules"] = "传送设置",
            ["Warm-up seconds"] = "预热秒数",
            ["Seconds to stand still before teleporting; 0 = instant"] = "传送前需静止的秒数，0=瞬移",
            ["Cancel on move"] = "移动取消",
            ["Move threshold (m)"] = "移动阈值(米)",
            ["Cooldown seconds"] = "冷却秒数",
            ["Cooldown after a successful teleport; 0 = none"] = "成功传送后的冷却，0=无",
            ["home cost"] = "home 费用",
            ["tp cost"] = "tp 费用",
            ["warp cost"] = "warp 费用",
            ["back cost"] = "back 费用",
            ["Shared rules for all teleports (home/tp/warp/back). Costs require an economy plugin (e.g. well404.Economy); default 0 = free."]
                = "所有传送(home/tp/warp/back)共用的规则。费用需安装经济插件(如 well404.Economy)才会扣费，默认 0=免费。",
            ["tpa request lifetime (s)"] = "tpa 请求有效期(秒)",
            ["party invite lifetime (s)"] = "party 邀请有效期(秒)",
            ["party max members"] = "party 人数上限",
            ["0 = no extra limit"] = "0=不额外限制",
            ["sleep voting"] = "sleep 投票",
            ["sleep pass ratio"] = "sleep 通过比例",
            ["0.5 = half"] = "0.5=半数",
            ["back invincibility (s)"] = "back 无敌秒数",
            ["tpa/party request timeouts, party member cap, sleep-vote toggle and pass ratio (of online players), and /back post-landing invincibility seconds (0 = none)."]
                = "tpa/party 请求超时、party 人数上限、sleep 投票开关与通过比例(在线玩家的比例)、/back 落地后的无敌秒数(0=无)。",
            ["tpa / sleep / back"] = "tpa / sleep / back（其它规则）",
            ["Saved tpa / sleep / back settings."] = "已保存 tpa / sleep / back 设置。",
            ["Warps"] = "传送点",
            ["Name"] = "名称",
            ["Name used by /warp"] = "/warp 用的名称",
            ["Yaw"] = "朝向(yaw)",
            ["0 = use global cooldown"] = "0=用全局冷却",
            ["Players teleport with /warp <name> and also need permission well404.essentials.warps.<name>. Capture coordinates in-game with /warp set."]
                = "玩家用 /warp <名称> 传送，还需要权限 well404.essentials.warps.<名称>。坐标可在游戏内用 /warp set 采集。",
            ["Gift packs"] = "礼包",
            ["Gift ID"] = "礼包ID",
            ["Unique ID used by /gift"] = "/gift 用的唯一ID",
            ["Display name"] = "显示名",
            ["Permission"] = "权限",
            ["Empty = everyone; set a permission node for VIP-only"] = "留空=所有人；VIP 专属填权限节点",
            ["Refresh (crontab)"] = "刷新(crontab)",
            ["e.g. 0 0 * * * (daily at 0:00); empty = one-time only"] = "如 0 0 * * * (每天0点)；留空=只能领一次",
            ["Gift contents"] = "物品",
            ["itemId×amount, comma-separated. e.g. 15x2, 81x1"] = "物品ID×数量，逗号分隔。如 15x2, 81x1",
            ["Free gift packs. Each player may claim once per crontab period. crontab uses server local time. Look up item IDs with the search below."]
                = "免费礼包。每位玩家在每个 crontab 周期内可领一次。crontab 按服务器本地时间。物品ID 可用下方检索。",
            ["Search game items"] = "检索游戏物品",
            ["Item name or ID"] = "物品名或ID",
            ["Type a keyword or numeric ID…"] = "输入关键词或数字ID…",
            ["Fuzzy-search all game items by name or ID; take the item ID into the gift contents."]
                = "在全部游戏物品中按名称或 ID 模糊检索，拿到「物品ID」填到礼包的物品里。",
            ["Item ID"] = "物品ID",
            ["Type an item name or ID to search."] = "输入物品名称或 ID 进行检索。",

            // ---- player command help (intro page); keys are the English descriptions ----
            ["Teleport back to the home you saved. After a short warm-up you return to that spot."]
                = "传送回你保存的家：短暂读条后回到那个位置。",
            ["Save your current position as your home, so /home brings you back here."]
                = "把你当前的位置保存为家，之后用 /home 就能传送回这里。",
            ["Return to the place where you last died (available for a short while after death)."]
                = "返回你上次死亡的地点（死亡后的一小段时间内可用）。",
            ["Ask another online player for permission to teleport to them."]
                = "向另一名在线玩家请求传送到对方身边（需对方同意）。",
            ["Accept the most recent teleport request someone sent you."]
                = "接受别人最近发给你的一条传送请求。",
            ["Decline the most recent teleport request someone sent you."]
                = "拒绝别人最近发给你的一条传送请求。",
            ["Create or manage a party — invite, accept, leave, kick. Party members can teleport to each other."]
                = "创建或管理队伍——邀请、接受、退出、踢人；队员之间可以互相传送。",
            ["Teleport to a named warp point you have access to."]
                = "传送到你有权限使用的某个命名传送点。",
            ["List every warp point you are allowed to teleport to."]
                = "列出所有你被允许传送的传送点。",
            ["Claim the free gift packs available to you (some refresh on a schedule)."]
                = "领取向你开放的免费礼包（部分礼包会按计划定时刷新）。",
            ["Vote to skip the night; once enough online players vote, the time changes to morning."]
                = "投票跳过夜晚；当足够多的在线玩家投票后，时间会切换到白天。",
        };
    }
}
