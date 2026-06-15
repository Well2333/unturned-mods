using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using OpenMod.Unturned.Users;
using SDG.Unturned;
using Steamworks;

namespace well404.Essentials.Party
{
    public enum PartyJoinStatus
    {
        Joined,
        Full,
        Failed
    }

    public enum PartyKickStatus
    {
        Kicked,
        NotInParty,
        NotLeader,
        TargetNotInParty,
        CannotKickSelf
    }

    /// <summary>A party member's Steam ID, display name and whether they lead the party.</summary>
    public sealed class PartyMember
    {
        public PartyMember(ulong steamId, string displayName, bool isLeader)
        {
            SteamId = steamId;
            DisplayName = displayName;
            IsLeader = isLeader;
        }

        public ulong SteamId { get; }
        public string DisplayName { get; }
        public bool IsLeader { get; }
    }

    /// <summary>
    /// Server-side party (Unturned group) management, bypassing the in-game group menu. Uses
    /// <see cref="PlayerQuests.ServerAssignToGroup"/> to place players into a shared group
    /// (creating one via <see cref="GroupManager"/> when needed). All methods touch Unturned
    /// state and must be called on the main thread. Plugin-scoped singleton.
    /// </summary>
    public sealed class PartyService
    {
        private readonly IConfiguration m_Configuration;
        private readonly IUnturnedUserDirectory m_UserDirectory;

        public PartyService(IConfiguration configuration, IUnturnedUserDirectory userDirectory)
        {
            m_Configuration = configuration;
            m_UserDirectory = userDirectory;
        }

        private int MaxMembers =>
            (m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings()).Party.MaxMembers;

        public bool IsInParty(UnturnedUser user) => GroupExists(user.Player.Player.quests.groupID);

        /// <summary>True when the user is in a party and holds its leader (ADMIN) rank.</summary>
        public bool IsLeader(UnturnedUser user)
        {
            var quests = user.Player.Player.quests;
            return GroupExists(quests.groupID) && quests.groupRank == EPlayerGroupRank.ADMIN;
        }

        public bool SameParty(UnturnedUser a, UnturnedUser b)
        {
            var group = a.Player.Player.quests.groupID;
            return GroupExists(group) && b.Player.Player.quests.groupID == group;
        }

        /// <summary>Adds <paramref name="target"/> to <paramref name="inviter"/>'s party, creating one if needed.</summary>
        public PartyJoinStatus JoinViaInvite(UnturnedUser inviter, UnturnedUser target)
        {
            var inviterQuests = inviter.Player.Player.quests;
            var groupId = inviterQuests.groupID;

            if (!GroupExists(groupId))
            {
                // The inviter has no party yet — create one and make them its leader.
                groupId = GroupManager.generateUniqueGroupID();
                GroupManager.addGroup(groupId, inviter.DisplayName + "'s party");
                inviterQuests.ServerAssignToGroup(groupId, EPlayerGroupRank.ADMIN, bypassMemberLimit: true);
            }

            var max = MaxMembers;
            if (max > 0)
            {
                var info = GroupManager.getGroupInfo(groupId);
                if (info != null && info.members >= max)
                {
                    return PartyJoinStatus.Full;
                }
            }

            return target.Player.Player.quests.ServerAssignToGroup(groupId, EPlayerGroupRank.MEMBER, bypassMemberLimit: true)
                ? PartyJoinStatus.Joined
                : PartyJoinStatus.Failed;
        }

        /// <summary>Removes the user from their party. Returns false if they were not in one.</summary>
        public bool Leave(UnturnedUser user)
        {
            var quests = user.Player.Player.quests;
            if (!GroupExists(quests.groupID))
            {
                return false;
            }

            quests.leaveGroup(force: true);
            return true;
        }

        public PartyKickStatus Kick(UnturnedUser kicker, UnturnedUser target)
        {
            var kickerQuests = kicker.Player.Player.quests;
            if (!GroupExists(kickerQuests.groupID))
            {
                return PartyKickStatus.NotInParty;
            }

            if (kicker.SteamId.m_SteamID == target.SteamId.m_SteamID)
            {
                return PartyKickStatus.CannotKickSelf;
            }

            if (kickerQuests.groupRank != EPlayerGroupRank.ADMIN)
            {
                return PartyKickStatus.NotLeader;
            }

            if (target.Player.Player.quests.groupID != kickerQuests.groupID)
            {
                return PartyKickStatus.TargetNotInParty;
            }

            target.Player.Player.quests.leaveGroup(force: true);
            return PartyKickStatus.Kicked;
        }

        public string? GetPartyName(UnturnedUser user)
        {
            var groupId = user.Player.Player.quests.groupID;
            return GroupExists(groupId) ? GroupManager.getGroupInfo(groupId)?.name : null;
        }

        /// <summary>Online members of the user's party (empty if they have no party).</summary>
        public IReadOnlyList<PartyMember> GetMembers(UnturnedUser user)
        {
            var result = new List<PartyMember>();
            var groupId = user.Player.Player.quests.groupID;
            if (!GroupExists(groupId))
            {
                return result;
            }

            foreach (var online in m_UserDirectory.GetOnlineUsers())
            {
                var quests = online.Player.Player.quests;
                if (quests.groupID == groupId)
                {
                    result.Add(new PartyMember(online.SteamId.m_SteamID, online.DisplayName, quests.groupRank == EPlayerGroupRank.ADMIN));
                }
            }

            return result;
        }

        private static bool GroupExists(CSteamID groupId)
            => groupId != CSteamID.Nil && GroupManager.getGroupInfo(groupId) != null;
    }
}
