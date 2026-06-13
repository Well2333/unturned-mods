using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.Core.Helpers;
using OpenMod.Unturned.Users;
using Steamworks;

namespace well404.Essentials.Party
{
    /// <summary>
    /// Tracks open party invites. A player (recipient) may hold several pending invites; each one
    /// auto-expires after a configurable lifetime (the inviter is notified). Accepting or denying
    /// cancels the expiry. Same shape as <see cref="Tp.TeleportRequestManager"/> but kept separate
    /// so a tp request and a party invite from the same player don't collide. Plugin-scoped singleton.
    /// </summary>
    public sealed class PartyInviteManager
    {
        private sealed class Pending
        {
            public ulong Inviter { get; set; }
            public CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();
        }

        private readonly object m_Lock = new object();
        private readonly Dictionary<ulong, List<Pending>> m_Invites = new Dictionary<ulong, List<Pending>>();

        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<PartyInviteManager> m_Logger;

        public PartyInviteManager(
            IUnturnedUserDirectory userDirectory,
            IStringLocalizer stringLocalizer,
            ILogger<PartyInviteManager> logger)
        {
            m_UserDirectory = userDirectory;
            m_StringLocalizer = stringLocalizer;
            m_Logger = logger;
        }

        /// <summary>Opens an invite. Returns false if the same inviter already has one pending to this recipient.</summary>
        public bool Open(ulong inviter, ulong recipient, int lifetimeMs)
        {
            lock (m_Lock)
            {
                if (!m_Invites.TryGetValue(recipient, out var list))
                {
                    list = new List<Pending>();
                    m_Invites[recipient] = list;
                }

                if (list.Exists(p => p.Inviter == inviter))
                {
                    return false;
                }

                var pending = new Pending { Inviter = inviter };
                list.Add(pending);
                ScheduleExpiry(pending, inviter, recipient, lifetimeMs, pending.Cts.Token);
            }

            return true;
        }

        /// <summary>A snapshot of the inviter ids with a pending invite to this recipient (oldest first).</summary>
        public IReadOnlyList<ulong> PendingSenders(ulong recipient)
        {
            lock (m_Lock)
            {
                if (!m_Invites.TryGetValue(recipient, out var list) || list.Count == 0)
                {
                    return Array.Empty<ulong>();
                }

                var ids = new ulong[list.Count];
                for (var i = 0; i < list.Count; i++)
                {
                    ids[i] = list[i].Inviter;
                }

                return ids;
            }
        }

        /// <summary>Removes and returns the oldest inviter for this recipient, or null if none.</summary>
        public ulong? TakeEarliest(ulong recipient)
        {
            lock (m_Lock)
            {
                if (!m_Invites.TryGetValue(recipient, out var list) || list.Count == 0)
                {
                    return null;
                }

                var pending = list[0];
                list.RemoveAt(0);
                pending.Cts.Cancel();
                if (list.Count == 0)
                {
                    m_Invites.Remove(recipient);
                }

                return pending.Inviter;
            }
        }

        /// <summary>Removes a specific inviter's invite for this recipient. Returns whether one was removed.</summary>
        public bool Take(ulong recipient, ulong inviter)
        {
            lock (m_Lock)
            {
                if (!m_Invites.TryGetValue(recipient, out var list))
                {
                    return false;
                }

                var index = list.FindIndex(p => p.Inviter == inviter);
                if (index < 0)
                {
                    return false;
                }

                list[index].Cts.Cancel();
                list.RemoveAt(index);
                if (list.Count == 0)
                {
                    m_Invites.Remove(recipient);
                }

                return true;
            }
        }

        private void ScheduleExpiry(Pending pending, ulong inviter, ulong recipient, int lifetimeMs, CancellationToken token)
        {
            AsyncHelper.Schedule("essentials:party-invite-expire", async () =>
            {
                try
                {
                    await Task.Delay(lifetimeMs, token);
                }
                catch (OperationCanceledException)
                {
                    return; // accepted or denied before expiry.
                }

                bool removed;
                lock (m_Lock)
                {
                    removed = RemoveLocked(recipient, pending);
                }

                if (!removed)
                {
                    return;
                }

                await NotifyExpiredAsync(inviter, recipient);
            }, ex => m_Logger.LogWarning(ex, "Party invite expiry failed."));
        }

        private bool RemoveLocked(ulong recipient, Pending pending)
        {
            if (!m_Invites.TryGetValue(recipient, out var list))
            {
                return false;
            }

            if (!list.Remove(pending))
            {
                return false;
            }

            if (list.Count == 0)
            {
                m_Invites.Remove(recipient);
            }

            return true;
        }

        private async Task NotifyExpiredAsync(ulong inviter, ulong recipient)
        {
            await UniTask.SwitchToMainThread();
            var inviterUser = m_UserDirectory.FindUser(new CSteamID(inviter));
            if (inviterUser == null)
            {
                return;
            }

            var recipientUser = m_UserDirectory.FindUser(new CSteamID(recipient));
            var recipientName = recipientUser?.DisplayName ?? recipient.ToString();
            await inviterUser.PrintMessageAsync(
                m_StringLocalizer["party:invite_expired", new { player = recipientName }]);
        }
    }
}
