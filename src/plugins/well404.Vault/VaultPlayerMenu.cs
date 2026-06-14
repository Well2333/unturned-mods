using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.Vault
{
    /// <summary>
    /// The player-facing vault surface for the web panel: a compact list with two sections — the
    /// player's current backpack (a Store button per item) and the vault contents (Take / Details per
    /// item). Clicking <i>Details</i> on a stacked vault entry drills into a sub-view that lists each
    /// stored copy individually with its specifics (amount/rounds, durability, whether it carries
    /// state) so the player can withdraw a particular one. Driven by the generic player-menu model.
    /// </summary>
    public sealed class VaultPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "vault";

        private const string DetailPrefix = "uid:";

        private readonly VaultService m_Vault;
        private readonly IUserManager m_UserManager;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IWebTranslationRegistry m_Tr;

        // Per-player drill-down state: the item id whose per-copy detail view the player is viewing.
        private readonly ConcurrentDictionary<string, ushort> m_Detail = new ConcurrentDictionary<string, ushort>();

        public VaultPlayerMenu(VaultService vault, IUserManager userManager, IItemDirectory itemDirectory, IWebTranslationRegistry translations)
        {
            m_Vault = vault;
            m_UserManager = userManager;
            m_ItemDirectory = itemDirectory;
            m_Tr = translations;
        }

        public string Id => MenuId;

        public string Title => "Vault";

        public string? Icon => "🧳";

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var lang = context.Language;
            var steamId = context.SteamId;
            var user = await ResolveOnlineAsync(steamId);

            var max = user != null ? await m_Vault.GetMaxSlotsAsync(user) : m_Vault.OverrideOrBase(steamId);
            var header = m_Tr.Format(lang, "Vault: {0} / {1} slots", m_Vault.UsedSlots(steamId), max);
            // The vault contents are viewable even offline (read-only); only storing/withdrawing needs
            // a live inventory. So offline shows the stored items + a notice, not a blank page.
            var message = user == null ? m_Tr.Resolve("You must be online to store or withdraw.", lang) : null;
            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);

            // Drill-down: a per-copy detail view for one item id (pure navigation — works offline too).
            if (m_Detail.TryGetValue(steamId, out var detailId))
            {
                var copies = m_Vault.Get(steamId).Where(x => x.ItemId == detailId).ToList();
                if (copies.Count > 0)
                {
                    return new PlayerMenuView(m_Tr.Resolve("Vault", lang), header, BuildDetailCards(lang, detailId, copies, names), message, null, "list");
                }

                m_Detail.TryRemove(steamId, out _);   // nothing left to detail — fall back to the top view
            }

            return new PlayerMenuView(m_Tr.Resolve("Vault", lang), header, await BuildTopCardsAsync(lang, steamId, user, names), message, null, "list");
        }

        private async Task<List<PlayerCard>> BuildTopCardsAsync(string lang, string steamId, UnturnedUser? user, IReadOnlyDictionary<string, string> names)
        {
            var backpackGroup = m_Tr.Resolve("Backpack", lang);
            var vaultGroup = m_Tr.Resolve("Vault", lang);
            var cards = new List<PlayerCard>();

            // Backpack: a Store button per distinct item (prompt defaults to all you carry). Online only.
            var backpack = user != null ? await m_Vault.ListBackpackAsync(user) : (IReadOnlyList<BackpackEntry>)new List<BackpackEntry>();
            foreach (var entry in backpack.OrderBy(e => VaultNames.NameOf(e.ItemId, names)))
            {
                var count = entry.Count.ToString(CultureInfo.InvariantCulture);
                cards.Add(new PlayerCard(
                    entry.ItemId.ToString(CultureInfo.InvariantCulture),
                    VaultNames.NameOf(entry.ItemId, names),
                    null,
                    new[] { "×" + count },
                    new[] { new PlayerButton("store", m_Tr.Resolve("Store", lang), "primary", m_Tr.Resolve("Amount to store", lang), count) },
                    backpackGroup,
                    "#" + entry.ItemId.ToString(CultureInfo.InvariantCulture)));
            }

            // Vault: Take (prompt defaults to all of that id), plus Details when there is more than one copy.
            foreach (var group in m_Vault.Get(steamId).GroupBy(x => x.ItemId).OrderBy(g => VaultNames.NameOf(g.Key, names)))
            {
                var count = group.Count();
                var slots = group.Sum(x => x.SlotCost);
                var idStr = group.Key.ToString(CultureInfo.InvariantCulture);
                var buttons = new List<PlayerButton>
                {
                    new PlayerButton("take", m_Tr.Resolve("Take", lang), "success", m_Tr.Resolve("Amount to take", lang),
                        count.ToString(CultureInfo.InvariantCulture))
                };
                if (count > 1)
                {
                    buttons.Add(new PlayerButton("details", m_Tr.Resolve("Details", lang)));
                }

                cards.Add(new PlayerCard(
                    idStr,
                    VaultNames.NameOf(group.Key, names),
                    null,
                    new[] { "×" + count.ToString(CultureInfo.InvariantCulture), m_Tr.Format(lang, "{0} slots", slots) },
                    buttons,
                    vaultGroup,
                    "#" + idStr));
            }

            return cards;
        }

        private List<PlayerCard> BuildDetailCards(string lang, ushort itemId, List<StoredItem> copies, IReadOnlyDictionary<string, string> names)
        {
            var name = VaultNames.NameOf(itemId, names);
            var group = m_Tr.Format(lang, "{0} — copies", name);
            var cards = new List<PlayerCard>
            {
                // A back entry (just the button) to return to the top view.
                new PlayerCard("__back", string.Empty, null, null,
                    new[] { new PlayerButton("back", "← " + m_Tr.Resolve("Back", lang)) }, group)
            };

            var ordinal = 1;
            foreach (var copy in copies)
            {
                var tags = new List<string>();
                if (copy.Amount > 1)
                {
                    tags.Add("×" + copy.Amount.ToString(CultureInfo.InvariantCulture));
                }

                if (copy.Quality < 100)
                {
                    tags.Add(m_Tr.Format(lang, "Durability {0}%", copy.Quality));
                }

                if (!string.IsNullOrEmpty(copy.State))
                {
                    tags.Add("🔧");   // carries internal config (attachments / firemode / etc.)
                }

                cards.Add(new PlayerCard(
                    DetailPrefix + copy.Uid,
                    name,
                    null,
                    tags,
                    new[] { new PlayerButton("takeuid", m_Tr.Resolve("Take", lang), "success") },
                    group,
                    "#" + ordinal.ToString(CultureInfo.InvariantCulture)));
                ordinal++;
            }

            return cards;
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var lang = context.Language;
            var steamId = context.SteamId;

            // Pure navigation — works offline (read-only browsing).
            if (actionId == "back")
            {
                m_Detail.TryRemove(steamId, out _);
                return PlayerActionResult.Ok();
            }

            if (actionId == "details")
            {
                if (!ushort.TryParse(cardKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var detailId) || detailId == 0)
                {
                    return PlayerActionResult.Fail(m_Tr.Resolve("Item not found.", lang));
                }

                m_Detail[steamId] = detailId;
                return PlayerActionResult.Ok();
            }

            var user = await ResolveOnlineAsync(steamId);
            if (user == null)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("You must be online to store or withdraw.", lang));
            }

            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);

            if (actionId == "takeuid")
            {
                var uid = cardKey.StartsWith(DetailPrefix) ? cardKey.Substring(DetailPrefix.Length) : cardKey;
                var copy = m_Vault.Get(steamId).FirstOrDefault(x => x.Uid == uid);
                var ok = await m_Vault.TakeByUidAsync(user, uid);
                if (!ok)
                {
                    return PlayerActionResult.Fail(m_Tr.Resolve("That copy is no longer in the vault.", lang));
                }

                // Leave the detail view automatically once the last copy of this id is gone.
                if (copy != null && !m_Vault.Get(steamId).Any(x => x.ItemId == copy.ItemId))
                {
                    m_Detail.TryRemove(steamId, out _);
                }

                var takenName = copy != null ? VaultNames.NameOf(copy.ItemId, names) : m_Tr.Resolve("Vault", lang);
                return PlayerActionResult.Ok(m_Tr.Format(lang, "Took {0} from your vault.", takenName));
            }

            if (!ushort.TryParse(cardKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId) || itemId == 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Item not found.", lang));
            }

            var amount = 1;
            if (!string.IsNullOrEmpty(value)
                && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0))
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Enter a valid quantity.", lang));
            }

            var name = VaultNames.NameOf(itemId, names);
            switch (actionId)
            {
                case "store":
                {
                    var result = await m_Vault.StoreAsync(user, itemId, amount);
                    if (result.Stored == 0)
                    {
                        return PlayerActionResult.Fail(result.CapacityReached
                            ? m_Tr.Resolve("Vault is full.", lang)
                            : m_Tr.Format(lang, "You don't have {0} in your backpack.", name));
                    }

                    var msg = m_Tr.Format(lang, "Stored {0}× {1}.", result.Stored, name);
                    if (result.CapacityReached)
                    {
                        msg += " " + m_Tr.Resolve("Vault is full.", lang);
                    }

                    return PlayerActionResult.Ok(msg);
                }

                case "take":
                {
                    var taken = await m_Vault.TakeAsync(user, itemId, amount);
                    return taken == 0
                        ? PlayerActionResult.Fail(m_Tr.Format(lang, "You have no {0} in the vault.", name))
                        : PlayerActionResult.Ok(m_Tr.Format(lang, "Took {0}× {1}.", taken, name));
                }

                default:
                    return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
            }
        }

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;
    }
}
