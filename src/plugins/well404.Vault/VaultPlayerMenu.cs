using System;
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
    /// player's backpack (store) and the vault contents (take). Copies that are identical (same id +
    /// amount + quality + state) are merged into one entry. An item that has several distinct variants
    /// (e.g. shell boxes with different round counts) becomes a collapsible card whose children list
    /// each variant with its specifics, so the player can act on a particular one. Every entry offers
    /// All / a typed amount / One. Driven entirely by the generic player-menu model.
    /// </summary>
    public sealed class VaultPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "vault";

        private readonly VaultService m_Vault;
        private readonly IUserManager m_UserManager;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IWebTranslationRegistry m_Tr;

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
            // Contents are viewable read-only offline; only store/withdraw needs a live inventory.
            var message = user == null ? m_Tr.Resolve("You must be online to store or withdraw.", lang) : null;
            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);

            var cards = new List<PlayerCard>();
            if (user != null)
            {
                var backpack = await m_Vault.BackpackVariantsAsync(user);
                cards.AddRange(BuildItemCards(backpack, "store", m_Tr.Resolve("Backpack", lang), names, lang));
            }

            cards.AddRange(BuildItemCards(m_Vault.VaultVariants(steamId), "take", m_Tr.Resolve("Vault", lang), names, lang));

            return new PlayerMenuView(m_Tr.Resolve("Vault", lang), header, cards, message, null, "list");
        }

        // Builds one card per item id (merging identical copies). A single-variant id is a plain card;
        // a multi-variant id is a collapsible parent whose children are the variants.
        private List<PlayerCard> BuildItemCards(IReadOnlyList<ItemVariant> variants, string mode, string group, IReadOnlyDictionary<string, string> names, string lang)
        {
            var cards = new List<PlayerCard>();
            foreach (var byId in variants.GroupBy(v => v.ItemId).OrderBy(g => VaultNames.NameOf(g.Key, names)))
            {
                var idStr = byId.Key.ToString(CultureInfo.InvariantCulture);
                var name = VaultNames.NameOf(byId.Key, names);
                var list = byId.ToList();
                var totalCount = list.Sum(v => v.Count);
                var totalSlots = list.Sum(v => v.Count * v.SlotCost);

                if (list.Count == 1)
                {
                    var v = list[0];
                    cards.Add(new PlayerCard(VariantKey(v), name, null,
                        VariantTags(v, true, lang), OpsButtons(mode, VariantKey(v), v.Count, lang), group, "#" + idStr));
                    continue;
                }

                // Multiple variants → a collapsible parent ("All" of the whole item) with variant children.
                var children = new List<PlayerCard>();
                foreach (var v in list.OrderByDescending(x => x.Amount).ThenByDescending(x => x.Quality))
                {
                    children.Add(new PlayerCard(VariantKey(v), name, null,
                        VariantTags(v, false, lang), OpsButtons(mode, VariantKey(v), v.Count, lang), group));
                }

                cards.Add(new PlayerCard(
                    idStr, name, null,
                    new[] { "×" + totalCount.ToString(CultureInfo.InvariantCulture), m_Tr.Format(lang, "{0} slots", totalSlots) },
                    new[] { OpButton(mode, "all", idStr, totalCount, lang) },
                    group, "#" + idStr, children));
            }

            return cards;
        }

        private List<string> VariantTags(ItemVariant v, bool includeSlots, string lang)
        {
            var tags = new List<string> { "×" + v.Count.ToString(CultureInfo.InvariantCulture) };
            // Stack/ammo fill shown as a ratio (e.g. 6/8) to avoid being mistaken for the copy count;
            // fall back to the raw figure if the capacity is unknown.
            if (v.MaxAmount > 1)
            {
                tags.Add(v.Amount.ToString(CultureInfo.InvariantCulture) + "/" + v.MaxAmount.ToString(CultureInfo.InvariantCulture));
            }
            else if (v.Amount > 1)
            {
                tags.Add(m_Tr.Format(lang, "Amount {0}", v.Amount));
            }

            if (v.Quality < 100)
            {
                tags.Add(m_Tr.Format(lang, "Durability {0}%", v.Quality));
            }

            if (!string.IsNullOrEmpty(v.State))
            {
                tags.Add("🔧");
            }

            if (includeSlots)
            {
                tags.Add(m_Tr.Format(lang, "{0} slots", v.Count * v.SlotCost));
            }

            return tags;
        }

        // With a single copy there's nothing to choose, so just one Store/Take button; with several,
        // offer All / a typed amount / One.
        private PlayerButton[] OpsButtons(string mode, string cardKey, int count, string lang)
        {
            if (count <= 1)
            {
                var style = mode == "store" ? "primary" : "success";
                var label = m_Tr.Resolve(mode == "store" ? "Store" : "Take", lang);
                return new[] { new PlayerButton(mode + "_all", label, style) };
            }

            return new[]
            {
                OpButton(mode, "all", cardKey, count, lang),
                OpButton(mode, "some", cardKey, count, lang),
                OpButton(mode, "one", cardKey, count, lang)
            };
        }

        private PlayerButton OpButton(string mode, string op, string cardKey, int count, string lang)
        {
            var style = mode == "store" ? "primary" : "success";
            switch (op)
            {
                case "all":
                    return new PlayerButton(mode + "_all", m_Tr.Resolve("All", lang), style);
                case "some":
                    // Prompt defaults to 1 (not the full count) so the typed-amount action is clearly
                    // distinct from "All" and the player picks how many.
                    return new PlayerButton(mode + "_some", m_Tr.Resolve("Amount", lang), style,
                        m_Tr.Resolve(mode == "store" ? "Amount to store" : "Amount to take", lang), "1");
                default:
                    return new PlayerButton(mode + "_one", m_Tr.Resolve("One", lang), style);
            }
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var lang = context.Language;
            var user = await ResolveOnlineAsync(context.SteamId);
            if (user == null)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("You must be online to store or withdraw.", lang));
            }

            // op = all | some | one; amount derived from it.
            var underscore = actionId.LastIndexOf('_');
            if (underscore <= 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
            }

            var mode = actionId.Substring(0, underscore);   // store | take
            var op = actionId.Substring(underscore + 1);    // all | some | one
            int amount;
            if (op == "all")
            {
                amount = int.MaxValue;
            }
            else if (op == "one")
            {
                amount = 1;
            }
            else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Enter a valid quantity.", lang));
            }

            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);
            var variant = ParseVariant(cardKey);
            var itemId = variant?.itemId ?? ParseItemId(cardKey);
            if (itemId == 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Item not found.", lang));
            }

            var name = VaultNames.NameOf(itemId, names);

            if (mode == "store")
            {
                var result = variant.HasValue
                    ? await m_Vault.StoreVariantAsync(user, itemId, variant.Value.amount, variant.Value.quality, variant.Value.state, amount)
                    : await m_Vault.StoreAsync(user, itemId, amount);
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

            if (mode == "take")
            {
                var taken = variant.HasValue
                    ? await m_Vault.TakeVariantAsync(user, itemId, variant.Value.amount, variant.Value.quality, variant.Value.stateBase64, amount)
                    : await m_Vault.TakeAsync(user, itemId, amount);
                return taken == 0
                    ? PlayerActionResult.Fail(m_Tr.Format(lang, "You have no {0} in the vault.", name))
                    : PlayerActionResult.Ok(m_Tr.Format(lang, "Took {0}× {1}.", taken, name));
            }

            return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
        }

        // ----- variant key encoding ("id|amount|quality|base64state") -----

        private static string VariantKey(ItemVariant v)
            => v.ItemId.ToString(CultureInfo.InvariantCulture) + "|" + ((int)v.Amount).ToString(CultureInfo.InvariantCulture)
               + "|" + ((int)v.Quality).ToString(CultureInfo.InvariantCulture) + "|" + v.State;

        private static (ushort itemId, byte amount, byte quality, string stateBase64, byte[] state)? ParseVariant(string cardKey)
        {
            if (cardKey.IndexOf('|') < 0)
            {
                return null;
            }

            var parts = cardKey.Split('|');
            if (parts.Length < 4
                || !ushort.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                || !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
                || !byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality))
            {
                return null;
            }

            var stateBase64 = parts[3];
            byte[] state;
            try
            {
                state = string.IsNullOrEmpty(stateBase64) ? Array.Empty<byte>() : Convert.FromBase64String(stateBase64);
            }
            catch
            {
                state = Array.Empty<byte>();
            }

            return (id, amount, quality, stateBase64, state);
        }

        private static ushort ParseItemId(string cardKey)
            => ushort.TryParse(cardKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (ushort)0;

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;
    }
}
