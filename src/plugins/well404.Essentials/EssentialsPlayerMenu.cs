using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using Steamworks;
using UnturnedMods.Shared.WebPanel;
using well404.Essentials.Data;
using well404.Essentials.Gift;
using well404.Essentials.Party;
using well404.Essentials.Teleport;
using well404.Essentials.Tp;
using well404.Essentials.Util;
using well404.Essentials.Warps;
using well404.Essentials.Sleep;

namespace well404.Essentials
{
    /// <summary>
    /// The player-facing "Utilities" surface for the web panel: home/back/warps, teleport requests
    /// (send and accept), party invites (send/accept/leave/members), gift claiming and sleep voting —
    /// all driven through the same services as the in-game commands. Web text is localized to the
    /// player's chosen language; in-game notices use the server's language.
    /// </summary>
    public sealed class EssentialsPlayerMenu : IPlayerMenu, IPlayerMenuUiProvider
    {
        public const string MenuId = "essentials";
        private const string WarpPrefix = "warp:";
        private static readonly WebUiExtension s_Ui = WebUiExtension.FromEmbeddedResources(
            typeof(EssentialsPlayerMenu).Assembly, "player-ui.html", "player-ui.css", "player-ui.js");

        private readonly PlayerDataStore m_PlayerData;
        private readonly WarpService m_Warps;
        private readonly TeleportService m_Teleport;
        private readonly TeleportRequestManager m_Requests;
        private readonly PartyInviteManager m_Invites;
        private readonly PartyService m_Party;
        private readonly GiftService m_Gifts;
        private readonly SleepVoteService m_Sleep;
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly IConfiguration m_Configuration;
        private readonly IWebTranslationRegistry m_Tr;
        private readonly IStringLocalizer m_StringLocalizer;

        public EssentialsPlayerMenu(
            PlayerDataStore playerData, WarpService warps, TeleportService teleport,
            TeleportRequestManager requests, PartyInviteManager invites, PartyService party,
            GiftService gifts, SleepVoteService sleep, IUnturnedUserDirectory userDirectory,
            IConfiguration configuration, IWebTranslationRegistry translations, IStringLocalizer stringLocalizer)
        {
            m_PlayerData = playerData;
            m_Warps = warps;
            m_Teleport = teleport;
            m_Requests = requests;
            m_Invites = invites;
            m_Party = party;
            m_Gifts = gifts;
            m_Sleep = sleep;
            m_UserDirectory = userDirectory;
            m_Configuration = configuration;
            m_Tr = translations;
            m_StringLocalizer = stringLocalizer;
        }

        public string Id => MenuId;

        public string Title => "Utilities";

        public string? Icon => "🧭";

        public WebUiExtension Ui => s_Ui;

        private string L(string lang, string key) => m_Tr.Resolve(key, lang);

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var lang = context.Language;
            await UniTask.SwitchToMainThread();
            var me = m_UserDirectory.FindUser(new CSteamID(ulong.Parse(context.SteamId)));
            if (me == null)
            {
                return new PlayerMenuView(L(lang, "Utilities"), null, Array.Empty<PlayerCard>(),
                    L(lang, "You must be online to use these tools."));
            }

            var meId = me.SteamId.m_SteamID;
            var cards = new List<PlayerCard>();

            // --- home / back / warps ---
            var home = await m_PlayerData.GetHomeAsync(context.SteamId);
            var homeButtons = new List<PlayerButton>();
            if (home != null)
            {
                homeButtons.Add(new PlayerButton("go", L(lang, "Go home"), "primary"));
            }

            homeButtons.Add(new PlayerButton("sethome", L(lang, "Set home here")));
            cards.Add(new PlayerCard("home", "🏠 " + L(lang, "Home"),
                home != null ? null : new[] { L(lang, "No home set yet — tap “Set home here”.") },
                null, homeButtons.ToArray()));

            var death = await m_PlayerData.GetLastDeathAsync(context.SteamId);
            cards.Add(death != null
                ? new PlayerCard("back", "↩ " + L(lang, "Back to death point"), null, null, new[] { new PlayerButton("go", L(lang, "Return")) })
                : new PlayerCard("back", "↩ " + L(lang, "Back to death point"), new[] { L(lang, "No death point yet — it appears after you die.") }));

            // Warps the player may use; show a hint card when they have access to none, so the
            // feature stays visible instead of the section silently disappearing.
            var warpCount = 0;
            foreach (var warp in m_Warps.All)
            {
                if (await m_Warps.HasAccessAsync(me, warp.Name))
                {
                    warpCount++;
                    cards.Add(new PlayerCard(WarpPrefix + warp.Name, "📍 " + warp.Name, null, null, new[] { new PlayerButton("go", L(lang, "Teleport")) }));
                }
            }

            if (warpCount == 0)
            {
                cards.Add(new PlayerCard("warps", "📍 " + L(lang, "Warps"), new[] { L(lang, "No warps are available to you right now.") }));
            }

            // --- incoming teleport requests ---
            foreach (var requesterId in m_Requests.PendingSenders(meId))
            {
                var name = NameOf(requesterId);
                cards.Add(new PlayerCard("tpreq:" + requesterId, m_Tr.Format(lang, "Teleport request from {0}", name), null, null, new[]
                {
                    new PlayerButton("tpaccept", L(lang, "Accept"), "primary"),
                    new PlayerButton("tpdeny", L(lang, "Deny"), "danger")
                }));
            }

            // --- incoming party invites ---
            foreach (var inviterId in m_Invites.PendingSenders(meId))
            {
                var name = NameOf(inviterId);
                cards.Add(new PlayerCard("pinv:" + inviterId, m_Tr.Format(lang, "Party invite from {0}", name), null, null, new[]
                {
                    new PlayerButton("pacc", L(lang, "Accept"), "primary"),
                    new PlayerButton("pden", L(lang, "Deny"), "danger")
                }));
            }

            // --- my party (summary + per-member kick when I'm the leader) ---
            if (m_Party.IsInParty(me))
            {
                var members = m_Party.GetMembers(me);
                var lines = new List<string>();
                foreach (var member in members)
                {
                    lines.Add(member.IsLeader ? m_Tr.Format(lang, "{0} (leader)", member.DisplayName) : member.DisplayName);
                }

                cards.Add(new PlayerCard("party", "👥 " + (m_Party.GetPartyName(me) ?? L(lang, "Party")), lines, null,
                    new[] { new PlayerButton("pleave", L(lang, "Leave party"), "danger") }));

                if (m_Party.IsLeader(me))
                {
                    foreach (var member in members)
                    {
                        if (member.SteamId == meId)
                        {
                            continue;
                        }

                        cards.Add(new PlayerCard("pmember:" + member.SteamId, "👥 " + member.DisplayName, null, null,
                            new[] { new PlayerButton("pkick", L(lang, "Kick from party"), "danger") }));
                    }
                }
            }
            else
            {
                cards.Add(new PlayerCard("party", "👥 " + L(lang, "Party"),
                    new[] { L(lang, "You're not in a party yet — invite an online player below to start one.") }));
            }

            // --- other online players: request teleport / invite to party ---
            var otherCount = 0;
            foreach (var other in m_UserDirectory.GetOnlineUsers())
            {
                if (other.SteamId.m_SteamID == meId)
                {
                    continue;
                }

                otherCount++;
                cards.Add(new PlayerCard("p:" + other.SteamId.m_SteamID, "👤 " + other.DisplayName, null, null, new[]
                {
                    new PlayerButton("tpreq", L(lang, me.Player.Player.quests.isMemberOfSameGroupAs(other.Player.Player)
                        ? "Teleport" : "Request teleport")),
                    new PlayerButton("pinvite", L(lang, "Invite to party"))
                }));
            }

            if (otherCount == 0)
            {
                cards.Add(new PlayerCard("noplayers", "👤 " + L(lang, "Other players"),
                    new[] { L(lang, "No other players are online right now.") }));
            }

            // --- gifts ---
            var giftCount = 0;
            foreach (var gift in await m_Gifts.GetListingsAsync(me))
            {
                giftCount++;
                if (gift.Ready)
                {
                    cards.Add(new PlayerCard("gift:" + gift.Id, "🎁 " + gift.Name, null, null,
                        new[] { new PlayerButton("giftclaim", L(lang, "Claim"), "primary") }));
                }
                else
                {
                    cards.Add(new PlayerCard("gift:" + gift.Id, "🎁 " + gift.Name,
                        new[] { m_Tr.Format(lang, "Refreshes in {0}", Duration((int)Math.Ceiling(gift.RefreshInSeconds))) }));
                }
            }

            if (giftCount == 0)
            {
                cards.Add(new PlayerCard("gifts", "🎁 " + L(lang, "Gifts"), new[] { L(lang, "No gift packs are available to you right now.") }));
            }

            // --- sleep vote ---
            cards.Add(new PlayerCard("sleep", "😴 " + L(lang, "Sleep vote"), null, null,
                new[] { new PlayerButton("sleepvote", L(lang, "Vote to sleep"), "primary") }));

            return new PlayerMenuView(L(lang, "Utilities"), L(lang, "Tap to use; some teleports need you to stand still briefly."), cards);
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var lang = context.Language;
            await UniTask.SwitchToMainThread();
            var me = m_UserDirectory.FindUser(new CSteamID(ulong.Parse(context.SteamId)));
            if (me == null)
            {
                return PlayerActionResult.Fail(L(lang, "You must be online to use these tools."));
            }

            var meId = me.SteamId.m_SteamID;
            var settings = m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings();

            switch (actionId)
            {
                case "go":
                    return await TeleportToAsync(me, cardKey, lang);

                case "sethome":
                {
                    var location = LocationHelper.FromPlayer(me.Player);
                    await m_PlayerData.SetHomeAsync(me.Id, location);
                    return PlayerActionResult.Ok(L(lang, "Home set to your current location."));
                }

                case "tpreq":
                {
                    var targetId = ParseId(cardKey, "p:");
                    var target = m_UserDirectory.FindUser(new CSteamID(targetId));
                    if (target == null) return PlayerActionResult.Fail(L(lang, "That player is no longer online."));

                    if (me.Player.Player.quests.isMemberOfSameGroupAs(target.Player.Player))
                    {
                        var destination = LocationHelper.FromPlayer(target.Player);
                        return await m_Teleport.TryTeleportAsync(me, destination, TeleportKind.Tp, "tp")
                            ? PlayerActionResult.Ok(m_Tr.Format(lang, "Teleported to {0}.", target.DisplayName))
                            : PlayerActionResult.Fail(L(lang, "Teleport didn't complete — check the in-game notice."));
                    }

                    if (!m_Requests.Open(meId, targetId, settings.Tpa.ExpirationSeconds * 1000))
                        return PlayerActionResult.Fail(m_Tr.Format(lang, "You already have a teleport request to {0}.", target.DisplayName));
                    await Notify(target, "tp:request_received", new { player = me.DisplayName });
                    return PlayerActionResult.Ok(m_Tr.Format(lang, "Teleport request sent to {0}.", target.DisplayName));
                }

                case "tpaccept":
                {
                    var requesterId = ParseId(cardKey, "tpreq:");
                    if (!m_Requests.Take(meId, requesterId)) return PlayerActionResult.Fail(L(lang, "That request is no longer pending."));
                    var requester = m_UserDirectory.FindUser(new CSteamID(requesterId));
                    if (requester == null) return PlayerActionResult.Fail(L(lang, "That player is no longer online."));
                    var destination = LocationHelper.FromPlayer(me.Player);
                    await Notify(requester, "tpa:accepted_other", new { player = me.DisplayName });
                    await m_Teleport.TryTeleportAsync(requester, destination, TeleportKind.Tp, "tp");
                    return PlayerActionResult.Ok(m_Tr.Format(lang, "Accepted {0}'s teleport request.", requester.DisplayName));
                }

                case "tpdeny":
                {
                    var requesterId = ParseId(cardKey, "tpreq:");
                    m_Requests.Take(meId, requesterId);
                    return PlayerActionResult.Ok(L(lang, "Teleport request denied."));
                }

                case "pinvite":
                {
                    var targetId = ParseId(cardKey, "p:");
                    var target = m_UserDirectory.FindUser(new CSteamID(targetId));
                    if (target == null) return PlayerActionResult.Fail(L(lang, "That player is no longer online."));
                    if (m_Party.SameParty(me, target)) return PlayerActionResult.Fail(m_Tr.Format(lang, "{0} is already in your party.", target.DisplayName));
                    if (!m_Invites.Open(meId, targetId, settings.Party.InviteExpirationSeconds * 1000))
                        return PlayerActionResult.Fail(m_Tr.Format(lang, "You already invited {0}.", target.DisplayName));
                    await Notify(target, "party:invite_received", new { player = me.DisplayName, seconds = settings.Party.InviteExpirationSeconds });
                    return PlayerActionResult.Ok(m_Tr.Format(lang, "Party invite sent to {0}.", target.DisplayName));
                }

                case "pacc":
                {
                    var inviterId = ParseId(cardKey, "pinv:");
                    if (!m_Invites.Take(meId, inviterId)) return PlayerActionResult.Fail(L(lang, "That invite is no longer pending."));
                    var inviter = m_UserDirectory.FindUser(new CSteamID(inviterId));
                    if (inviter == null) return PlayerActionResult.Fail(L(lang, "That player is no longer online."));
                    var status = m_Party.JoinViaInvite(inviter, me);
                    if (status == PartyJoinStatus.Full) return PlayerActionResult.Fail(L(lang, "That party is full."));
                    if (status == PartyJoinStatus.Failed) return PlayerActionResult.Fail(L(lang, "Could not join the party."));
                    await Notify(inviter, "party:joined_other", new { player = me.DisplayName });
                    return PlayerActionResult.Ok(m_Tr.Format(lang, "Joined {0}'s party.", inviter.DisplayName));
                }

                case "pden":
                {
                    var inviterId = ParseId(cardKey, "pinv:");
                    m_Invites.Take(meId, inviterId);
                    return PlayerActionResult.Ok(L(lang, "Party invite denied."));
                }

                case "pleave":
                    return PlayerActionResult.Ok(m_Party.Leave(me) ? L(lang, "You left the party.") : L(lang, "You are not in a party."));

                case "pkick":
                {
                    var targetId = ParseId(cardKey, "pmember:");
                    var target = m_UserDirectory.FindUser(new CSteamID(targetId));
                    if (target == null) return PlayerActionResult.Fail(L(lang, "That player is no longer online."));
                    switch (m_Party.Kick(me, target))
                    {
                        case PartyKickStatus.Kicked: return PlayerActionResult.Ok(m_Tr.Format(lang, "Kicked {0} from the party.", target.DisplayName));
                        case PartyKickStatus.NotLeader: return PlayerActionResult.Fail(L(lang, "Only the party leader can kick members."));
                        case PartyKickStatus.NotInParty: return PlayerActionResult.Fail(L(lang, "You are not in a party."));
                        case PartyKickStatus.CannotKickSelf: return PlayerActionResult.Fail(L(lang, "You can't kick yourself."));
                        default: return PlayerActionResult.Fail(L(lang, "That player is not in your party."));
                    }
                }

                case "giftclaim":
                {
                    var giftId = cardKey.StartsWith("gift:", StringComparison.Ordinal) ? cardKey.Substring(5) : cardKey;
                    var result = await m_Gifts.ClaimAsync(me, giftId);
                    switch (result.Status)
                    {
                        case GiftClaimStatus.Claimed: return PlayerActionResult.Ok(m_Tr.Format(lang, "Claimed {0}.", result.GiftName));
                        case GiftClaimStatus.NoPermission: return PlayerActionResult.Fail(L(lang, "You can't claim that gift."));
                        case GiftClaimStatus.OnCooldown: return PlayerActionResult.Fail(m_Tr.Format(lang, "Not ready yet — refreshes in {0}.", Duration((int)Math.Ceiling(result.RefreshInSeconds))));
                        default: return PlayerActionResult.Fail(L(lang, "Gift not found."));
                    }
                }

                case "sleepvote":
                {
                    var outcome = await m_Sleep.VoteAsync(me);
                    switch (outcome)
                    {
                        case SleepVoteOutcome.Disabled: return PlayerActionResult.Fail(L(lang, "Sleep voting is disabled."));
                        case SleepVoteOutcome.AlreadyVoted: return PlayerActionResult.Fail(L(lang, "You already voted to sleep."));
                        case SleepVoteOutcome.Passed: return PlayerActionResult.Ok(L(lang, "The vote passed — time changed."));
                        default: return PlayerActionResult.Ok(L(lang, "Your sleep vote was counted."));
                    }
                }

                default:
                    return PlayerActionResult.Fail(L(lang, "Unknown action."));
            }
        }

        private async Task<PlayerActionResult> TeleportToAsync(UnturnedUser me, string cardKey, string lang)
        {
            PlayerLocation destination;
            TeleportKind kind;
            string cooldownKey;
            int? cooldownOverride = null;

            if (cardKey == "home")
            {
                var home = await m_PlayerData.GetHomeAsync(me.Id);
                if (home == null) return PlayerActionResult.Fail(L(lang, "You haven't set a home yet."));
                destination = home; kind = TeleportKind.Home; cooldownKey = "home";
            }
            else if (cardKey == "back")
            {
                var d = await m_PlayerData.GetLastDeathAsync(me.Id);
                if (d == null) return PlayerActionResult.Fail(L(lang, "No death point to return to."));
                destination = d; kind = TeleportKind.Back; cooldownKey = "back";
            }
            else if (cardKey.StartsWith(WarpPrefix, StringComparison.Ordinal))
            {
                var name = cardKey.Substring(WarpPrefix.Length);
                var warp = m_Warps.Find(name);
                if (warp == null) return PlayerActionResult.Fail(L(lang, "Warp not found."));
                if (!await m_Warps.HasAccessAsync(me, warp.Name)) return PlayerActionResult.Fail(L(lang, "You don't have access to that warp."));
                destination = WarpService.ToLocation(warp); kind = TeleportKind.Warp; cooldownKey = WarpPrefix + warp.Name.ToLowerInvariant(); cooldownOverride = warp.CooldownSeconds;
            }
            else
            {
                return PlayerActionResult.Fail(L(lang, "Unknown destination."));
            }

            var ok = await m_Teleport.TryTeleportAsync(me, destination, kind, cooldownKey, cooldownOverride);
            return ok ? PlayerActionResult.Ok(L(lang, "Teleported.")) : PlayerActionResult.Fail(L(lang, "Teleport didn't complete — check the in-game notice."), refresh: true);
        }

        private string NameOf(ulong steamId)
        {
            var user = m_UserDirectory.FindUser(new CSteamID(steamId));
            return user?.DisplayName ?? steamId.ToString();
        }

        private static ulong ParseId(string cardKey, string prefix)
        {
            var raw = cardKey.StartsWith(prefix, StringComparison.Ordinal) ? cardKey.Substring(prefix.Length) : cardKey;
            return ulong.TryParse(raw, out var id) ? id : 0;
        }

        private async Task Notify(UnturnedUser user, string key, object args)
        {
            try { await user.PrintMessageAsync(m_StringLocalizer[key, args]); }
            catch { /* best-effort in-game notice */ }
        }

        private static string Duration(int seconds)
        {
            if (seconds < 0) seconds = 0;
            if (seconds < 60) return seconds + "s";
            var m = seconds / 60;
            var s = seconds % 60;
            return s == 0 ? m + "m" : m + "m " + s + "s";
        }
    }
}
