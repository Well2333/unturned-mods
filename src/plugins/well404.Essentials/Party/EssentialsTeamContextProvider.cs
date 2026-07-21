using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Ioc;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using SDG.Unturned;
using Steamworks;
using UnturnedMods.Shared.Teams;

namespace well404.Essentials.Party
{
    /// <summary>
    /// Global, reload-safe bridge from Vault to Essentials' current Unturned party. Authorization
    /// is resolved from live server state on every operation; callers never submit a trusted team ID.
    /// </summary>
    [ServiceImplementation(Lifetime = ServiceLifetime.Singleton)]
    public sealed class EssentialsTeamContextProvider : ITeamContextProvider
    {
        private readonly IPluginAccessor<EssentialsPlugin> m_PluginAccessor;
        private readonly IUserManager m_UserManager;

        public EssentialsTeamContextProvider(
            IPluginAccessor<EssentialsPlugin> pluginAccessor,
            IUserManager userManager)
        {
            m_PluginAccessor = pluginAccessor;
            m_UserManager = userManager;
        }

        public async Task<TeamLookupResult> GetCurrentTeamAsync(string playerId, string playerType)
        {
            if (!string.Equals(playerType, KnownActorTypes.Player, StringComparison.OrdinalIgnoreCase))
            {
                return TeamLookupResult.Unavailable();
            }

            var plugin = m_PluginAccessor.Instance;
            if (plugin == null || !plugin.IsComponentAlive)
            {
                return TeamLookupResult.Unavailable();
            }

            var user = await m_UserManager.FindUserAsync(
                KnownActorTypes.Player, playerId, UserSearchMode.FindById) as UnturnedUser;
            if (user == null)
            {
                return TeamLookupResult.Offline();
            }

            await UniTask.SwitchToMainThread();
            var quests = user.Player.Player.quests;
            var groupId = quests.groupID;
            if (groupId == CSteamID.Nil)
            {
                return TeamLookupResult.OnlineNoTeam();
            }

            var info = GroupManager.getGroupInfo(groupId);
            if (info == null)
            {
                return TeamLookupResult.Unavailable();
            }

            var scope = string.IsNullOrWhiteSpace(Provider.serverID) ? "server" : Provider.serverID;
            return TeamLookupResult.InTeam(new TeamContext(
                "unturned",
                scope,
                groupId.m_SteamID.ToString(),
                string.IsNullOrWhiteSpace(info.name) ? groupId.m_SteamID.ToString() : info.name,
                quests.groupRank == EPlayerGroupRank.ADMIN ? TeamRole.Leader : TeamRole.Member));
        }
    }
}
