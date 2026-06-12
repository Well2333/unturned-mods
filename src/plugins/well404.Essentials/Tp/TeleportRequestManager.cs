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

namespace well404.Essentials.Tp
{
    /// <summary>
    /// Tracks open <c>/tp</c> requests between players who are not on the same team. A recipient
    /// may hold several pending requests; each one auto-expires after a configurable lifetime
    /// (the requester is notified). Accepting or denying cancels the expiry. Thread-safe;
    /// registered as a plugin-scoped singleton.
    /// </summary>
    public sealed class TeleportRequestManager
    {
        private sealed class Pending
        {
            public ulong Requester { get; set; }
            public CancellationTokenSource Cts { get; set; } = new CancellationTokenSource();
        }

        private readonly object m_Lock = new object();
        private readonly Dictionary<ulong, List<Pending>> m_Requests = new Dictionary<ulong, List<Pending>>();

        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<TeleportRequestManager> m_Logger;

        public TeleportRequestManager(
            IUnturnedUserDirectory userDirectory,
            IStringLocalizer stringLocalizer,
            ILogger<TeleportRequestManager> logger)
        {
            m_UserDirectory = userDirectory;
            m_StringLocalizer = stringLocalizer;
            m_Logger = logger;
        }

        /// <summary>Opens a request. Returns false if the same requester already has one pending to this recipient.</summary>
        public bool Open(ulong requester, ulong recipient, int lifetimeMs)
        {
            CancellationToken token;
            lock (m_Lock)
            {
                if (!m_Requests.TryGetValue(recipient, out var list))
                {
                    list = new List<Pending>();
                    m_Requests[recipient] = list;
                }

                if (list.Exists(p => p.Requester == requester))
                {
                    return false;
                }

                var pending = new Pending { Requester = requester };
                list.Add(pending);
                token = pending.Cts.Token;
                ScheduleExpiry(pending, requester, recipient, lifetimeMs, token);
            }

            return true;
        }

        public bool HasAny(ulong recipient)
        {
            lock (m_Lock)
            {
                return m_Requests.TryGetValue(recipient, out var list) && list.Count > 0;
            }
        }

        /// <summary>Removes and returns the oldest requester for this recipient, or null if none.</summary>
        public ulong? TakeEarliest(ulong recipient)
        {
            lock (m_Lock)
            {
                if (!m_Requests.TryGetValue(recipient, out var list) || list.Count == 0)
                {
                    return null;
                }

                var pending = list[0];
                list.RemoveAt(0);
                pending.Cts.Cancel();
                if (list.Count == 0)
                {
                    m_Requests.Remove(recipient);
                }

                return pending.Requester;
            }
        }

        /// <summary>Removes a specific requester's request for this recipient. Returns whether one was removed.</summary>
        public bool Take(ulong recipient, ulong requester)
        {
            lock (m_Lock)
            {
                if (!m_Requests.TryGetValue(recipient, out var list))
                {
                    return false;
                }

                var index = list.FindIndex(p => p.Requester == requester);
                if (index < 0)
                {
                    return false;
                }

                list[index].Cts.Cancel();
                list.RemoveAt(index);
                if (list.Count == 0)
                {
                    m_Requests.Remove(recipient);
                }

                return true;
            }
        }

        private void ScheduleExpiry(Pending pending, ulong requester, ulong recipient, int lifetimeMs, CancellationToken token)
        {
            AsyncHelper.Schedule("essentials:tpa-expire", async () =>
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
                    // Remove only this exact request — never a newer one the player may have
                    // re-opened to the same recipient in the meantime.
                    removed = RemoveLocked(recipient, pending);
                }

                if (!removed)
                {
                    return;
                }

                await NotifyExpiredAsync(requester, recipient);
            }, ex => m_Logger.LogWarning(ex, "Teleport request expiry failed."));
        }

        private bool RemoveLocked(ulong recipient, Pending pending)
        {
            if (!m_Requests.TryGetValue(recipient, out var list))
            {
                return false;
            }

            if (!list.Remove(pending))
            {
                return false;
            }

            if (list.Count == 0)
            {
                m_Requests.Remove(recipient);
            }

            return true;
        }

        private async Task NotifyExpiredAsync(ulong requester, ulong recipient)
        {
            await UniTask.SwitchToMainThread();
            var requesterUser = m_UserDirectory.FindUser(new CSteamID(requester));
            if (requesterUser == null)
            {
                return;
            }

            var recipientUser = m_UserDirectory.FindUser(new CSteamID(recipient));
            var recipientName = recipientUser?.DisplayName ?? recipient.ToString();
            await requesterUser.PrintMessageAsync(
                m_StringLocalizer["tp:request_expired_sender", new { player = recipientName }]);
        }
    }
}
