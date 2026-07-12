using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnturnedMods.Shared.WebPanel;

namespace well404.AdminTools
{
    /// <summary>
    /// The admin module: an online-players overview plus godmode, kick, ban and unban. English
    /// labels are i18n keys localized per request; result messages are localized via the translation
    /// registry from the <see cref="AdminResult"/> template + arguments.
    /// </summary>
    internal static class AdminToolsWebPanelModule
    {
        public const string ModuleId = "well404.admintools";

        public static WebPanelModule Create(AdminToolsService admin, IWebTranslationRegistry tr)
        {
            WebActionResult Map(AdminResult result, string lang)
                => result.Ok
                    ? WebActionResult.Ok(tr.Format(lang, result.Key, result.Args))
                    : WebActionResult.Fail(tr.Format(lang, result.Key, result.Args));

            var players = new WebPanelAction(
                id: "players",
                label: "Online players",
                kind: WebActionKind.Table,
                handler: async _ =>
                {
                    await UniTask.SwitchToMainThread();
                    var rows = admin.OnlineUsers()
                        .Select(u => (IReadOnlyList<string>)new[]
                        {
                            u.DisplayName,
                            u.SteamId.m_SteamID.ToString(),
                            admin.IsGod(u.SteamId.m_SteamID) ? "✓" : ""
                        })
                        .ToList();
                    return WebActionResult.Table(new[] { "Name", "SteamID", "Godmode" }, rows);
                });

            var godmode = new WebPanelAction(
                id: "godmode",
                label: "Godmode",
                kind: WebActionKind.Form,
                handler: async request =>
                {
                    await UniTask.SwitchToMainThread();
                    var player = request.Get("player");
                    if (player == null) return WebActionResult.Fail(tr.Resolve("Enter a player.", request.Language));
                    return Map(await admin.SetGodAsync(player, request.Get("enable") != "false"), request.Language);
                },
                fields: new[]
                {
                    new WebField("player", "Player", WebFieldType.Text, required: true, placeholder: "Name or SteamID"),
                    new WebField("enable", "Invincible", WebFieldType.Boolean)
                },
                description: "Make an online player invincible (or turn it off). Cleared on restart.");

            var repair = new WebPanelAction(
                id: "repair",
                label: "Repair equipment",
                kind: WebActionKind.Form,
                handler: async request =>
                {
                    var player = request.Get("player");
                    if (player == null) return WebActionResult.Fail(tr.Resolve("Enter a player.", request.Language));
                    return Map(await admin.RepairEquipmentAsync(player), request.Language);
                },
                fields: new[]
                {
                    new WebField("player", "Player", WebFieldType.Text, required: true, placeholder: "Name or SteamID")
                },
                description: "Restore all durability-bearing equipment on an online player to 100%.");

            var kick = new WebPanelAction(
                id: "kick",
                label: "Kick",
                kind: WebActionKind.Form,
                handler: async request =>
                {
                    var player = request.Get("player");
                    if (player == null) return WebActionResult.Fail(tr.Resolve("Enter a player.", request.Language));
                    return Map(await admin.KickAsync(player, request.Get("reason")), request.Language);
                },
                fields: new[]
                {
                    new WebField("player", "Player", WebFieldType.Text, required: true, placeholder: "Name or SteamID"),
                    new WebField("reason", "Reason", WebFieldType.Text)
                },
                description: "Disconnect an online player.");

            var ban = new WebPanelAction(
                id: "ban",
                label: "Ban",
                kind: WebActionKind.Form,
                handler: async request =>
                {
                    var player = request.Get("player");
                    if (player == null) return WebActionResult.Fail(tr.Resolve("Enter a player.", request.Language));
                    var minutes = request.GetDecimal("minutes");
                    return Map(await admin.BanAsync(player, request.Get("reason"), minutes == null ? (int?)null : (int)minutes.Value), request.Language);
                },
                fields: new[]
                {
                    new WebField("player", "Player", WebFieldType.Text, required: true, placeholder: "Name or SteamID"),
                    new WebField("minutes", "Minutes", WebFieldType.Number, placeholder: "Empty = permanent"),
                    new WebField("reason", "Reason", WebFieldType.Text)
                },
                description: "Ban a player. Leave minutes empty for a permanent ban.");

            var unban = new WebPanelAction(
                id: "unban",
                label: "Unban",
                kind: WebActionKind.Form,
                handler: async request =>
                {
                    var id = request.Get("steamId");
                    return id == null
                        ? WebActionResult.Fail(tr.Resolve("Enter a SteamID.", request.Language))
                        : Map(await admin.UnbanAsync(id), request.Language);
                },
                fields: new[] { new WebField("steamId", "SteamID", WebFieldType.Text, required: true) },
                description: "Lift a ban by SteamID.");

            return new WebPanelModule(
                ModuleId, "Admin tools",
                new[] { players, godmode, repair, kick, ban, unban },
                icon: "🛡️");
        }
    }
}
