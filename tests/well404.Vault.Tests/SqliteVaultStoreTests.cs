using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace well404.Vault.Tests
{
    public class SqliteVaultStoreTests : IDisposable
    {
        private readonly string m_File =
            Path.Combine(Path.GetTempPath(), "vaulttest-" + Guid.NewGuid().ToString("N") + ".sqlite3");

        private SqliteVaultStore NewStore()
        {
            var store = new SqliteVaultStore(m_File);
            store.Initialize();
            return store;
        }

        [Fact]
        public void AddAndGet_RoundTripsRawStateAndCapacity()
        {
            var store = NewStore();
            var state = new byte[] { 1, 2, 3, 255 };
            store.AddItems("7656119", new[]
            {
                new StoredItem
                {
                    ItemId = 363,
                    Amount = 7,
                    Quality = 83,
                    State = Convert.ToBase64String(state),
                    SlotCost = 4,
                    MaxAmount = 8
                }
            });

            var item = Assert.Single(store.Get("7656119"));
            Assert.True(item.RecordId > 0);
            Assert.Equal((ushort)363, item.ItemId);
            Assert.Equal((byte)7, item.Amount);
            Assert.Equal((byte)83, item.Quality);
            Assert.Equal(state, Convert.FromBase64String(item.State));
            Assert.Equal(4, store.UsedSlots("7656119"));
        }

        [Fact]
        public void TakeFirst_AtomicallyDeletesOnlyMatchingRow()
        {
            var store = NewStore();
            store.AddItems("p", new[]
            {
                new StoredItem { ItemId = 1, State = string.Empty },
                new StoredItem { ItemId = 2, State = string.Empty }
            });

            var taken = store.TakeFirst("p", item => item.ItemId == 2);

            Assert.NotNull(taken);
            Assert.Equal((ushort)2, taken!.ItemId);
            var remaining = Assert.Single(new SqliteVaultStore(m_File).Get("p"));
            Assert.Equal((ushort)1, remaining.ItemId);
        }

        [Fact]
        public void AddItems_InvalidState_RollsBackWholeBatch()
        {
            var store = NewStore();

            Assert.Throws<FormatException>(() => store.AddItems("p", new[]
            {
                new StoredItem { ItemId = 1, State = string.Empty },
                new StoredItem { ItemId = 2, State = "not-base64" }
            }));

            Assert.Empty(store.Get("p"));
        }

        [Fact]
        public void UpdateItem_IsPlayerScopedAndPreservesOpaqueState()
        {
            var store = NewStore();
            var state = Convert.ToBase64String(new byte[] { 4, 8, 15, 16, 23, 42 });
            store.AddItems("owner", new[]
            {
                new StoredItem { ItemId = 10, Amount = 1, Quality = 90, State = state, SlotCost = 2, MaxAmount = 0 }
            });
            var row = Assert.Single(store.Get("owner"));

            Assert.False(store.UpdateItem("other", row.RecordId, 11, 2, 70, 4, 8));
            Assert.True(store.UpdateItem("owner", row.RecordId, 11, 2, 70, 4, 8));

            var updated = Assert.Single(store.Get("owner"));
            Assert.Equal((ushort)11, updated.ItemId);
            Assert.Equal((byte)2, updated.Amount);
            Assert.Equal((byte)70, updated.Quality);
            Assert.Equal(4, updated.SlotCost);
            Assert.Equal((byte)8, updated.MaxAmount);
            Assert.Equal(state, updated.State);
        }

        [Fact]
        public void DeleteItem_IsPlayerScoped()
        {
            var store = NewStore();
            store.AddItems("owner", new[] { new StoredItem { ItemId = 10, State = string.Empty } });
            var row = Assert.Single(store.Get("owner"));

            Assert.False(store.DeleteItem("other", row.RecordId));
            Assert.Single(store.Get("owner"));
            Assert.True(store.DeleteItem("owner", row.RecordId));
            Assert.Empty(store.Get("owner"));
        }

        [Fact]
        public void DeleteItems_DeletesOnlyMatchingItemForOnePlayer()
        {
            var store = NewStore();
            store.AddItems("owner", new[]
            {
                new StoredItem { ItemId = 60332, State = string.Empty },
                new StoredItem { ItemId = 60332, State = string.Empty },
                new StoredItem { ItemId = 15, State = string.Empty }
            });
            store.AddItems("other", new[] { new StoredItem { ItemId = 60332, State = string.Empty } });

            Assert.Equal(2, store.DeleteItems("owner", 60332));
            Assert.Single(store.Get("owner"));
            Assert.Equal((ushort)15, store.Get("owner")[0].ItemId);
            Assert.Single(store.Get("other"));
            Assert.Equal(0, store.DeleteItems("owner", 60332));
        }

        [Fact]
        public void Owners_ListOnlyPlayersWithStockAndKeepLastKnownNames()
        {
            var store = NewStore();
            store.TouchOwner("76561198000000001", "角色甲", "Steam A");
            store.TouchOwner("76561198000000002", "No stock", "Steam B");
            store.AddItems("76561198000000001", new[]
            {
                new StoredItem { ItemId = 15, State = string.Empty, SlotCost = 4 },
                new StoredItem { ItemId = 16, State = string.Empty, SlotCost = 2 }
            });

            var owner = Assert.Single(store.GetOwners());
            Assert.Equal("76561198000000001", owner.SteamId);
            Assert.Equal("角色甲", owner.GameName);
            Assert.Equal("Steam A", owner.SteamName);
            Assert.Equal(2, owner.Rows);
            Assert.Equal(6, owner.UsedSlots);

            store.TouchOwner(owner.SteamId, string.Empty, "Steam A2");
            owner = Assert.Single(store.GetOwners());
            Assert.Equal("角色甲", owner.GameName);
            Assert.Equal("Steam A2", owner.SteamName);
        }

        [Fact]
        public void Database_UsesWalJournalMode()
        {
            NewStore();
            using var connection = new SqliteConnection("Data Source=" + m_File);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", (string)command.ExecuteScalar()!);
        }

        [Fact]
        public void TeamContainers_IsolateItemsAndTrackCapacityAtomically()
        {
            var store = NewStore();
            var alpha = VaultContainerRef.Team("unturned:test:100");
            var beta = VaultContainerRef.Team("unturned:test:200");
            var item = new StoredItem { ItemId = 15, State = string.Empty, SlotCost = 4 };

            Assert.True(store.TryAddItems(alpha, "Alpha", 8, 5000, new[] { item, item }));
            Assert.False(store.TryAddItems(alpha, "Alpha", 8, 5000, new[] { item }));
            Assert.True(store.TryAddItems(beta, "Beta", 8, 5000, new[] { item }));

            Assert.Equal(2, store.Get(alpha).Count);
            Assert.Single(store.Get(beta));
            Assert.Equal(8, store.UsedSlots(alpha));
            Assert.Equal(4, store.UsedSlots(beta));
        }

        [Fact]
        public void TeamPurchase_CompletesOnceAndRaisesCapacity()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:100");
            var operation = store.CreatePendingTeamPurchase(team, "Alpha", 200, "7656119", 100, 1000m)
                ?? throw new InvalidOperationException("Expected purchase creation to succeed.");

            Assert.Single(store.GetPendingTeamPurchases());
            Assert.Equal("debiting", store.GetTeamPurchaseState(operation));
            Assert.True(store.MarkTeamPurchaseDebited(operation));
            Assert.True(store.MarkTeamPurchaseReady(operation));
            Assert.Equal(300, store.CompleteTeamPurchase(operation, 5000));
            Assert.Equal(300, store.CompleteTeamPurchase(operation, 5000));
            Assert.Empty(store.GetPendingTeamPurchases());
            Assert.Equal(300, store.GetOrCreateContainer(team, "Alpha", 200).Capacity);
        }

        [Fact]
        public void TeamPurchase_ExpectedVersionRejectsDuplicateRequest()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:versioned-purchase");
            var version = store.GetOrCreateContainer(team, "Versioned", 200).PurchaseVersion;

            var first = store.CreatePendingTeamPurchase(
                team, "Versioned", 200, "7656119", 100, 1000m, version);
            var duplicate = store.CreatePendingTeamPurchase(
                team, "Versioned", 200, "7656119", 100, 1000m, version);

            Assert.NotNull(first);
            Assert.Null(duplicate);
            Assert.Single(store.GetPendingTeamPurchases());
            Assert.Equal(1, store.PendingTeamPurchaseCount(team));
        }

        [Fact]
        public void TeamPurchaseVersion_IsIndependentFromInventoryMutations()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:purchase-version");
            var before = store.GetOrCreateContainer(team, "Purchase Version", 200);

            Assert.True(store.TryAddItems(
                team,
                "Purchase Version",
                200,
                5000,
                new[] { new StoredItem { ItemId = 15, State = string.Empty, SlotCost = 1 } }));

            var after = store.GetOrCreateContainer(team, "Purchase Version", 200);
            Assert.True(after.Version > before.Version);
            Assert.Equal(before.PurchaseVersion, after.PurchaseVersion);
        }

        [Fact]
        public void CommandStylePurchase_StillAdvancesPurchaseVersion()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:command-version");
            var before = store.GetOrCreateContainer(team, "Command Version", 200);

            Assert.NotNull(store.CreatePendingTeamPurchase(
                team, "Command Version", 200, "7656119", 100, 1000m));
            Assert.Null(store.CreatePendingTeamPurchase(
                team, "Command Version", 200, "7656119", 100, 1000m));

            var after = store.GetOrCreateContainer(team, "Command Version", 200);
            Assert.Equal(before.PurchaseVersion + 1, after.PurchaseVersion);
        }

        [Fact]
        public void AdminCapacityEdit_InvalidatesOutstandingPurchaseQuote()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:admin-capacity-version");
            var before = store.GetOrCreateContainer(team, "Admin Capacity", 200);

            var updated = store.SetContainerCapacity(team, "Admin Capacity", 200, 350);

            Assert.NotNull(updated);
            Assert.Equal(350, updated!.Capacity);
            Assert.Equal(before.PurchaseVersion + 1, updated.PurchaseVersion);
            Assert.Null(store.CreatePendingTeamPurchase(
                team, "Admin Capacity", 200, "7656119", 100, 500m, before.PurchaseVersion));
        }

        [Fact]
        public void CompletedPurchase_CannotBeClaimedForRefund()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:completed-cas");
            var operation = store.CreatePendingTeamPurchase(team, "Completed", 200, "7656119", 100, 1000m)
                ?? throw new InvalidOperationException("Expected purchase creation to succeed.");
            Assert.True(store.MarkTeamPurchaseDebited(operation));
            Assert.True(store.MarkTeamPurchaseReady(operation));
            Assert.Equal(300, store.CompleteTeamPurchase(operation, 5000));

            Assert.False(store.MarkTeamPurchaseRefundPending(operation));
            Assert.False(store.MarkTeamPurchaseRefunded(operation));
            Assert.Equal("completed", store.GetTeamPurchaseState(operation));
        }

        [Fact]
        public void TeamPurchase_RefundedOperationCannotComplete()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:refund");
            var operation = store.CreatePendingTeamPurchase(team, "Refunded", 200, "7656119", 100, 1000m)
                ?? throw new InvalidOperationException("Expected purchase creation to succeed.");

            Assert.True(store.MarkTeamPurchaseRefundPending(operation));
            Assert.True(store.MarkTeamPurchaseRefunded(operation));

            Assert.Equal(0, store.CompleteTeamPurchase(operation, 5000));
            Assert.Equal(200, store.GetOrCreateContainer(team, "Refunded", 200).Capacity);
            Assert.Empty(store.GetPendingTeamPurchases());
        }

        [Fact]
        public void ZeroSlotCost_IsNormalizedAndDoesNotDriftContainerUsage()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:normalized");
            var item = new StoredItem { ItemId = 15, State = string.Empty, SlotCost = 0 };

            Assert.True(store.TryAddItems(team, "Normalized", 1, 5000, new[] { item }));

            Assert.Equal(1, Assert.Single(store.Get(team)).SlotCost);
            Assert.Equal(1, store.UsedSlots(team));
            Assert.False(store.TryAddItems(team, "Normalized", 1, 5000, new[] { item }));
        }

        [Fact]
        public async Task ConcurrentCapacityReservations_NeverExceedLimit()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:parallel");
            var item = new StoredItem { ItemId = 15, State = string.Empty, SlotCost = 1 };

            var attempts = Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() => store.TryAddItems(team, "Parallel", 20, 20, new[] { item })))
                .ToArray();
            var results = await Task.WhenAll(attempts);

            Assert.Equal(20, results.Count(success => success));
            Assert.Equal(20, store.UsedSlots(team));
            Assert.Equal(20, store.Get(team).Count);
        }

        [Fact]
        public void TransferAudit_TracksInterruptedCommittedAndCompletedStates()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:audit");
            var item = new StoredItem
            {
                ItemId = 15,
                Amount = 2,
                Quality = 73,
                State = Convert.ToBase64String(new byte[] { 9, 8, 7 }),
                SlotCost = 1,
                MaxAmount = 4
            };

            store.BeginTransferAudit("store-1", team, "7656119", "store", new[] { item });
            Assert.Equal(1, store.InterruptedTransferCount());
            Assert.Equal("prepared", store.GetTransferAuditState("store-1"));
            var prepared = Assert.Single(store.GetTransferAuditItems("store-1"));
            Assert.Equal(item.ItemId, prepared.ItemId);
            Assert.Equal(item.Amount, prepared.Amount);
            Assert.Equal(item.Quality, prepared.Quality);
            Assert.Equal(item.State, prepared.State);
            var quarantined = Assert.Single(store.GetInterruptedTransfers());
            Assert.Equal("store-1", quarantined.OperationId);
            Assert.Equal(team, quarantined.Container);
            Assert.Equal("7656119", quarantined.ActorSteamId);
            Assert.Equal("store", quarantined.Direction);
            var quarantinedItem = Assert.Single(quarantined.Items);
            Assert.Equal(item.State, quarantinedItem.Item.State);
            Assert.Equal("candidate", quarantinedItem.Stage);
            store.SetTransferAuditItemStage("store-1", 0, "inventory_removed");
            Assert.Equal("inventory_removed", Assert.Single(store.GetInterruptedTransfers()).Items[0].Stage);

            Assert.True(store.TryAddItems(team, "Audit", 10, 10, new[] { item }, "store-1", "7656119"));
            Assert.Equal(1, store.InterruptedTransferCount());
            Assert.Equal("database_committed", store.GetTransferAuditState("store-1"));

            store.SetTransferAuditState("store-1", "completed");
            Assert.Equal(0, store.InterruptedTransferCount());

            var taken = store.TakeMany(team, _ => true, 1, "take-1", "7656119");
            Assert.Single(taken);
            Assert.Equal(1, store.InterruptedTransferCount());
            store.SetTransferAuditState("take-1", "compensated");
            Assert.Equal(0, store.InterruptedTransferCount());
        }

        [Fact]
        public void TeamPurchase_RefundPendingIsRecoverableButCannotComplete()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:refund-pending");
            var operation = store.CreatePendingTeamPurchase(team, "Refund Pending", 200, "7656119", 100, 1000m)
                ?? throw new InvalidOperationException("Expected purchase creation to succeed.");

            Assert.True(store.MarkTeamPurchaseDebited(operation));
            Assert.True(store.MarkTeamPurchaseRefundPending(operation));

            var pending = Assert.Single(store.GetPendingTeamPurchases());
            Assert.True(pending.RefundOnly);
            Assert.Equal("refund_pending", pending.State);
            Assert.Equal(team.Key, pending.TeamKey);
            Assert.False(string.IsNullOrWhiteSpace(pending.CreatedUtc));
            Assert.False(string.IsNullOrWhiteSpace(pending.UpdatedUtc));
            Assert.Equal(0, store.CompleteTeamPurchase(operation, 5000));
            Assert.True(store.MarkTeamPurchaseRefunded(operation));
            Assert.Empty(store.GetPendingTeamPurchases());
            Assert.Equal(200, store.GetOrCreateContainer(team, "Refund Pending", 200).Capacity);
        }

        [Fact]
        public void VersionTwoDatabase_NormalizesSlotCostAndRecalculatesUsage()
        {
            using (var connection = new SqliteConnection("Data Source=" + m_File))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE vault_items (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    steam_id TEXT NOT NULL,
    owner_kind TEXT NOT NULL DEFAULT 'player',
    owner_key TEXT NOT NULL DEFAULT '',
    item_id INTEGER NOT NULL,
    amount INTEGER NOT NULL,
    quality INTEGER NOT NULL,
    state BLOB NOT NULL,
    slot_cost INTEGER NOT NULL,
    max_amount INTEGER NOT NULL
);
CREATE TABLE vault_containers (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    owner_kind TEXT NOT NULL,
    owner_key TEXT NOT NULL,
    display_name TEXT NOT NULL DEFAULT '',
    base_capacity INTEGER NOT NULL,
    purchased_slots INTEGER NOT NULL DEFAULT 0,
    used_slots INTEGER NOT NULL DEFAULT 0,
    version INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'active',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE(owner_kind, owner_key)
);
INSERT INTO vault_items(
    steam_id, owner_kind, owner_key, item_id, amount, quality, state, slot_cost, max_amount)
VALUES('p', 'player', 'p', 15, 1, 100, X'', 0, 0);
INSERT INTO vault_containers(
    owner_kind, owner_key, display_name, base_capacity, purchased_slots,
    used_slots, version, status, created_utc, updated_utc)
VALUES('player', 'p', '', 200, 0, 0, 0, 'active', 'now', 'now');
PRAGMA user_version = 2;";
                command.ExecuteNonQuery();
            }

            var store = new SqliteVaultStore(m_File);
            store.Initialize();

            Assert.Equal(1, Assert.Single(store.Get("p")).SlotCost);
            Assert.Equal(1, store.UsedSlots("p"));
            using var reopened = new SqliteConnection("Data Source=" + m_File);
            reopened.Open();
            using var version = reopened.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(6L, (long)version.ExecuteScalar()!);
            using var columns = reopened.CreateCommand();
            columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('vault_containers') WHERE name = 'admin_adjustment';";
            Assert.Equal(1L, (long)columns.ExecuteScalar()!);
        }

        [Fact]
        public void ExistingPersonalRows_MigrateIntoContainerOwnership()
        {
            var store = NewStore();
            store.AddItems("p", new[] { new StoredItem { ItemId = 15, State = string.Empty, SlotCost = 4 } });

            var reopened = new SqliteVaultStore(m_File);
            reopened.Initialize();

            Assert.Single(reopened.Get("p"));
            Assert.Equal(4, reopened.UsedSlots("p"));
            Assert.Empty(reopened.Get(VaultContainerRef.Team("p")));
        }

        [Fact]
        public void LegacyCapacityOverrides_MigrateEmptyPlayersWithAbsoluteSemanticsIdempotently()
        {
            using (var connection = new SqliteConnection("Data Source=" + m_File))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE vault_items (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    steam_id TEXT NOT NULL,
    item_id INTEGER NOT NULL,
    amount INTEGER NOT NULL,
    quality INTEGER NOT NULL,
    state BLOB NOT NULL,
    slot_cost INTEGER NOT NULL,
    max_amount INTEGER NOT NULL
);
CREATE INDEX ix_vault_items_player ON vault_items(steam_id, id);
CREATE INDEX ix_vault_items_player_item ON vault_items(steam_id, item_id);
CREATE TABLE capacity_overrides (
    steam_id TEXT NOT NULL PRIMARY KEY,
    capacity INTEGER NOT NULL CHECK(capacity > 0)
);
INSERT INTO capacity_overrides(steam_id, capacity) VALUES(76561198000000001, 321);";
                command.ExecuteNonQuery();
            }

            var store = NewStore();
            var player = VaultContainerRef.Player("76561198000000001");
            var migrated = store.GetOrCreateContainer(player, "Empty legacy player", 900);
            Assert.Equal(321, migrated.Capacity);
            Assert.Equal(321, migrated.LegacyCapacityOverride);
            Assert.Empty(store.Get(player));

            var purchase = store.CreatePendingTeamPurchase(
                player, "Empty legacy player", 900, player.Key, 20, 0m)
                ?? throw new InvalidOperationException("Expected purchase creation.");
            Assert.True(store.MarkTeamPurchaseDebited(purchase));
            Assert.True(store.MarkTeamPurchaseReady(purchase));
            Assert.Equal(341, store.CompleteTeamPurchase(purchase, 1000));

            var reopened = new SqliteVaultStore(m_File);
            reopened.Initialize();
            Assert.Equal(341, reopened.GetOrCreateContainer(player, "", 700).Capacity);
            using var verify = new SqliteConnection("Data Source=" + m_File);
            verify.Open();
            using var version = verify.CreateCommand();
            version.CommandText = "PRAGMA user_version;";
            Assert.Equal(6L, (long)version.ExecuteScalar()!);
        }

        [Fact]
        public void V6Migration_DoesNotOverwriteCapacityEditedUnderV5()
        {
            var store = NewStore();
            var player = VaultContainerRef.Player("76561198000000002");
            Assert.Equal(450, store.SetContainerCapacity(player, "Edited", 200, 450)!.Capacity);
            using (var connection = new SqliteConnection("Data Source=" + m_File))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE capacity_overrides (steam_id TEXT NOT NULL PRIMARY KEY, capacity INTEGER NOT NULL);
INSERT INTO capacity_overrides(steam_id, capacity) VALUES(76561198000000002, 999);
PRAGMA user_version = 5;";
                command.ExecuteNonQuery();
            }

            var reopened = new SqliteVaultStore(m_File);
            reopened.Initialize();
            var snapshot = reopened.GetOrCreateContainer(player, "Edited", 200);
            Assert.Equal(450, snapshot.Capacity);
            Assert.Null(snapshot.LegacyCapacityOverride);
        }

        [Fact]
        public void PendingPurchaseQuery_EnforcesBatchAndBuyerBoundaries()
        {
            var store = NewStore();
            for (var index = 0; index < 3; index++)
            {
                Assert.NotNull(store.CreatePendingTeamPurchase(
                    VaultContainerRef.Team("unturned:test:batch-" + index),
                    "Batch", 200, index == 2 ? "buyer-b" : "buyer-a", 10, 0m));
            }

            Assert.Single(store.GetPendingTeamPurchases(1));
            Assert.Equal(2, store.GetPendingTeamPurchases(10, "buyer-a").Count);
            Assert.Single(store.GetPendingTeamPurchases(10, "buyer-b"));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.GetPendingTeamPurchases(1001));
        }

        [Fact]
        public async Task AdminAbortResolution_IsCasProtectedAndAuditedOnce()
        {
            var store = NewStore();
            var team = VaultContainerRef.Team("unturned:test:admin-abort");
            var operation = store.CreatePendingTeamPurchase(team, "Admin abort", 200, "7656119", 10, 0m)
                ?? throw new InvalidOperationException("Expected purchase creation.");

            var attempts = Enumerable.Range(0, 16)
                .Select(index => Task.Run(() => store.TryAbortTeamPurchase(
                    operation, "debiting", "admin-" + index, "verified unpaid")))
                .ToArray();
            var results = await Task.WhenAll(attempts);

            Assert.Single(results, result => result);
            Assert.Equal("aborted", store.GetTeamPurchaseState(operation));
            Assert.Empty(store.GetPendingTeamPurchases());
            var audit = Assert.Single(store.GetTeamPurchaseResolutions());
            Assert.Equal(operation, audit.OperationId);
            Assert.Equal("abort_unpaid", audit.Action);
            Assert.Equal("debiting", audit.FromState);
            Assert.Equal("aborted", audit.ToState);
        }

        [Fact]
        public void ManualRefundConfirmation_RequiresRefundPendingAndWritesAudit()
        {
            var store = NewStore();
            var player = VaultContainerRef.Player("76561198000000003");
            var operation = store.CreatePendingTeamPurchase(player, "Refund", 200, player.Key, 10, 100m,
                economyMode: "experience") ?? throw new InvalidOperationException("Expected purchase.");

            Assert.False(store.TryConfirmTeamPurchaseRefunded(operation, "web-admin", "too early"));
            Assert.True(store.MarkTeamPurchaseRefundPending(operation));
            Assert.True(store.TryConfirmTeamPurchaseRefunded(operation, "web-admin", "manual XP refund receipt 42"));
            Assert.Equal("refunded_manual", store.GetTeamPurchaseState(operation));
            var audit = Assert.Single(store.GetTeamPurchaseResolutions());
            Assert.Equal("confirm_refunded", audit.Action);
            Assert.Equal("manual XP refund receipt 42", audit.Note);

            var second = store.CreatePendingTeamPurchase(player, "Refund 2", 200, player.Key, 10, 100m,
                economyMode: "experience") ?? throw new InvalidOperationException("Expected purchase.");
            Assert.True(store.TryConfirmTeamPurchaseRefunded(
                second, "debiting", "web-admin", "manual XP refund receipt 43"));
            Assert.Equal("refunded_manual", store.GetTeamPurchaseState(second));
        }

        [Fact]
        public void CapacityHardLimit_IsValidatedAtRuntime()
        {
            var store = NewStore();
            var player = VaultContainerRef.Player("76561198000000004");
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                store.GetOrCreateContainer(player, "Too large", VaultCapacityLimits.Maximum + 1));
            Assert.Null(store.SetContainerCapacity(player, "Valid", 200, VaultCapacityLimits.Maximum + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                store.CreatePendingTeamPurchase(player, "Valid", 200, player.Key,
                    VaultCapacityLimits.Maximum + 1, 0m));
        }

        public void Dispose()
        {
            DeleteIfExists(m_File);
            DeleteIfExists(m_File + "-wal");
            DeleteIfExists(m_File + "-shm");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
