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
        // Stable PlayerInventory page numbers. Keeping action-key parsing independent from
        // Assembly-CSharp lets the presentation helpers remain unit-testable outside the game.
        private const byte HandsInventoryPage = 2;
        private const byte BackpackInventoryPage = 3;
        private const byte VestInventoryPage = 4;
        private const byte ShirtInventoryPage = 5;
        private const byte PantsInventoryPage = 6;
        private const byte StorageInventoryPage = 7;

        private static readonly WebUiExtension s_Ui = WebUiExtension.FromEmbeddedResources(
            typeof(VaultPlayerMenu).Assembly, "player-ui.html", "player-ui.css", "player-ui.js");

        // The web surface enforces the same permissions as the chat commands, so a player whose
        // store/take is denied can't bypass it through the panel.
        private const string StorePermission = "well404.Vault:commands.vault.store";
        private const string TakePermission = "well404.Vault:commands.vault.take";
        private const string UpgradePermission = "well404.Vault:commands.vault.upgrade";
        private const string TeamStorePermission = "well404.Vault:commands.vault.team.store";
        private const string TeamTakePermission = "well404.Vault:commands.vault.team.take";
        private const string TeamUpgradePermission = "well404.Vault:commands.vault.team.upgrade";

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
            if (user != null)
            {
                await Cysharp.Threading.Tasks.UniTask.SwitchToMainThread();
                m_Vault.TouchOwner(user);
            }

            var personal = user != null
                ? await m_Vault.GetPersonalVaultAsync(user)
                : m_Vault.GetContainer(VaultContainerRef.Player(steamId));
            var max = personal.Capacity;
            var personalUsed = personal.UsedSlots;
            var personalHeader = m_Tr.Format(lang, "Personal vault: {0} / {1} slots", personalUsed, max);

            // Store/withdraw need a live inventory AND the matching permission (parity with the
            // commands). Contents stay viewable (read-only) when offline or unauthorized.
            var canStore = user != null && await CanAsync(user, StorePermission);
            var canTake = user != null && await CanAsync(user, TakePermission);
            var canUpgrade = user != null && await CanAsync(user, UpgradePermission);
            var canTeamStore = user != null && await CanAsync(user, TeamStorePermission);
            var canTeamTake = user != null && await CanAsync(user, TeamTakePermission);
            var canTeamUpgrade = user != null && await CanAsync(user, TeamUpgradePermission);
            var message = user == null
                ? m_Tr.Resolve("You must be online to store or withdraw.", lang)
                : (!canStore && !canTake && !canUpgrade && !canTeamStore && !canTeamTake && !canTeamUpgrade
                    ? m_Tr.Resolve("You don't have permission to use the vault.", lang)
                    : null);
            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);

            var cards = new List<PlayerCard>();
            cards.Add(ScopeMarker("personal", "vault", personalUsed, max));
            if (canStore && user != null)
            {
                cards.Add(ScopeMarker("personal", "backpack", personalUsed, max));
                var backpack = await m_Vault.BackpackVariantsAsync(user);
                cards.AddRange(BuildItemCards(backpack, "store", m_Tr.Resolve("Backpack", lang), names, lang, allowOps: true, "personal", "backpack"));
            }

            cards.AddRange(BuildItemCards(m_Vault.VaultVariants(steamId), "take", m_Tr.Resolve("Vault", lang), names, lang, allowOps: canTake, "personal", "vault"));

            var personalPurchase = m_Vault.CurrentPersonalPurchaseSettings;
            var personalMaximum = Math.Max(personal.BaseCapacity, personalPurchase.MaxSlots);
            if (canUpgrade && personalPurchase.Enabled && personal.Capacity < personalMaximum)
            {
                var quote = m_Vault.QuotePersonalCapacity(personal.Capacity, personal.BaseCapacity);
                if (quote.Slots > 0)
                {
                    cards.Add(CapacityUpgradeCard(
                        "personal", "personal-capacity", "personal_upgrade", personal.PurchaseVersion,
                        quote.Slots, quote.Price, personalMaximum,
                        "Buy personal vault capacity", "Personal vault", lang));
                }
            }

            if (user != null && m_Vault.CurrentTeamSettings.Enabled)
            {
                var team = await m_Vault.GetTeamVaultAsync(user);
                if (team != null)
                {
                    var teamCapacity = team.Value.Container.Capacity;
                    var teamHeader = m_Tr.Format(
                        lang,
                        "Team vault {0}: {1} / {2} slots",
                        team.Value.Context.DisplayName,
                        team.Value.Container.UsedSlots,
                        teamCapacity);
                    cards.Add(ScopeMarker("team", "vault", team.Value.Container.UsedSlots, teamCapacity));
                    cards.Add(MoveTargetMarker("personal", canTake && canTeamStore));
                    cards.Add(MoveTargetMarker("team", canTeamTake && canStore));
                    if (canTeamStore)
                    {
                        cards.Add(ScopeMarker("team", "backpack", team.Value.Container.UsedSlots, teamCapacity));
                        var backpack = await m_Vault.BackpackVariantsAsync(user);
                        cards.AddRange(BuildItemCards(backpack, "teamstore", m_Tr.Resolve("Backpack", lang), names, lang, true, "team", "backpack"));
                    }

                    cards.AddRange(BuildItemCards(
                        m_Vault.VaultVariants(VaultContainerRef.Team(team.Value.Context.Key)),
                        "teamtake",
                        m_Tr.Resolve("Team vault", lang),
                        names,
                        lang,
                        canTeamTake,
                        "team",
                        "vault"));

                    var teamSettings = m_Vault.CurrentTeamSettings;
                    if (canTeamUpgrade
                        && teamSettings.Purchase.Enabled
                        && team.Value.Container.Capacity < teamSettings.MaxSlots)
                    {
                        var quote = m_Vault.QuoteTeamCapacity(team.Value.Container.Capacity);
                        var step = quote.Slots;
                        var price = quote.Price;
                        if (step > 0)
                        {
                            cards.Add(new PlayerCard(
                            "team-capacity|"
                                + team.Value.Container.PurchaseVersion.ToString(CultureInfo.InvariantCulture) + "|"
                                + step.ToString(CultureInfo.InvariantCulture) + "|"
                                + price.ToString(CultureInfo.InvariantCulture) + "|"
                                + teamSettings.MaxSlots.ToString(CultureInfo.InvariantCulture),
                            m_Tr.Resolve("Buy team vault capacity", lang),
                            new[] { m_Tr.Format(lang, "Add {0} slots for {1}", step, price) },
                            new[] { m_Tr.Format(lang, "Maximum {0} slots", teamSettings.MaxSlots) },
                            new[]
                            {
                                new PlayerButton(
                                    "team_upgrade",
                                    m_Tr.Resolve("Buy capacity", lang),
                                    "primary",
                                    null,
                                    null,
                                    null,
                                    m_Tr.Format(lang, "Spend {0} from your balance to add {1} slots to the team vault? Capacity belongs to the team and is not refunded when you leave.", price, step))
                            },
                            m_Tr.Resolve("Team vault", lang),
                            null,
                            null,
                            null,
                            null,
                            new Dictionary<string, string>
                            {
                                ["scope"] = "team",
                                ["section"] = "vault",
                                ["control"] = "upgrade",
                                ["upgradePrice"] = m_Vault.CurrencySymbol + price.ToString("0.##", CultureInfo.InvariantCulture),
                                ["upgradeSlots"] = step.ToString(CultureInfo.InvariantCulture),
                                ["count"] = "0",
                                ["totalSlots"] = "0",
                                ["category"] = "other",
                                ["rarity"] = "common",
                                ["rarityRank"] = "0"
                            }));
                        }
                    }
                }
                else
                {
                    message = string.IsNullOrEmpty(message)
                        ? m_Tr.Resolve("Join or create a party to use the team vault.", lang)
                        : message + " " + m_Tr.Resolve("Join or create a party to use the team vault.", lang);
                }
            }

            return new PlayerMenuView(m_Tr.Resolve("Vault", lang), personalHeader, cards, message, null, "list");
        }

        private static PlayerCard ScopeMarker(string scope, string section, int usedSlots, int maxSlots)
            => new PlayerCard(
                "scope:" + scope + ":" + section,
                string.Empty,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new Dictionary<string, string>
                {
                    ["scope"] = scope,
                    ["section"] = section,
                    ["control"] = "scope_marker",
                    ["usedSlots"] = usedSlots.ToString(CultureInfo.InvariantCulture),
                    ["maxSlots"] = maxSlots.ToString(CultureInfo.InvariantCulture)
                });

        private static PlayerCard MoveTargetMarker(string scope, bool enabled)
            => new PlayerCard(
                "move-target:" + scope, string.Empty, null, null, null, null, null, null, null, null,
                new Dictionary<string, string>
                {
                    ["scope"] = scope,
                    ["section"] = "vault",
                    ["control"] = "move_target",
                    ["enabled"] = enabled ? "true" : "false"
                });

        private PlayerCard CapacityUpgradeCard(
            string scope, string keyPrefix, string actionId, long version, int slots, decimal price,
            int maximum, string title, string group, string lang)
            => new PlayerCard(
                keyPrefix + "|" + version.ToString(CultureInfo.InvariantCulture) + "|"
                + slots.ToString(CultureInfo.InvariantCulture) + "|"
                + price.ToString(CultureInfo.InvariantCulture) + "|"
                + maximum.ToString(CultureInfo.InvariantCulture),
                m_Tr.Resolve(title, lang),
                new[] { m_Tr.Format(lang, "Add {0} slots for {1}", slots, price) },
                new[] { m_Tr.Format(lang, "Maximum {0} slots", maximum) },
                new[]
                {
                    new PlayerButton(
                        actionId, m_Tr.Resolve("Buy capacity", lang), "primary", null, null, null,
                        m_Tr.Format(lang, "Spend {0} from your balance to add {1} vault slots?", price, slots))
                },
                m_Tr.Resolve(group, lang), null, null, null, null,
                new Dictionary<string, string>
                {
                    ["scope"] = scope,
                    ["section"] = "vault",
                    ["control"] = "upgrade",
                    ["upgradePrice"] = m_Vault.CurrencySymbol + price.ToString("0.##", CultureInfo.InvariantCulture),
                    ["upgradeSlots"] = slots.ToString(CultureInfo.InvariantCulture),
                    ["count"] = "0",
                    ["totalSlots"] = "0",
                    ["category"] = "other",
                    ["rarity"] = "common",
                    ["rarityRank"] = "0"
                });

        // Builds one card per item id and carried-container source. Keeping carried sources
        // separate makes the UI filter truthful and lets store actions target exactly the page the
        // player selected instead of taking an identical item from hidden pants/hands.
        private List<PlayerCard> BuildItemCards(IReadOnlyList<ItemVariant> variants, string mode, string group, IReadOnlyDictionary<string, LocalizedItemInfo> names, string lang, bool allowOps, string scope, string section)
        {
            var cards = new List<PlayerCard>();
            var carried = string.Equals(section, "backpack", StringComparison.Ordinal);
            foreach (var bySource in variants
                         .GroupBy(v => (v.ItemId, InventoryPage: carried ? v.InventoryPage : byte.MaxValue))
                         .OrderByDescending(grouping => grouping.Sum(item => item.Count * item.SlotCost))
                         .ThenBy(grouping => grouping.Key.ItemId)
                         .ThenBy(grouping => grouping.Key.InventoryPage))
            {
                var itemId = bySource.Key.ItemId;
                var idStr = itemId.ToString(CultureInfo.InvariantCulture);
                var containerKey = carried ? InventoryContainerKey(bySource.Key.InventoryPage) : string.Empty;
                var containerLabel = string.IsNullOrEmpty(containerKey)
                    ? null
                    : m_Tr.Resolve(InventoryContainerSource(containerKey), lang);
                var itemInfo = LocalizedItemCatalog.Get(itemId, names);
                var name = itemInfo.DisplayName(lang);
                var showsQuality = itemInfo.ShowsQuality;
                var list = MergeVisibleVariants(bySource, showsQuality);
                var totalCount = list.Sum(v => v.Count);
                var totalSlots = list.Sum(v => v.Count * v.SlotCost);
                var metadata = ItemMetadata(
                    itemInfo, itemId, totalCount, totalSlots, scope, section, containerKey);

                if (list.Count == 1)
                {
                    var v = list[0];
                    var key = VariantKey(v, showsQuality);
                    cards.Add(new PlayerCard(key, name, null,
                        VariantTags(v, true, lang, showsQuality, containerLabel),
                        Ops(mode, key, v.Count, lang, allowOps),
                        group, "#" + idStr, null, null, null, metadata));
                    continue;
                }

                var children = new List<PlayerCard>();
                foreach (var v in list.OrderByDescending(x => x.Amount)
                             .ThenByDescending(x => showsQuality ? x.Quality : (byte)0))
                {
                    var key = VariantKey(v, showsQuality);
                    children.Add(new PlayerCard(key, name, null,
                        VariantTags(v, false, lang, showsQuality, containerLabel),
                        Ops(mode, key, v.Count, lang, allowOps),
                        group, null, null, null, null,
                        ItemMetadata(
                            itemInfo, itemId, v.Count, v.Count * v.SlotCost,
                            scope, section, containerKey)));
                }

                var itemKey = ItemContainerKey(itemId, bySource.Key.InventoryPage);
                var parentButtons = allowOps
                    ? new[] { OpButton(mode, "all", itemKey, totalCount, lang) }
                    : Array.Empty<PlayerButton>();
                var summaryTags = new List<string>();
                if (!string.IsNullOrEmpty(containerLabel))
                {
                    summaryTags.Add(containerLabel!);
                }
                summaryTags.Add("×" + totalCount.ToString(CultureInfo.InvariantCulture));
                summaryTags.Add(m_Tr.Format(lang, "{0} slots", totalSlots));
                cards.Add(new PlayerCard(
                    itemKey, name, null, summaryTags, parentButtons,
                    group, "#" + idStr, children, null, null, metadata));
            }

            return cards;
        }

        internal static string InventoryContainerKey(byte page)
        {
            if (page == HandsInventoryPage) return "hands";
            if (page == BackpackInventoryPage) return "backpack";
            if (page == VestInventoryPage) return "vest";
            if (page == ShirtInventoryPage) return "shirt";
            if (page == PantsInventoryPage) return "pants";
            return string.Empty;
        }

        internal static bool IsCarriedInventoryPage(byte page)
            => page >= HandsInventoryPage && page < StorageInventoryPage;

        private static string InventoryContainerSource(string key)
        {
            switch (key)
            {
                case "hands": return "Hands";
                case "backpack": return "Backpack";
                case "vest": return "Vest";
                case "shirt": return "Shirt";
                case "pants": return "Pants";
                default: return key;
            }
        }

        internal static string ItemContainerKey(ushort itemId, byte inventoryPage)
        {
            var id = itemId.ToString(CultureInfo.InvariantCulture);
            return IsCarriedInventoryPage(inventoryPage)
                ? id + "@" + inventoryPage.ToString(CultureInfo.InvariantCulture)
                : id;
        }

        internal static IReadOnlyDictionary<string, string> ItemMetadata(
            LocalizedItemInfo item, ushort itemId, int count, int totalSlots,
            string scope = "", string section = "", string inventoryContainer = "")
            => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["itemId"] = itemId.ToString(CultureInfo.InvariantCulture),
                ["count"] = count.ToString(CultureInfo.InvariantCulture),
                ["totalSlots"] = totalSlots.ToString(CultureInfo.InvariantCulture),
                ["rarity"] = item.Rarity,
                ["rarityRank"] = item.RarityRank.ToString(CultureInfo.InvariantCulture),
                ["category"] = item.Category,
                ["itemType"] = item.ItemType,
                ["scope"] = scope,
                ["section"] = section,
                ["inventoryContainer"] = inventoryContainer
            };

        internal static List<ItemVariant> MergeVisibleVariants(IEnumerable<ItemVariant> variants, bool showsQuality)
            => variants
                .GroupBy(v => (v.Amount, Quality: showsQuality ? v.Quality : (byte)0, v.State,
                    v.SlotCost, v.MaxAmount, v.InventoryPage))
                .Select(group =>
                {
                    var first = group.First();
                    return new ItemVariant(first.ItemId, first.Amount, group.Key.Quality, first.State,
                        group.Sum(item => item.Count), first.SlotCost, first.MaxAmount, first.InventoryPage);
                })
                .ToList();

        private PlayerButton[] Ops(string mode, string cardKey, int count, string lang, bool allowOps)
            => allowOps ? OpsButtons(mode, cardKey, count, lang) : Array.Empty<PlayerButton>();

        internal static bool ShouldShowQuality(bool assetShowsQuality, byte quality)
            => assetShowsQuality && quality < 100;

        private List<string> VariantTags(
            ItemVariant v, bool includeSlots, string lang, bool showsQuality, string? containerLabel = null)
        {
            var tags = new List<string>();
            if (!string.IsNullOrEmpty(containerLabel))
            {
                tags.Add(containerLabel!);
            }
            tags.Add("×" + v.Count.ToString(CultureInfo.InvariantCulture));
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
            var storing = mode == "store" || mode == "teamstore";
            if (count <= 1)
            {
                var style = storing ? "primary" : "success";
                var label = m_Tr.Resolve(storing ? "Store" : "Take", lang);
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
            var storing = mode == "store" || mode == "teamstore";
            var style = storing ? "primary" : "success";
            switch (op)
            {
                case "all":
                    return new PlayerButton(mode + "_all", m_Tr.Resolve("All", lang), style);
                case "some":
                    // Empty default ("") so the box opens blank and the player must type a number —
                    // this keeps the typed-amount action distinct from both "All" and "One" (any fixed
                    // default would coincide with one of them). The label shows the valid range.
                    return new PlayerButton(mode + "_some", m_Tr.Resolve("Amount", lang), style,
                        m_Tr.Resolve(storing ? "Amount to store" : "Amount to take", lang)
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

            if (actionId == "personal_upgrade")
            {
                if (!await CanAsync(user, UpgradePermission))
                    return PlayerActionResult.Fail(m_Tr.Resolve("You don't have permission to use the vault.", lang));

                var purchaseKey = cardKey.Split('|');
                if (purchaseKey.Length != 5
                    || !string.Equals(purchaseKey[0], "personal-capacity", StringComparison.Ordinal)
                    || !long.TryParse(purchaseKey[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedVersion)
                    || !int.TryParse(purchaseKey[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedSlots)
                    || !decimal.TryParse(purchaseKey[3], NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedPrice)
                    || !int.TryParse(purchaseKey[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedMaximum))
                {
                    return PlayerActionResult.Fail(m_Tr.Resolve("The personal vault changed; refresh and try again.", lang));
                }

                var purchase = await m_Vault.PurchasePersonalCapacityAsync(
                    user, expectedVersion, expectedSlots, expectedPrice, expectedMaximum);
                return purchase.Success
                    ? PlayerActionResult.Ok(m_Tr.Format(
                        lang, "Bought {0} personal vault slots for {1}. New capacity: {2}.",
                        purchase.SlotsAdded, purchase.Price, purchase.NewCapacity))
                    : PlayerActionResult.Fail(PersonalPurchaseError(purchase, lang));
            }

            if (actionId == "team_upgrade")
            {
                if (!await CanAsync(user, TeamUpgradePermission))
                {
                    return PlayerActionResult.Fail(m_Tr.Resolve("You don't have permission to use the vault.", lang));
                }

                var purchaseKey = cardKey.Split('|');
                long expectedVersion;
                int expectedSlots;
                decimal expectedPrice;
                int expectedMaximum;
                if (purchaseKey.Length != 5
                    || !string.Equals(purchaseKey[0], "team-capacity", StringComparison.Ordinal)
                    || !long.TryParse(purchaseKey[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedVersion)
                    || !int.TryParse(purchaseKey[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedSlots)
                    || !decimal.TryParse(purchaseKey[3], NumberStyles.Number, CultureInfo.InvariantCulture, out expectedPrice)
                    || !int.TryParse(purchaseKey[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out expectedMaximum))
                {
                    return PlayerActionResult.Fail(
                        m_Tr.Resolve("The team vault changed; refresh and try again.", lang));
                }
                var purchase = await m_Vault.PurchaseTeamCapacityAsync(
                    user,
                    expectedVersion,
                    expectedSlots,
                    expectedPrice,
                    expectedMaximum);
                if (purchase.Success)
                {
                    return PlayerActionResult.Ok(m_Tr.Format(
                        lang,
                        "Bought {0} team vault slots for {1}. New capacity: {2}.",
                        purchase.SlotsAdded,
                        purchase.Price,
                        purchase.NewCapacity));
                }

                return PlayerActionResult.Fail(PurchaseError(purchase, lang));
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
            if (mode == "store" || mode == "take" || mode == "teamstore" || mode == "teamtake"
                || mode == "movetoteam" || mode == "movetopersonal")
            {
                var allowed = mode == "movetoteam"
                    ? await CanAsync(user, TakePermission) && await CanAsync(user, TeamStorePermission)
                    : mode == "movetopersonal"
                        ? await CanAsync(user, TeamTakePermission) && await CanAsync(user, StorePermission)
                        : await CanAsync(user, mode == "teamstore"
                            ? TeamStorePermission
                            : mode == "teamtake"
                                ? TeamTakePermission
                                : mode == "store" ? StorePermission : TakePermission);
                if (!allowed)
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
            var inventoryPage = variant?.inventoryPage ?? ParseInventoryPage(cardKey);
            var itemId = variant?.itemId ?? ParseItemId(cardKey);
            if (itemId == 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Item not found.", lang));
            }

            var name = VaultNames.NameOf(itemId, names, lang);

            if (mode == "store" || mode == "teamstore")
            {
                var result = mode == "teamstore"
                    ? (variant.HasValue
                        ? await m_Vault.StoreTeamVariantAsync(user, itemId, variant.Value.amount, variant.Value.quality, variant.Value.state, amount, inventoryPage)
                        : await m_Vault.StoreTeamAsync(user, itemId, amount, inventoryPage))
                    : (variant.HasValue
                        ? await m_Vault.StoreVariantAsync(user, itemId, variant.Value.amount, variant.Value.quality, variant.Value.state, amount, inventoryPage)
                        : await m_Vault.StoreAsync(user, itemId, amount, inventoryPage));
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

            if (mode == "take" || mode == "teamtake")
            {
                var taken = mode == "teamtake"
                    ? (variant.HasValue
                        ? await m_Vault.TakeTeamVariantAsync(user, itemId, variant.Value.amount, variant.Value.quality, variant.Value.stateBase64, amount)
                        : await m_Vault.TakeTeamAsync(user, itemId, amount))
                    : (variant.HasValue
                        ? await m_Vault.TakeVariantAsync(user, itemId, variant.Value.amount, variant.Value.quality, variant.Value.stateBase64, amount)
                        : await m_Vault.TakeAsync(user, itemId, amount));
                return taken == 0
                    ? PlayerActionResult.Fail(m_Tr.Format(lang, "You have no {0} in the vault.", name))
                    : PlayerActionResult.Ok(m_Tr.Format(lang, "Took {0}× {1}.", taken, name));
            }

            if (mode == "movetoteam" || mode == "movetopersonal")
            {
                var moved = mode == "movetoteam"
                    ? await m_Vault.MovePersonalToTeamAsync(
                        user, itemId, amount, variant?.amount, variant?.quality, variant?.stateBase64)
                    : await m_Vault.MoveTeamToPersonalAsync(
                        user, itemId, amount, variant?.amount, variant?.quality, variant?.stateBase64);
                if (moved.Moved == 0)
                {
                    return PlayerActionResult.Fail(moved.CapacityReached
                        ? m_Tr.Resolve("Destination vault is full.", lang)
                        : m_Tr.Format(lang, "You have no {0} in the vault.", name));
                }
                var moveMessage = m_Tr.Format(lang, "Moved {0}× {1} to the other vault.", moved.Moved, name);
                if (moved.CapacityReached)
                    moveMessage += " " + m_Tr.Resolve("Destination vault is full.", lang);
                return PlayerActionResult.Ok(moveMessage);
            }

            return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
        }

        private string PersonalPurchaseError(TeamVaultPurchaseResult result, string lang)
        {
            switch (result.Status)
            {
                case TeamVaultPurchaseStatus.EconomyUnavailable:
                    return m_Tr.Resolve("The economy plugin is required to buy vault capacity.", lang);
                case TeamVaultPurchaseStatus.MaximumReached:
                    return m_Tr.Resolve("The personal vault is already at its maximum capacity.", lang);
                case TeamVaultPurchaseStatus.StaleRequest:
                    return m_Tr.Resolve("The personal vault changed; refresh and try again.", lang);
                case TeamVaultPurchaseStatus.InvalidConfiguration:
                    return m_Tr.Resolve("Personal vault purchasing is not configured correctly.", lang);
                case TeamVaultPurchaseStatus.Disabled:
                    return m_Tr.Resolve("Personal vault capacity purchasing is disabled.", lang);
                default:
                    return string.IsNullOrWhiteSpace(result.Error)
                        ? m_Tr.Resolve("The personal vault capacity purchase failed.", lang)
                        : result.Error!;
            }
        }

        private string PurchaseError(TeamVaultPurchaseResult result, string lang)
        {
            switch (result.Status)
            {
                case TeamVaultPurchaseStatus.NotInTeam:
                    return m_Tr.Resolve("Join or create a party to use the team vault.", lang);
                case TeamVaultPurchaseStatus.EconomyUnavailable:
                    return m_Tr.Resolve("The economy plugin is required to buy team vault capacity.", lang);
                case TeamVaultPurchaseStatus.MaximumReached:
                    return m_Tr.Resolve("The team vault is already at its maximum capacity.", lang);
                case TeamVaultPurchaseStatus.StaleRequest:
                    return m_Tr.Resolve("The team vault changed; refresh and try again.", lang);
                case TeamVaultPurchaseStatus.InvalidConfiguration:
                    return m_Tr.Resolve("Team vault purchasing is not configured correctly.", lang);
                case TeamVaultPurchaseStatus.Disabled:
                    return m_Tr.Resolve("Team vault capacity purchasing is disabled.", lang);
                default:
                    return string.IsNullOrWhiteSpace(result.Error)
                        ? m_Tr.Resolve("The team vault capacity purchase failed.", lang)
                        : result.Error!;
            }
        }

        // ----- action-key encoding -----
        // Variant keys optionally append the carried inventory page. Summary cards use id@page.
        // Vault-content keys keep the historical format because they have no carried source.
        internal static string VariantKey(ItemVariant v, bool showsQuality)
        {
            var key = v.ItemId.ToString(CultureInfo.InvariantCulture)
                + "|" + ((int)v.Amount).ToString(CultureInfo.InvariantCulture)
                + "|" + (showsQuality ? ((int)v.Quality).ToString(CultureInfo.InvariantCulture) : "*")
                + "|" + v.State;
            return IsCarriedInventoryPage(v.InventoryPage)
                ? key + "|" + v.InventoryPage.ToString(CultureInfo.InvariantCulture)
                : key;
        }

        internal static (
            ushort itemId, byte amount, byte? quality, string stateBase64, byte[] state,
            byte? inventoryPage)? ParseVariant(string cardKey)
        {
            if (cardKey.IndexOf("|", StringComparison.Ordinal) < 0)
            {
                return null;
            }

            var parts = cardKey.Split(new[] { "|" }, 5, StringSplitOptions.None);
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

            byte? inventoryPage = null;
            if (parts.Length == 5)
            {
                if (!byte.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)
                    || !IsCarriedInventoryPage(page))
                {
                    return null;
                }
                inventoryPage = page;
            }

            return (id, amount, quality, stateBase64, state, inventoryPage);
        }

        private static ushort ParseItemId(string cardKey)
        {
            var separator = cardKey.IndexOf("@", StringComparison.Ordinal);
            var rawId = separator < 0 ? cardKey : cardKey.Substring(0, separator);
            return ushort.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : (ushort)0;
        }

        private static byte? ParseInventoryPage(string cardKey)
        {
            var separator = cardKey.IndexOf("@", StringComparison.Ordinal);
            if (separator < 0
                || !byte.TryParse(cardKey.Substring(separator + 1), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var page)
                || !IsCarriedInventoryPage(page))
            {
                return null;
            }
            return page;
        }

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;

        private async Task<bool> CanAsync(UnturnedUser user, string permission)
            => await m_Permissions.CheckPermissionAsync(user, permission) == PermissionGrantResult.Grant;
    }
}
