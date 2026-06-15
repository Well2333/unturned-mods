using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using OpenMod.Unturned.Users;

namespace well404.Essentials.Sleep
{
    public enum SleepVoteOutcome
    {
        Disabled,
        AlreadyVoted,
        Counted,
        Passed
    }

    /// <summary>
    /// Minecraft-style sleep voting: when at least <c>requiredRatio</c> of the online players
    /// have typed <c>/sleep</c>, the world flips day↔night and votes reset. Progress and the
    /// flip are broadcast to everyone here; the command only reports the disabled / already-voted
    /// cases. Registered as a plugin-scoped singleton.
    /// </summary>
    public sealed class SleepVoteService
    {
        private readonly object m_Lock = new object();
        private readonly HashSet<ulong> m_Votes = new HashSet<ulong>();

        private readonly IConfiguration m_Configuration;
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly IStringLocalizer m_StringLocalizer;

        public SleepVoteService(
            IConfiguration configuration,
            IUnturnedUserDirectory userDirectory,
            IStringLocalizer stringLocalizer)
        {
            m_Configuration = configuration;
            m_UserDirectory = userDirectory;
            m_StringLocalizer = stringLocalizer;
        }

        private SleepSettings Settings =>
            (m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings()).Sleep;

        public async Task<SleepVoteOutcome> VoteAsync(UnturnedUser user)
        {
            var settings = Settings;
            if (!settings.Enabled)
            {
                return SleepVoteOutcome.Disabled;
            }

            await UniTask.SwitchToMainThread();

            var online = m_UserDirectory.GetOnlineUsers();
            var onlineIds = new HashSet<ulong>();
            foreach (var u in online)
            {
                onlineIds.Add(u.SteamId.m_SteamID);
            }

            var ratio = settings.RequiredRatio;
            if (ratio <= 0m)
            {
                ratio = 0.5m;
            }
            else if (ratio > 1m)
            {
                ratio = 1m;
            }

            var voterId = user.SteamId.m_SteamID;
            int current;
            int required;
            bool passed;

            lock (m_Lock)
            {
                // Drop votes from players who have since left, so the threshold reflects who is here.
                m_Votes.IntersectWith(onlineIds);

                if (m_Votes.Contains(voterId))
                {
                    return SleepVoteOutcome.AlreadyVoted;
                }

                m_Votes.Add(voterId);
                current = m_Votes.Count;
                required = (int)Math.Ceiling((double)ratio * Math.Max(1, onlineIds.Count));
                if (required < 1)
                {
                    required = 1;
                }

                passed = current >= required;
                if (passed)
                {
                    m_Votes.Clear();
                }
            }

            if (!passed)
            {
                await BroadcastAsync(online,
                    m_StringLocalizer["sleep:progress", new { player = user.DisplayName, current, required }]);
                return SleepVoteOutcome.Counted;
            }

            var isDay = DayNightController.Toggle();
            await BroadcastAsync(online, m_StringLocalizer[isDay ? "sleep:passed_day" : "sleep:passed_night"]);
            return SleepVoteOutcome.Passed;
        }

        private static async Task BroadcastAsync(IEnumerable<UnturnedUser> users, string message)
        {
            foreach (var user in users)
            {
                await user.PrintMessageAsync(message);
            }
        }
    }
}
