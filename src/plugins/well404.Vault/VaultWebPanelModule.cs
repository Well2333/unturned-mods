using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using OpenMod.Extensions.Games.Abstractions.Items;
using UnturnedMods.Shared.Items;
using UnturnedMods.Shared.WebPanel;

namespace well404.Vault
{
    /// <summary>
    /// Builds the Vault's <see cref="WebPanelModule"/>: capacity settings (base + per-permission
    /// tiers), purchasable capacity, unified personal/team inspection, and recovery quarantine.
    /// </summary>
    internal static class VaultWebPanelModule
    {
        public const string ModuleId = "well404.vault";
        private static readonly WebUiExtension s_Ui = WebUiExtension.FromEmbeddedResources(
            typeof(VaultWebPanelModule).Assembly, "admin-ui.html", "admin-ui.css", "admin-ui.js");

        public static WebPanelModule Create(VaultConfigStore store, VaultService vault, IItemDirectory itemDirectory, IUserManager userManager)
        {
            var settings = new WebPanelAction(
                id: "settings",
                label: "Vault",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveSettings(store, request)),
                fields: new[]
                {
                    new WebField("maxSlots", "Capacity (grid cells)", WebFieldType.Number, required: true,
                        placeholder: "e.g. 200 — each item costs its size_x×size_y footprint"),
                    new WebField("tiers", "Capacity tiers", WebFieldType.Text,
                        placeholder: "permission=capacity, comma-separated: e.g. well404.vault.size.vip=400"),
                    new WebField("personalPurchaseEnabled", "Players can buy personal capacity", WebFieldType.Boolean),
                    new WebField("personalMaxSlots", "Personal maximum capacity", WebFieldType.Number, required: true),
                    new WebField("personalPurchaseSlots", "Personal slots per purchase", WebFieldType.Number, required: true),
                    new WebField("personalPurchasePrice", "Personal price per purchase", WebFieldType.Number, required: true),
                    new WebField("teamEnabled", "Team vault enabled", WebFieldType.Boolean),
                    new WebField("teamBaseSlots", "Team base capacity", WebFieldType.Number, required: true),
                    new WebField("teamMaxSlots", "Team maximum capacity", WebFieldType.Number, required: true,
                        placeholder: "Default: 5000"),
                    new WebField("teamPurchaseEnabled", "Members can buy capacity", WebFieldType.Boolean),
                    new WebField("teamPurchaseSlots", "Slots per purchase", WebFieldType.Number, required: true),
                    new WebField("teamPurchasePrice", "Price per purchase", WebFieldType.Number, required: true)
                },
                description: "Base personal capacity, permission tiers, and purchasable personal/team capacity. Administrators edit a currently viewed vault capacity in Vault inspection.",
                loader: () =>
                {
                    var personal = store.PersonalPurchase;
                    var team = store.TeamVault;
                    return Task.FromResult((IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                    {
                        ["maxSlots"] = store.MaxSlots.ToString(CultureInfo.InvariantCulture),
                        ["tiers"] = FormatTiers(store.Tiers),
                        ["personalPurchaseEnabled"] = personal.Enabled ? "true" : "false",
                        ["personalMaxSlots"] = personal.MaxSlots.ToString(CultureInfo.InvariantCulture),
                        ["personalPurchaseSlots"] = personal.SlotsPerPurchase.ToString(CultureInfo.InvariantCulture),
                        ["personalPurchasePrice"] = personal.Price.ToString(CultureInfo.InvariantCulture),
                        ["teamEnabled"] = team.Enabled ? "true" : "false",
                        ["teamBaseSlots"] = team.BaseSlots.ToString(CultureInfo.InvariantCulture),
                        ["teamMaxSlots"] = team.MaxSlots.ToString(CultureInfo.InvariantCulture),
                        ["teamPurchaseEnabled"] = team.Purchase.Enabled ? "true" : "false",
                        ["teamPurchaseSlots"] = team.Purchase.SlotsPerPurchase.ToString(CultureInfo.InvariantCulture),
                        ["teamPurchasePrice"] = team.Purchase.Price.ToString(CultureInfo.InvariantCulture)
                    });
                });

            var owners = new WebPanelAction(
                id: "owners",
                label: "Vault owners",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(WebActionResult.Fail("Read-only action.")),
                description: "Every Steam ID with at least one stored row, including last known game and Steam names.",
                recordsLoader: () => LoadOwnerRecordsAsync(vault, userManager),
                keyField: "steamId",
                hidden: true);

            var teams = new WebPanelAction(
                id: "teams",
                label: "Team vaults",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(WebActionResult.Fail("Read-only action.")),
                description: "Party-owned shared vaults, including their stable owner key, capacity and current usage.",
                recordsLoader: () => Task.FromResult(LoadTeamRecords(vault)),
                keyField: "ownerKey",
                hidden: true);

            var pendingPurchases = new WebPanelAction(
                id: "pendingpurchases",
                label: "Pending vault capacity purchases",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(WebActionResult.Fail("Read-only action.")),
                description: "Durable purchases waiting for debit, membership validation, completion, or refund.",
                recordsLoader: () => Task.FromResult(LoadPendingPurchaseRecords(vault)),
                keyField: "operationId",
                hidden: true);

            var purchaseResolutions = new WebPanelAction(
                id: "purchaseresolutions",
                label: "Vault purchase resolution audit",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(WebActionResult.Fail("Read-only action.")),
                description: "Append-only administrator resolution history for quarantined purchases.",
                recordsLoader: () => Task.FromResult(LoadPurchaseResolutionRecords(vault)),
                keyField: "id",
                hidden: true);

            var resolvePurchase = new WebPanelAction(
                id: "resolvepurchase",
                label: "Resolve quarantined vault purchase",
                kind: WebActionKind.Form,
                handler: request => ResolvePurchaseAsync(vault, request),
                description: "Explicitly confirmed, audited CAS resolution of one quarantined purchase.",
                hidden: true);

            var interruptedTransfers = new WebPanelAction(
                id: "interruptedtransfers",
                label: "Interrupted vault transfers",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(WebActionResult.Fail("Read-only action.")),
                description: "Quarantined cross-system transfers with exact stored item payloads. Never auto-replayed.",
                recordsLoader: () => Task.FromResult(LoadInterruptedTransferRecords(vault)),
                keyField: "operationId",
                hidden: true);

            var inventory = new WebPanelAction(
                id: "inventory",
                label: "Vault contents",
                kind: WebActionKind.Table,
                handler: request => LoadInventoryAsync(vault, itemDirectory, request),
                description: "Load the exact SQLite rows stored for one personal or team vault.",
                hidden: true);

            var containerInfo = new WebPanelAction(
                id: "containerinfo",
                label: "Vault container information",
                kind: WebActionKind.Table,
                handler: request => Task.FromResult(LoadContainerInfo(vault, request)),
                hidden: true);

            var addItem = new WebPanelAction(
                id: "additem",
                label: "Add stored item",
                kind: WebActionKind.Form,
                handler: request => AddItemAsync(vault, request),
                hidden: true);

            var updateItem = new WebPanelAction(
                id: "updateitem",
                label: "Update stored item",
                kind: WebActionKind.Form,
                handler: request => UpdateItemAsync(vault, request),
                hidden: true);

            var deleteItem = new WebPanelAction(
                id: "deleteitem",
                label: "Delete stored item",
                kind: WebActionKind.Form,
                handler: request => DeleteItemAsync(vault, request),
                hidden: true);

            var deleteItems = new WebPanelAction(
                id: "deleteitems",
                label: "Delete matching stored items",
                kind: WebActionKind.Form,
                handler: request => DeleteItemsAsync(vault, request),
                hidden: true);

            var setCapacity = new WebPanelAction(
                id: "setcapacity",
                label: "Set vault capacity",
                kind: WebActionKind.Form,
                handler: request => Task.FromResult(SetCapacity(vault, request)),
                hidden: true);

            return new WebPanelModule(ModuleId, "Vault",
                new[] { settings, owners, teams, pendingPurchases, purchaseResolutions, resolvePurchase,
                    interruptedTransfers, inventory, containerInfo, addItem, updateItem, deleteItem, deleteItems, setCapacity },
                icon: "🧳", ui: s_Ui);
        }

        private static IReadOnlyList<WebRecord> LoadPendingPurchaseRecords(VaultService vault)
        {
            var records = new List<WebRecord>();
            foreach (var purchase in vault.PendingTeamPurchases)
            {
                records.Add(new WebRecord(
                    purchase.OperationId,
                    purchase.OperationId,
                    new Dictionary<string, string>
                    {
                        ["operationId"] = purchase.OperationId,
                        ["buyerSteamId"] = purchase.BuyerSteamId,
                        ["teamKey"] = purchase.TeamKey,
                        ["ownerKind"] = purchase.Container.KindKey,
                        ["ownerKey"] = purchase.Container.Key,
                        ["economyMode"] = purchase.EconomyMode,
                        ["slots"] = purchase.Slots.ToString(CultureInfo.InvariantCulture),
                        ["price"] = purchase.Price.ToString(CultureInfo.InvariantCulture),
                        ["state"] = purchase.State,
                        ["createdUtc"] = purchase.CreatedUtc,
                        ["updatedUtc"] = purchase.UpdatedUtc
                    }));
            }
            return records;
        }

        private static IReadOnlyList<WebRecord> LoadPurchaseResolutionRecords(VaultService vault)
        {
            return vault.PurchaseResolutionAudit.Select(entry => new WebRecord(
                entry.Id.ToString(CultureInfo.InvariantCulture),
                entry.OperationId,
                new Dictionary<string, string>
                {
                    ["id"] = entry.Id.ToString(CultureInfo.InvariantCulture),
                    ["operationId"] = entry.OperationId,
                    ["action"] = entry.Action,
                    ["actor"] = entry.Actor,
                    ["note"] = entry.Note,
                    ["fromState"] = entry.FromState,
                    ["toState"] = entry.ToState,
                    ["createdUtc"] = entry.CreatedUtc
                })).ToList();
        }

        private static async Task<WebActionResult> ResolvePurchaseAsync(
            VaultService vault, WebActionRequest request)
        {
            var operationId = request.Get("operationId") ?? string.Empty;
            var action = request.Get("resolution") ?? string.Empty;
            var confirmation = request.Get("confirmation") ?? string.Empty;
            var note = request.Get("note") ?? string.Empty;
            if (operationId.Length < 1 || !string.Equals(confirmation, operationId, StringComparison.Ordinal))
                return WebActionResult.Fail("Confirmation must exactly match the operation ID.");
            if (note.Trim().Length < 3)
                return WebActionResult.Fail("Enter an audit note with at least 3 characters.");
            if (!string.Equals(action, "abort-unpaid", StringComparison.Ordinal)
                && !string.Equals(action, "retry-refund", StringComparison.Ordinal)
                && !string.Equals(action, "confirm-refunded", StringComparison.Ordinal))
                return WebActionResult.Fail("Choose a valid resolution action.");

            var result = await vault.ResolvePendingTeamPurchaseAsync(operationId, action, note.Trim());
            return result.Success ? WebActionResult.Ok(result.Message) : WebActionResult.Fail(result.Message);
        }

        private static IReadOnlyList<WebRecord> LoadInterruptedTransferRecords(VaultService vault)
        {
            var records = new List<WebRecord>();
            foreach (var transfer in vault.InterruptedTransfers)
            {
                var lines = new List<string>();
                foreach (var entry in transfer.Items)
                {
                    var item = entry.Item;
                    lines.Add(
                        "[" + entry.Stage + "] #" + item.ItemId.ToString(CultureInfo.InvariantCulture)
                        + " amount=" + item.Amount.ToString(CultureInfo.InvariantCulture)
                        + " quality=" + item.Quality.ToString(CultureInfo.InvariantCulture)
                        + " slots=" + item.SlotCost.ToString(CultureInfo.InvariantCulture)
                        + " max=" + item.MaxAmount.ToString(CultureInfo.InvariantCulture)
                        + " state=" + item.State);
                }
                records.Add(new WebRecord(
                    transfer.OperationId,
                    transfer.OperationId,
                    new Dictionary<string, string>
                    {
                        ["operationId"] = transfer.OperationId,
                        ["ownerKind"] = transfer.Container.KindKey,
                        ["ownerKey"] = transfer.Container.Key,
                        ["actorSteamId"] = transfer.ActorSteamId,
                        ["direction"] = transfer.Direction,
                        ["state"] = transfer.State,
                        ["createdUtc"] = transfer.CreatedUtc,
                        ["updatedUtc"] = transfer.UpdatedUtc,
                        ["itemCount"] = transfer.Items.Count.ToString(CultureInfo.InvariantCulture),
                        ["items"] = string.Join("\n", lines)
                    }));
            }
            return records;
        }

        private static IReadOnlyList<WebRecord> LoadTeamRecords(VaultService vault)
        {
            var records = new List<WebRecord>();
            foreach (var team in vault.TeamContainers)
            {
                var label = string.IsNullOrWhiteSpace(team.DisplayName)
                    ? team.Container.Key
                    : team.DisplayName;
                var effectiveCapacity = team.Capacity;
                records.Add(new WebRecord(
                    team.Container.Key,
                    label,
                    new Dictionary<string, string>
                    {
                        ["ownerKey"] = team.Container.Key,
                        ["name"] = team.DisplayName,
                        ["usedSlots"] = team.UsedSlots.ToString(CultureInfo.InvariantCulture),
                        ["capacity"] = effectiveCapacity.ToString(CultureInfo.InvariantCulture),
                        ["configuredCapacity"] = team.Capacity.ToString(CultureInfo.InvariantCulture),
                        ["purchasedSlots"] = team.PurchasedSlots.ToString(CultureInfo.InvariantCulture),
                        ["version"] = team.Version.ToString(CultureInfo.InvariantCulture),
                        ["status"] = team.Status,
                        ["pendingPurchases"] = team.PendingPurchases.ToString(CultureInfo.InvariantCulture)
                    }));
            }
            return records;
        }

        private static async Task<IReadOnlyList<WebRecord>> LoadOwnerRecordsAsync(
            VaultService vault, IUserManager userManager)
        {
            await UniTask.SwitchToMainThread();
            var online = await userManager.GetUsersAsync(KnownActorTypes.Player);
            await UniTask.SwitchToMainThread();
            foreach (var user in online)
            {
                if (user is UnturnedUser unturned)
                {
                    vault.TouchOwner(unturned);
                }
            }

            var records = new List<WebRecord>();
            foreach (var owner in vault.Owners)
            {
                var label = !string.IsNullOrWhiteSpace(owner.GameName)
                    ? owner.GameName
                    : (!string.IsNullOrWhiteSpace(owner.SteamName) ? owner.SteamName : owner.SteamId);
                records.Add(new WebRecord(
                    owner.SteamId,
                    label,
                    new Dictionary<string, string>
                    {
                        ["steamId"] = owner.SteamId,
                        ["gameName"] = owner.GameName,
                        ["steamName"] = owner.SteamName,
                        ["rows"] = owner.Rows.ToString(CultureInfo.InvariantCulture),
                        ["usedSlots"] = owner.UsedSlots.ToString(CultureInfo.InvariantCulture)
                    },
                    new[]
                    {
                        owner.SteamId,
                        owner.Rows.ToString(CultureInfo.InvariantCulture) + " rows",
                        owner.UsedSlots.ToString(CultureInfo.InvariantCulture) + " slots"
                    }));
            }

            return records;
        }

        private static async Task<WebActionResult> AddItemAsync(VaultService vault, WebActionRequest request)
        {
            var container = ContainerOf(request);
            if (!container.HasValue
                || !ushort.TryParse(request.Get("itemId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                || itemId == 0
                || !byte.TryParse(request.Get("amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
                || amount == 0
                || !byte.TryParse(request.Get("quality"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality)
                || quality > 100
                || !int.TryParse(request.Get("count"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                || count < 1 || count > 1000)
            {
                return WebActionResult.Fail("Enter a valid vault owner, item ID, amount (1–255), quality (0–100), and copy count (1–1000).");
            }

            var added = await vault.AddStoredItemsAsync(container.Value, itemId, amount, quality, count);
            return added > 0
                ? WebActionResult.Ok("Added " + added.ToString(CultureInfo.InvariantCulture)
                    + " stored row(s) with item ID " + itemId.ToString(CultureInfo.InvariantCulture) + ".")
                : WebActionResult.Fail("Item asset not found.");
        }

        private static async Task<WebActionResult> LoadInventoryAsync(
            VaultService vault, IItemDirectory itemDirectory, WebActionRequest request)
        {
            var container = ContainerOf(request);
            if (!container.HasValue)
                return WebActionResult.Fail("Choose a valid personal or team vault.");

            var names = await LocalizedItemCatalog.BuildAsync(itemDirectory);
            var rows = new List<IReadOnlyList<string>>();
            foreach (var item in vault.Get(container.Value))
            {
                rows.Add(new[]
                {
                    item.RecordId.ToString(CultureInfo.InvariantCulture),
                    item.ItemId.ToString(CultureInfo.InvariantCulture),
                    LocalizedItemCatalog.DisplayName(item.ItemId, names, request.Language),
                    item.Amount.ToString(CultureInfo.InvariantCulture),
                    item.Quality.ToString(CultureInfo.InvariantCulture),
                    item.SlotCost.ToString(CultureInfo.InvariantCulture),
                    item.MaxAmount.ToString(CultureInfo.InvariantCulture)
                });
            }

            var snapshot = vault.GetContainer(container.Value);
            var message = rows.Count.ToString(CultureInfo.InvariantCulture) + " stored row(s), "
                + snapshot.UsedSlots.ToString(CultureInfo.InvariantCulture) + " / "
                + snapshot.Capacity.ToString(CultureInfo.InvariantCulture) + " grid cell(s).";
            return WebActionResult.Table(
                new[] { "Record ID", "Item ID", "Name", "Amount", "Quality", "Grid cells", "Max amount",
                    "Owner kind", "Owner key", "Capacity", "Used slots" },
                rows.Select(row => (IReadOnlyList<string>)row.Concat(new[]
                {
                    container.Value.KindKey, container.Value.Key,
                    snapshot.Capacity.ToString(CultureInfo.InvariantCulture),
                    snapshot.UsedSlots.ToString(CultureInfo.InvariantCulture)
                }).ToArray()).ToList(),
                message);
        }

        private static WebActionResult LoadContainerInfo(VaultService vault, WebActionRequest request)
        {
            var container = ContainerOf(request);
            if (!container.HasValue)
                return WebActionResult.Fail("Choose a valid personal or team vault.");
            var snapshot = vault.GetContainer(container.Value);
            return WebActionResult.Table(
                new[] { "Owner kind", "Owner key", "Capacity", "Used slots" },
                new[] { (IReadOnlyList<string>)new[]
                {
                    container.Value.KindKey, container.Value.Key,
                    snapshot.Capacity.ToString(CultureInfo.InvariantCulture),
                    snapshot.UsedSlots.ToString(CultureInfo.InvariantCulture)
                } },
                "Vault container information.");
        }

        private static async Task<WebActionResult> UpdateItemAsync(VaultService vault, WebActionRequest request)
        {
            var container = ContainerOf(request);
            if (!container.HasValue
                || !long.TryParse(request.Get("recordId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recordId)
                || recordId < 1
                || !ushort.TryParse(request.Get("itemId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                || itemId == 0
                || !byte.TryParse(request.Get("amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
                || amount == 0
                || !byte.TryParse(request.Get("quality"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality)
                || quality > 100)
            {
                return WebActionResult.Fail("Enter a valid vault owner, record ID, item ID, amount (1–255), and quality (0–100).");
            }

            return await vault.UpdateStoredItemAsync(container.Value, recordId, itemId, amount, quality)
                ? WebActionResult.Ok("Stored item updated; its opaque state data was preserved.")
                : WebActionResult.Fail("Stored row not found in this vault.");
        }

        private static async Task<WebActionResult> DeleteItemAsync(VaultService vault, WebActionRequest request)
        {
            var container = ContainerOf(request);
            if (!container.HasValue
                || !long.TryParse(request.Get("recordId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recordId)
                || recordId < 1)
            {
                return WebActionResult.Fail("Enter a valid vault owner and record ID.");
            }

            return await vault.DeleteStoredItemAsync(container.Value, recordId)
                ? WebActionResult.Ok("Stored row deleted.")
                : WebActionResult.Fail("Stored row not found in this vault.");
        }

        private static async Task<WebActionResult> DeleteItemsAsync(VaultService vault, WebActionRequest request)
        {
            var container = ContainerOf(request);
            if (!container.HasValue
                || !ushort.TryParse(request.Get("itemId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                || itemId == 0)
            {
                return WebActionResult.Fail("Enter a valid vault owner and item ID.");
            }

            var count = await vault.DeleteStoredItemsAsync(container.Value, itemId);
            return count > 0
                ? WebActionResult.Ok("Deleted " + count.ToString(CultureInfo.InvariantCulture)
                    + " stored row(s) with item ID " + itemId.ToString(CultureInfo.InvariantCulture) + ".")
                : WebActionResult.Fail("No matching stored rows were found in this vault.");
        }

        private static WebActionResult SetCapacity(VaultService vault, WebActionRequest request)
        {
            var container = ContainerOf(request);
            if (!container.HasValue
                || !int.TryParse(request.Get("capacity"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var capacity)
                || capacity < 1 || capacity > VaultCapacityLimits.Maximum)
                return WebActionResult.Fail("Choose a vault and enter a capacity from 1 to 1,000,000.");

            var updated = vault.SetContainerCapacity(container.Value, capacity);
            return updated == null
                ? WebActionResult.Fail("Capacity cannot be lower than the vault current usage.")
                : WebActionResult.Ok("Vault capacity updated to "
                    + updated.Capacity.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static VaultContainerRef? ContainerOf(WebActionRequest request)
        {
            var kind = request.Get("ownerKind");
            var key = request.Get("ownerKey");
            if (string.Equals(kind, "team", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(key) ? (VaultContainerRef?)null : VaultContainerRef.Team(key);

            var steamId = ValidSteamId(key) ?? ValidSteamId(request.Get("steamId"));
            return steamId == null ? (VaultContainerRef?)null : VaultContainerRef.Player(steamId);
        }

        private static string? ValidSteamId(string? value)
        {
            if (value == null || value.Length != 17 || !ulong.TryParse(value, out var parsed) || parsed == 0)
            {
                return null;
            }

            return value;
        }

        private static WebActionResult SaveSettings(VaultConfigStore store, WebActionRequest request)
        {
            var raw = request.Get("maxSlots");
            if (raw == null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slots) || slots < 1 || slots > VaultCapacityLimits.Maximum)
            {
                return WebActionResult.Fail("Enter a valid capacity (a positive whole number).");
            }

            var tiers = ParseTiers(request.Get("tiers") ?? string.Empty, out var error);
            if (tiers == null)
            {
                return WebActionResult.Fail(error!);
            }

            if (!int.TryParse(request.Get("personalMaxSlots"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var personalMax)
                || personalMax < slots || personalMax > VaultCapacityLimits.Maximum
                || !int.TryParse(request.Get("personalPurchaseSlots"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var personalPurchaseSlots)
                || personalPurchaseSlots < 1 || personalPurchaseSlots > VaultCapacityLimits.Maximum
                || !decimal.TryParse(request.Get("personalPurchasePrice"), NumberStyles.Number, CultureInfo.InvariantCulture, out var personalPurchasePrice)
                || personalPurchasePrice < 0m
                || !int.TryParse(request.Get("teamBaseSlots"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var teamBase)
                || teamBase < 1 || teamBase > VaultCapacityLimits.Maximum
                || !int.TryParse(request.Get("teamMaxSlots"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var teamMax)
                || teamMax < teamBase || teamMax > VaultCapacityLimits.Maximum
                || !int.TryParse(request.Get("teamPurchaseSlots"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var purchaseSlots)
                || purchaseSlots < 1 || purchaseSlots > VaultCapacityLimits.Maximum
                || !decimal.TryParse(request.Get("teamPurchasePrice"), NumberStyles.Number, CultureInfo.InvariantCulture, out var purchasePrice)
                || purchasePrice < 0m)
            {
                return WebActionResult.Fail("Enter valid team vault capacities and purchase price.");
            }

            store.Save(slots, tiers, new PersonalVaultPurchaseSettings
            {
                Enabled = string.Equals(request.Get("personalPurchaseEnabled"), "true", StringComparison.OrdinalIgnoreCase),
                MaxSlots = personalMax,
                SlotsPerPurchase = personalPurchaseSlots,
                Price = personalPurchasePrice
            }, new TeamVaultSettings
            {
                Enabled = string.Equals(request.Get("teamEnabled"), "true", StringComparison.OrdinalIgnoreCase),
                BaseSlots = teamBase,
                MaxSlots = teamMax,
                Purchase = new TeamVaultPurchaseSettings
                {
                    Enabled = string.Equals(request.Get("teamPurchaseEnabled"), "true", StringComparison.OrdinalIgnoreCase),
                    SlotsPerPurchase = purchaseSlots,
                    Price = purchasePrice
                }
            });
            return WebActionResult.Ok("Saved.");
        }

        private static string FormatTiers(IReadOnlyDictionary<string, int> tiers)
        {
            var parts = new List<string>();
            foreach (var tier in tiers)
            {
                parts.Add(tier.Key + "=" + tier.Value.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts);
        }

        /// <summary>Parses "perm=capacity, perm:capacity" into a map. Returns null + error on a bad entry.</summary>
        private static Dictionary<string, int>? ParseTiers(string raw, out string? error)
        {
            error = null;
            var result = new Dictionary<string, int>();
            var tokens = raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tokenRaw in tokens)
            {
                var token = tokenRaw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                var parts = token.Split(new[] { '=', ':' }, 2);
                if (parts.Length != 2)
                {
                    // Arg-free so the host can localize it by key (no interpolated token).
                    error = "Bad tier format (expected permission=capacity).";
                    return null;
                }

                var perm = parts[0].Trim();
                if (perm.Length == 0
                    || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap)
                    || cap < 1 || cap > VaultCapacityLimits.Maximum)
                {
                    error = "Invalid tier capacity (a positive whole number).";
                    return null;
                }

                result[perm] = cap;
            }

            return result;
        }
    }
}
