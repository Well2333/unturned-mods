using System.Threading.Tasks;
using OpenMod.API.Ioc;

namespace UnturnedMods.Shared.Teams
{
    public enum TeamLookupStatus
    {
        Unavailable,
        Offline,
        OnlineNoTeam,
        InTeam
    }

    public enum TeamRole
    {
        Member,
        Leader
    }

    public sealed class TeamContext
    {
        public TeamContext(string providerId, string scopeId, string teamId, string displayName, TeamRole role)
        {
            ProviderId = providerId;
            ScopeId = scopeId;
            TeamId = teamId;
            DisplayName = displayName;
            Role = role;
        }

        public string ProviderId { get; }
        public string ScopeId { get; }
        public string TeamId { get; }
        public string DisplayName { get; }
        public TeamRole Role { get; }
        public string Key => ProviderId + ":" + ScopeId + ":" + TeamId;
    }

    public sealed class TeamLookupResult
    {
        private TeamLookupResult(TeamLookupStatus status, TeamContext? context)
        {
            Status = status;
            Context = context;
        }

        public TeamLookupStatus Status { get; }
        public TeamContext? Context { get; }

        public static TeamLookupResult Unavailable() => new TeamLookupResult(TeamLookupStatus.Unavailable, null);
        public static TeamLookupResult Offline() => new TeamLookupResult(TeamLookupStatus.Offline, null);
        public static TeamLookupResult OnlineNoTeam() => new TeamLookupResult(TeamLookupStatus.OnlineNoTeam, null);
        public static TeamLookupResult InTeam(TeamContext context) => new TeamLookupResult(TeamLookupStatus.InTeam, context);
    }

    [Service]
    public interface ITeamContextProvider
    {
        Task<TeamLookupResult> GetCurrentTeamAsync(string playerId, string playerType);
    }
}
