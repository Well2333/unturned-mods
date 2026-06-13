using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using SDG.Unturned;
using Steamworks;

namespace well404.AdminTools
{
    /// <summary>
    /// The result of an admin action: a success flag plus a message <b>template</b> and its
    /// arguments. The template is an English source string used as an i18n key, so the web panel
    /// can localize it to the admin's language (<see cref="WebText.Format"/>); <see cref="Message"/>
    /// is the English-formatted fallback used in-game.
    /// </summary>
    public sealed class AdminResult
    {
        public AdminResult(bool ok, string key, object[] args)
        {
            Ok = ok;
            Key = key;
            Args = args ?? Array.Empty<object>();
        }

        public bool Ok { get; }

        /// <summary>English message template; doubles as the i18n key for localized panels.</summary>
        public string Key { get; }

        public object[] Args { get; }

        /// <summary>English-formatted message (in-game fallback when not localizing).</summary>
        public string Message
        {
            get
            {
                if (Args.Length == 0)
                {
                    return Key;
                }

                try { return string.Format(Key, Args); }
                catch (FormatException) { return Key; }
            }
        }

        public static AdminResult Fail(string key, params object[] args) => new AdminResult(false, key, args);
        public static AdminResult Done(string key, params object[] args) => new AdminResult(true, key, args);
    }

    /// <summary>
    /// The moderation operations behind both the commands and the web panel: godmode, kick, and
    /// temporary ban / unban. Messages are English templates (the source language); the web panel
    /// localizes them per request.
    /// </summary>
    public sealed class AdminToolsService
    {
        private readonly ILifetimeScope m_Scope;
        private readonly GodModeService m_God;

        // OpenMod user services are resolved on demand from the plugin scope: some are not resolvable
        // when a plugin-scoped singleton is first constructed, so capturing them in the constructor
        // would fail plugin load. Lazy resolution keeps the plugin loadable.
        public AdminToolsService(ILifetimeScope scope, GodModeService god)
        {
            m_Scope = scope;
            m_God = god;
        }

        private IUserManager m_UserManager => m_Scope.Resolve<IUserManager>();
        private IUnturnedUserDirectory m_UserDirectory => m_Scope.Resolve<IUnturnedUserDirectory>();

        // ----- godmode / kick / ban -----------------------------------------

        public async Task<AdminResult> SetGodAsync(string playerSearch, bool? on)
        {
            var user = await FindOnlineAsync(playerSearch);
            if (user == null)
            {
                return AdminResult.Fail("Player not online: {0}", playerSearch);
            }

            var id = user.SteamId.m_SteamID;
            var state = on ?? !m_God.IsGod(id);
            m_God.Set(id, state);
            return state
                ? AdminResult.Done("Godmode ON for {0}.", user.DisplayName)
                : AdminResult.Done("Godmode OFF for {0}.", user.DisplayName);
        }

        public async Task<AdminResult> KickAsync(string playerSearch, string? reason)
        {
            var user = await FindOnlineAsync(playerSearch);
            if (user?.Session == null)
            {
                return AdminResult.Fail("Player not online: {0}", playerSearch);
            }

            await user.Session.DisconnectAsync(reason ?? "Kicked by an admin.");
            return AdminResult.Done("Kicked {0}.", user.DisplayName);
        }

        public async Task<AdminResult> BanAsync(string playerSearch, string? reason, int? minutes)
        {
            var user = await m_UserManager.FindUserAsync(KnownActorTypes.Player, playerSearch, UserSearchMode.FindByNameOrId)
                ?? await m_UserManager.FindUserAsync(KnownActorTypes.Player, playerSearch, UserSearchMode.FindById);
            if (user == null)
            {
                return AdminResult.Fail("Player not found: {0}", playerSearch);
            }

            DateTime? endTime = minutes.HasValue && minutes.Value > 0
                ? DateTime.UtcNow.AddMinutes(minutes.Value)
                : (DateTime?)null;

            await m_UserManager.BanAsync(user, reason ?? "Banned by an admin.", endTime);
            return endTime.HasValue
                ? AdminResult.Done("Banned {0} for {1} min.", user.DisplayName, minutes!.Value)
                : AdminResult.Done("Banned {0} permanently.", user.DisplayName);
        }

        public async Task<AdminResult> UnbanAsync(string steamId)
        {
            if (!ulong.TryParse(steamId, out var id))
            {
                return AdminResult.Fail("Invalid SteamID: {0}", steamId);
            }

            await UniTask.SwitchToMainThread();
            var removed = SteamBlacklist.unban(new CSteamID(id));
            return removed ? AdminResult.Done("Unbanned {0}.", steamId) : AdminResult.Fail("{0} was not banned.", steamId);
        }

        // ----- helpers -------------------------------------------------------

        public IReadOnlyList<UnturnedUser> OnlineUsers() => m_UserDirectory.GetOnlineUsers().ToList();

        public bool IsGod(ulong steamId) => m_God.IsGod(steamId);

        private async Task<UnturnedUser?> FindOnlineAsync(string search)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, search, UserSearchMode.FindByNameOrId) as UnturnedUser;
    }
}
