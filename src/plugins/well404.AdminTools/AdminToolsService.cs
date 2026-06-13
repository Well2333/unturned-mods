using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using OpenMod.API.Commands;
using OpenMod.API.Permissions;
using OpenMod.API.Users;
using OpenMod.Core.Permissions;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using SDG.Unturned;
using Steamworks;

namespace well404.AdminTools
{
    /// <summary>The result of an admin action: a success flag and a human-readable detail.</summary>
    public sealed class AdminResult
    {
        public AdminResult(bool ok, string message) { Ok = ok; Message = message; }
        public bool Ok { get; }
        public string Message { get; }
        public static AdminResult Fail(string message) => new AdminResult(false, message);
        public static AdminResult Done(string message) => new AdminResult(true, message);
    }

    /// <summary>
    /// The moderation/permission operations behind both the commands and the web panel: godmode,
    /// kick, temporary ban / unban, assigning a player to a role, and granting/revoking a command
    /// for a role. All messages are English (the source language); callers may localize them.
    /// </summary>
    public sealed class AdminToolsService
    {
        private readonly ILifetimeScope m_Scope;
        private readonly GodModeService m_God;

        // OpenMod permission/command/user services are resolved on demand from the plugin scope:
        // some are not resolvable when a plugin-scoped singleton is first constructed, so capturing
        // them in the constructor would fail plugin load. Lazy resolution keeps the plugin loadable.
        public AdminToolsService(ILifetimeScope scope, GodModeService god)
        {
            m_Scope = scope;
            m_God = god;
        }

        private IUserManager m_UserManager => m_Scope.Resolve<IUserManager>();
        private IUnturnedUserDirectory m_UserDirectory => m_Scope.Resolve<IUnturnedUserDirectory>();
        private IPermissionRoleStore m_Roles => m_Scope.Resolve<IPermissionRoleStore>();
        private IPermissionStore m_Permissions => m_Scope.Resolve<IPermissionStore>();
        private ICommandStore m_Commands => m_Scope.Resolve<ICommandStore>();
        private ICommandPermissionBuilder m_CommandPermission => m_Scope.Resolve<ICommandPermissionBuilder>();
        // Role permissions are edited via the roles data store (IPermissionStore isn't registered here).
        private IPermissionRolesDataStore m_RolesData => m_Scope.Resolve<IPermissionRolesDataStore>();

        // ----- godmode / kick / ban -----------------------------------------

        public async Task<AdminResult> SetGodAsync(string playerSearch, bool? on)
        {
            var user = await FindOnlineAsync(playerSearch);
            if (user == null)
            {
                return AdminResult.Fail($"Player not online: {playerSearch}");
            }

            var id = user.SteamId.m_SteamID;
            var state = on ?? !m_God.IsGod(id);
            m_God.Set(id, state);
            return AdminResult.Done(state
                ? $"Godmode ON for {user.DisplayName}."
                : $"Godmode OFF for {user.DisplayName}.");
        }

        public async Task<AdminResult> KickAsync(string playerSearch, string? reason)
        {
            var user = await FindOnlineAsync(playerSearch);
            if (user?.Session == null)
            {
                return AdminResult.Fail($"Player not online: {playerSearch}");
            }

            await user.Session.DisconnectAsync(reason ?? "Kicked by an admin.");
            return AdminResult.Done($"Kicked {user.DisplayName}.");
        }

        public async Task<AdminResult> BanAsync(string playerSearch, string? reason, int? minutes)
        {
            var user = await m_UserManager.FindUserAsync(KnownActorTypes.Player, playerSearch, UserSearchMode.FindByNameOrId)
                ?? await m_UserManager.FindUserAsync(KnownActorTypes.Player, playerSearch, UserSearchMode.FindById);
            if (user == null)
            {
                return AdminResult.Fail($"Player not found: {playerSearch}");
            }

            DateTime? endTime = minutes.HasValue && minutes.Value > 0
                ? DateTime.UtcNow.AddMinutes(minutes.Value)
                : (DateTime?)null;

            await m_UserManager.BanAsync(user, reason ?? "Banned by an admin.", endTime);
            var span = endTime.HasValue ? $"for {minutes} min" : "permanently";
            return AdminResult.Done($"Banned {user.DisplayName} {span}.");
        }

        public async Task<AdminResult> UnbanAsync(string steamId)
        {
            if (!ulong.TryParse(steamId, out var id))
            {
                return AdminResult.Fail($"Invalid SteamID: {steamId}");
            }

            await UniTask.SwitchToMainThread();
            var removed = SteamBlacklist.unban(new CSteamID(id));
            return removed ? AdminResult.Done($"Unbanned {steamId}.") : AdminResult.Fail($"{steamId} was not banned.");
        }

        // ----- roles ---------------------------------------------------------

        public Task<IReadOnlyCollection<IPermissionRole>> GetRolesAsync() => m_Roles.GetRolesAsync();

        public async Task<AdminResult> SetPlayerRoleAsync(string playerSearch, string roleId, bool add)
        {
            var role = await m_Roles.GetRoleAsync(roleId);
            if (role == null)
            {
                return AdminResult.Fail($"Role not found: {roleId}");
            }

            var actor = await ResolveActorAsync(playerSearch);
            if (actor == null)
            {
                return AdminResult.Fail($"Player not found: {playerSearch}");
            }

            if (add)
            {
                await m_Roles.AddRoleToActorAsync(actor, roleId);
                return AdminResult.Done($"Added role '{roleId}' to {actor.DisplayName}.");
            }

            await m_Roles.RemoveRoleFromActorAsync(actor, roleId);
            return AdminResult.Done($"Removed role '{roleId}' from {actor.DisplayName}.");
        }

        // ----- role command permissions -------------------------------------

        /// <summary>Finds registered commands matching a query: returns (id, permission, description).</summary>
        public async Task<IReadOnlyList<(string Id, string Permission, string Description)>> SearchCommandsAsync(string query)
        {
            var commands = await m_Commands.GetCommandsAsync();
            var result = new List<(string, string, string)>();
            foreach (var command in commands)
            {
                if (command.ParentId != null)
                {
                    // Skip sub-commands' duplicate listing noise; still searchable by id below.
                }

                var hit = string.IsNullOrEmpty(query)
                    || (command.Id?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (command.Name?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!hit)
                {
                    continue;
                }

                result.Add((command.Name ?? command.Id ?? string.Empty, m_CommandPermission.GetPermission(command) ?? string.Empty, command.Description ?? string.Empty));
                if (result.Count >= 100)
                {
                    break;
                }
            }

            return result;
        }

        public async Task<AdminResult> SetRoleCommandAsync(string roleId, string commandOrPermission, bool grant)
        {
            var roleData = await m_RolesData.GetRoleAsync(roleId);
            if (roleData == null)
            {
                return AdminResult.Fail($"Role not found: {roleId}");
            }

            var permission = await ResolveCommandPermissionAsync(commandOrPermission);
            if (permission == null)
            {
                return AdminResult.Fail($"Unknown command: {commandOrPermission}");
            }

            roleData.Permissions ??= new HashSet<string>();
            if (grant)
            {
                roleData.Permissions.Add(permission);
            }
            else
            {
                roleData.Permissions.Remove(permission);
            }

            await m_RolesData.SaveChangesAsync();
            return AdminResult.Done(grant
                ? $"Granted '{permission}' to role '{roleId}'."
                : $"Revoked '{permission}' from role '{roleId}'.");
        }

        public async Task<IReadOnlyList<string>> GetRolePermissionsAsync(string roleId)
        {
            var roleData = await m_RolesData.GetRoleAsync(roleId);
            return roleData?.Permissions?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        // ----- helpers -------------------------------------------------------

        public IReadOnlyList<UnturnedUser> OnlineUsers() => m_UserDirectory.GetOnlineUsers().ToList();

        public bool IsGod(ulong steamId) => m_God.IsGod(steamId);

        private async Task<UnturnedUser?> FindOnlineAsync(string search)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, search, UserSearchMode.FindByNameOrId) as UnturnedUser;

        private async Task<IPermissionActor?> ResolveActorAsync(string search)
        {
            var online = await FindOnlineAsync(search);
            if (online != null)
            {
                return online;
            }

            // Accept a bare 17-digit Steam ID so offline players can be assigned roles.
            if (search.Length == 17 && ulong.TryParse(search, out _))
            {
                return new PlayerPermissionActor(search);
            }

            return null;
        }

        /// <summary>Maps a command id/name (or a raw permission node) to its permission node.</summary>
        private async Task<string?> ResolveCommandPermissionAsync(string input)
        {
            var commands = await m_Commands.GetCommandsAsync();
            var match = commands.FirstOrDefault(c =>
                string.Equals(c.Id, input, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Name, input, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return m_CommandPermission.GetPermission(match);
            }

            // Already a permission node (e.g. "well404.shop:commands.buy").
            return input.Contains(":") ? input : null;
        }
    }
}
