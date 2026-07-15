using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OpenMod.API.Permissions;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.Items;
using UnturnedMods.Shared.WebPanel;

namespace well404.Vault
{
    /// <summary>
    /// The player-facing vault surface for the web panel: a compact list with two sections — the
    /// player's backpack (store) and the vault contents (take). Copies with the same meaningful visible state are merged into one entry. Raw quality only
    /// distinguishes assets whose ItemAsset.showQuality is true. An item that has several distinct variants
    /// (e.g. shell boxes with different round counts) becomes a collapsed summary card whose custom
    /// UI opens the children in a modal, so the player can act on a particular one. Every entry offers
    /// All / a typed amount / One. Driven entirely by the generic player-menu model.
    /// </summary>
    public sealed class VaultPlayerMenu : IPlayerMenu, IPlayerMenuUiProvider
    {
        public const string MenuId = "vault";
        private static readonly WebUiExtension s_Ui = WebUiExtension.FromEmbeddedResources(
            typeof(VaultPlayerMenu).Assembly, "player-ui.html", "player-ui.css", "player-ui.js");

        // The web surface enforces the same permissions as the chat commands, so a player whose
        // store/take is denied can't bypass it through the panel.
        private const string StorePermission = "well404.Vault:commands.vault.store";
        private const string TakePermission = "well404.Vault:commands.vault.take";

        private readonly VaultService m_Vault;
        private readonly IUserManager m_UserManager;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IPermissionChecker m_Permissions;
        private readonly IWebTranslationRegistry m_Tr;

        public VaultPlayerMenu(VaultService vault, IUserManager userManager, IItemDirectory itemDirectory, IPermissionChecker permissions, IWebTranslationRegistry translations)
        {
            m_Vault = vault;
            m_UserManager = userManager;
            m_ItemDirectory = itemDirectory;
            m_Permissions = permissions;
            m_Tr = translations;
        }

        public string Id => MenuId;

        public string Title => "Vault";

        public string? Icon => "🧳";

        public WebUiExtension Ui => s_Ui;

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var lang = context.Language;
            var steamId = context.SteamId;
            var user = await ResolveOnlineAsync(steamId);

            var max = user != null ? await m_Vault.GetMaxSlotsAsync(user) : m_Vault.OverrideOrBase(steamId);
            var header = m_Tr.Format(lang, "Vault: {0} / {1} slots", m_Vault.UsedSlots(steamId), max);

            // Store/withdraw need a live inventory AND the matching permission (parity with the
            // commands). Contents stay viewable (read-only) when offline or unauthorized.
            var canStore = user != null && await CanAsync(user, StorePermission);
            var canTake = user != null && await CanAsync(user, TakePermission);
            var message = user == null
                ? m_Tr.Resolve("You must be online to store or withdraw.", lang)
                : (!canStore && !canTake ? m_Tr.Resolve("You don't have permission to use the vault.", lang) : null);
            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);

            var cards = new List<PlayerCard>();
            if (canStore && user != null)
            {
                var backpack = await m_Vault.BackpackVariantsAsync(user);
                cards.AddRange(BuildItemCards(backpack, "store", m_Tr.Resolve("Backpack", lang), names, lang, allowOps: true));
            }

            cards.AddRange(BuildItemCards(m_Vault.VaultVariants(steamId), "take", m_Tr.Resolve("Vault", lang), names, lang, allowOps: canTake));

            return new PlayerMenuView(m_Tr.Resolve("Vault", lang), header, cards, message, null, "list");
        }

        // Builds one card per item id. Quality participates in the visible variant only when
        // Unturned itself exposes it through ItemAsset.showQuality. Raw quality is still preserved in
        // storage and restored on withdrawal; non-quality cards use a wildcard action key.
        private List<PlayerCard> BuildItemCards(IReadOnlyList<ItemVariant> variants, string mode, string group, IReadOnlyDictionary<string, LocalizedItemInfo> names, string lang, bool allowOps)
        {
            var cards = new List<PlayerCard>();
            foreach (var byId in variants.GroupBy(v => v.ItemId)
                         .OrderByDescending(grouping => grouping.Sum(item => item.Count * item.SlotCost))
                         .ThenBy(grouping => grouping.Key))
            {
                var idStr = byId.Key.ToString(CultureInfo.InvariantCulture);
                var itemInfo = LocalizedItemCatalog.Get(byId.Key, names);
                var name = itemInfo.DisplayName(lang);
                var showsQuality = itemInfo.ShowsQuality;
                var list = MergeVisibleVariants(byId, showsQuality);
                var totalCount = list.Sum(v => v.Count);
                var totalSlots = list.Sum(v => v.Count * v.SlotCost);
                var metadata = ItemMetadata(itemInfo, byId.Key, totalCount, totalSlots);

                if (list.Count == 1)
                {
                    var v = list[0];
                    var key = VariantKey(v, showsQuality);
                    cards.Add(new PlayerCard(key, name, null,
                        VariantTags(v, true, lang, showsQuality), Ops(mode, key, v.Count, lang, allowOps),
                        group, "#" + idStr, null, null, null, metadata));
                    continue;
                }

                var children = new List<PlayerCard>();
                foreach (var v in list.OrderByDescending(x => x.Amount).ThenByDescending(x => showsQuality ? x.Quality : (byte)0))
                {
                    var key = VariantKey(v, showsQuality);
                    children.Add(new PlayerCard(key, name, null,
                        VariantTags(v, false, lang, showsQuality), Ops(mode, key, v.Count, lang, allowOps),
                        group, null, null, null, null,
                        ItemMetadata(itemInfo, byId.Key, v.Count, v.Count * v.SlotCost)));
                }

                var parentButtons = allowOps
                    ? new[] { OpButton(mode, "all", idStr, totalCount, lang) }
                    : Array.Empty<PlayerButton>();
                cards.Add(new PlayerCard(
                    idStr, name, null,
                    new[] { "×" + totalCount.ToString(CultureInfo.InvariantCulture), m_Tr.Format(lang, "{0} slots", totalSlots) },
                    parentButtons,
                    group, "#" + idStr, children, null, null, metadata));
            }

            return cards;
        }

        internal static IReadOnlyDictionary<string, string> ItemMetadata(
            LocalizedItemInfo item, ushort itemId, int count, int totalSlots)
            => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["itemId"] = itemId.ToString(CultureInfo.InvariantCulture),
                ["count"] = count.ToString(CultureInfo.InvariantCulture),
                ["totalSlots"] = totalSlots.ToString(CultureInfo.InvariantCulture),
                ["rarity"] = item.Rarity,
                ["rarityRank"] = item.RarityRank.ToString(CultureInfo.InvariantCulture),
                ["category"] = item.Category,
                ["itemType"] = item.ItemType
            };

        internal static List<ItemVariant> MergeVisibleVariants(IEnumerable<ItemVariant> variants, bool showsQuality)
            => variants
                .GroupBy(v => (v.Amount, Quality: showsQuality ? v.Quality : (byte)0, v.State, v.SlotCost, v.MaxAmount))
                .Select(group =>
                {
                    var first = group.First();
                    return new ItemVariant(first.ItemId, first.Amount, group.Key.Quality, first.State,
                        group.Sum(item => item.Count), first.SlotCost, first.MaxAmount);
                })
                .ToList();

        private PlayerButton[] Ops(string mode, string cardKey, int count, string lang, bool allowOps)
            => allowOps ? OpsButtons(mode, cardKey, count, lang) : Array.Empty<PlayerButton>();

        internal static bool ShouldShowQuality(bool assetShowsQuality, byte quality)
            => assetShowsQuality && quality < 100;

        private List<string> VariantTags(ItemVariant v, bool includeSlots, string lang, bool showsQuality)
        {
            var tags = new List<string> { "×" + v.Count.ToString(CultureInfo.InvariantCulture) };
            if (v.MaxAmount > 1)
            {
                tags.Add(v.Amount.ToString(CultureInfo.InvariantCulture) + "/" + v.MaxAmount.ToString(CultureInfo.InvariantCulture));
            }
            else if (v.Amount > 1)
            {
                tags.Add(m_Tr.Format(lang, "Amount {0}", v.Amount));
            }

            if (ShouldShowQuality(showsQuality, v.Quality))
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
                    // Empty default ("") so the box opens blank and the player must type a number —
                    // this keeps the typed-amount action distinct from both "All" and "One" (any fixed
                    // default would coincide with one of them). The label shows the valid range.
                    return new PlayerButton(mode + "_some", m_Tr.Resolve("Amount", lang), style,
                        m_Tr.Resolve(mode == "store" ? "Amount to store" : "Amount to take", lang)
                            + " (1–" + count.ToString(CultureInfo.InvariantCulture) + ")",
                        "");
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

            // Enforce the same permission as the corresponding command (web/command parity).
            if (mode == "store" || mode == "take")
            {
                if (!await CanAsync(user, mode == "store" ? StorePermission : TakePermission))
                {
                    return PlayerActionResult.Fail(m_Tr.Resolve("You don't have permission to use the vault.", lang));
                }
            }

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

            var name = VaultNames.NameOf(itemId, names, lang);

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

        // ----- variant key encoding ("id|amount|quality-or-*|base64state") -----

        internal static string VariantKey(ItemVariant v, bool showsQuality)
            => v.ItemId.ToString(CultureInfo.InvariantCulture) + "|" + ((int)v.Amount).ToString(CultureInfo.InvariantCulture)
               + "|" + (showsQuality ? ((int)v.Quality).ToString(CultureInfo.InvariantCulture) : "*") + "|" + v.State;

        internal static (ushort itemId, byte amount, byte? quality, string stateBase64, byte[] state)? ParseVariant(string cardKey)
        {
            if (cardKey.IndexOf("|", StringComparison.Ordinal) < 0)
            {
                return null;
            }

            var parts = cardKey.Split(new[] { "|" }, 4, StringSplitOptions.None);
            if (parts.Length < 4
                || !ushort.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                || !byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                return null;
            }

            byte? quality = null;
            if (!string.Equals(parts[2], "*", StringComparison.Ordinal))
            {
                if (!byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuality))
                {
                    return null;
                }
                quality = parsedQuality;
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

        private async Task<bool> CanAsync(UnturnedUser user, string permission)
            => await m_Permissions.CheckPermissionAsync(user, permission) == PermissionGrantResult.Grant;
    }
}
