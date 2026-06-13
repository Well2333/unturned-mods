using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnturnedMods.Shared.WebPanel;

namespace well404.AdminTools
{
    /// <summary>
    /// The admin module: online players overview, godmode, kick/ban/unban, role assignment, and
    /// granting/revoking commands per role. English labels are i18n keys localized per request.
    /// </summary>
    internal static class AdminToolsWebPanelModule
    {
        public const string ModuleId = "well404.admintools";

        public static WebPanelModule Create(AdminToolsService admin)
        {
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
                            admin.IsGod(u.SteamId.m_SteamID) ? "ON" : ""
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
                    if (player == null) return WebActionResult.Fail("Enter a player.");
                    return Map(await admin.SetGodAsync(player, request.Get("enable") != "false"));
                },
                fields: new[]
                {
                    new WebField("player", "Player", WebFieldType.Text, required: true, placeholder: "Name or SteamID"),
                    new WebField("enable", "Invincible", WebFieldType.Boolean)
                },
                description: "Make an online player invincible (or turn it off). Cleared on restart.");

            var kick = new WebPanelAction(
                id: "kick",
                label: "Kick",
                kind: WebActionKind.Form,
                handler: async request =>
                {
                    var player = request.Get("player");
                    if (player == null) return WebActionResult.Fail("Enter a player.");
                    return Map(await admin.KickAsync(player, request.Get("reason")));
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
                    if (player == null) return WebActionResult.Fail("Enter a player.");
                    var minutes = request.GetDecimal("minutes");
                    return Map(await admin.BanAsync(player, request.Get("reason"), minutes == null ? (int?)null : (int)minutes.Value));
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
                    return id == null ? WebActionResult.Fail("Enter a SteamID.") : Map(await admin.UnbanAsync(id));
                },
                fields: new[] { new WebField("steamId", "SteamID", WebFieldType.Text, required: true) },
                description: "Lift a ban by SteamID.");

            var roles = new WebPanelAction(
                id: "roles",
                label: "Roles",
                kind: WebActionKind.Table,
                handler: async _ =>
                {
                    var all = await admin.GetRolesAsync();
                    var rows = all
                        .OrderByDescending(r => r.Priority)
                        .Select(r => (IReadOnlyList<string>)new[] { r.Id, r.DisplayName, r.Priority.ToString() })
                        .ToList();
                    return WebActionResult.Table(new[] { "Role ID", "Name", "Priority" }, rows);
                });

            var playerRoles = new WebPanelAction(
                id: "playerroles",
                label: "Player roles",
                kind: WebActionKind.Form,
                handler: async request =>
                {
                    var player = request.Get("player");
                    var role = request.Get("role");
                    if (player == null || role == null) return WebActionResult.Fail("Enter a player and a role.");
                    return Map(await admin.SetPlayerRoleAsync(player, role, request.Get("action") != "remove"));
                },
                fields: new[]
                {
                    new WebField("player", "Player", WebFieldType.Text, required: true, placeholder: "Name or SteamID (17 digits for offline)"),
                    new WebField("role", "Role ID", WebFieldType.Text, required: true, placeholder: "see the Roles table (e.g. vip)"),
                    new WebField("action", "Action", WebFieldType.Select, options: new[] { "add", "remove" })
                },
                description: "Add or remove a permission role (e.g. VIP) for a player.");

            var findCommands = new WebPanelAction(
                id: "findcommands",
                label: "Find commands",
                kind: WebActionKind.Search,
                handler: async request =>
                {
                    var rows = (await admin.SearchCommandsAsync(request.Get("query") ?? ""))
                        .Select(c => (IReadOnlyList<string>)new[] { c.Id, c.Permission, c.Description })
                        .ToList();
                    return WebActionResult.Table(new[] { "Command", "Permission", "Description" }, rows);
                },
                fields: new[] { new WebField("query", "Command name or ID", WebFieldType.Text, placeholder: "Type to filter; empty lists all") },
                description: "Look up a command's permission node to grant it to a role below.");

            var roleCommands = new WebPanelAction(
                id: "rolecommands",
                label: "Role commands",
                kind: WebActionKind.Form,
                handler: async request =>
                {
                    var role = request.Get("role");
                    var command = request.Get("command");
                    if (role == null || command == null) return WebActionResult.Fail("Enter a role and a command.");
                    return Map(await admin.SetRoleCommandAsync(role, command, request.Get("grant") != "false"));
                },
                fields: new[]
                {
                    new WebField("role", "Role ID", WebFieldType.Text, required: true),
                    new WebField("command", "Command or permission", WebFieldType.Text, required: true, placeholder: "command id (e.g. buy) or a permission node"),
                    new WebField("grant", "Grant", WebFieldType.Boolean)
                },
                description: "Grant (or revoke) a command for a role. Use «Find commands» to look up names.");

            var viewRoleCommands = new WebPanelAction(
                id: "viewrolecommands",
                label: "A role's commands",
                kind: WebActionKind.Search,
                handler: async request =>
                {
                    var role = request.Get("query");
                    if (role == null) return WebActionResult.Table(new[] { "Granted permission" }, new List<IReadOnlyList<string>>(), "Enter a role ID.");
                    var rows = (await admin.GetRolePermissionsAsync(role))
                        .Select(p => (IReadOnlyList<string>)new[] { p })
                        .ToList();
                    return WebActionResult.Table(new[] { "Granted permission" }, rows);
                },
                fields: new[] { new WebField("query", "Role ID", WebFieldType.Text, placeholder: "e.g. vip") },
                description: "List the permissions (including commands) currently granted to a role.");

            return new WebPanelModule(
                ModuleId, "Admin tools",
                new[] { players, godmode, kick, ban, unban, roles, playerRoles, findCommands, roleCommands, viewRoleCommands },
                icon: "🛡️");
        }

        private static WebActionResult Map(AdminResult result)
            => result.Ok ? WebActionResult.Ok(result.Message) : WebActionResult.Fail(result.Message);
    }
}
