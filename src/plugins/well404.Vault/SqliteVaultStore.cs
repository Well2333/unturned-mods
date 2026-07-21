using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace well404.Vault
{
    public sealed class VaultOwner
    {
        public VaultOwner(string steamId, string gameName, string steamName, int rows, int usedSlots)
        {
            SteamId = steamId;
            GameName = gameName;
            SteamName = steamName;
            Rows = rows;
            UsedSlots = usedSlots;
        }

        public string SteamId { get; }
        public string GameName { get; }
        public string SteamName { get; }
        public int Rows { get; }
        public int UsedSlots { get; }
    }

    /// <summary>
    /// Transactional SQLite persistence for personal/team containers, items, capacity adjustments, and recovery records.
    /// Every operation opens a short-lived connection so OpenMod reload never leaves the file locked.
    /// </summary>
    public sealed class SqliteVaultStore
    {
        private readonly object m_Lock = new object();
        private bool m_Initialized;

        public string FilePath { get; }

        public SqliteVaultStore(string filePath)
        {
            SqliteRuntime.Initialize();
            FilePath = filePath;
        }

        public void Initialize()
        {
            lock (m_Lock)
            {
                EnsureInitialized();
            }
        }

        public IReadOnlyList<StoredItem> Get(string steamId)
            => Get(VaultContainerRef.Player(steamId));

        public IReadOnlyList<StoredItem> Get(VaultContainerRef container)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT id, item_id, amount, quality, state, slot_cost, max_amount
FROM vault_items
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey
ORDER BY id;";
                AddOwnerParameters(command, container);
                using var reader = command.ExecuteReader();
                var result = new List<StoredItem>();
                while (reader.Read())
                {
                    result.Add(ReadItem(reader));
                }

                return (IReadOnlyList<StoredItem>)result;
            });

        public int UsedSlots(string steamId)
            => UsedSlots(VaultContainerRef.Player(steamId));

        public int UsedSlots(VaultContainerRef container)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT COALESCE(used_slots, 0)
FROM vault_containers
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey;";
                AddOwnerParameters(command, container);
                return Convert.ToInt32(command.ExecuteScalar());
            });

        public VaultContainerSnapshot GetOrCreateContainer(
            VaultContainerRef container,
            string displayName,
            int baseCapacity)
            => WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var id = EnsureContainer(connection, transaction, container, displayName, baseCapacity);
                var snapshot = ReadContainer(connection, transaction, id);
                transaction.Commit();
                return snapshot;
            });

        public VaultContainerSnapshot? GetContainer(VaultContainerRef container)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT id, owner_kind, owner_key, display_name, base_capacity, purchased_slots,
       admin_adjustment, legacy_capacity_override, used_slots, version, purchase_version,
       (SELECT COUNT(*) FROM team_vault_purchases AS purchase
        WHERE purchase.container_id = vault_containers.id
          AND purchase.state IN ('pending', 'debiting', 'debited', 'ready', 'refund_pending')),
       status
FROM vault_containers
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey;";
                AddOwnerParameters(command, container);
                using var reader = command.ExecuteReader();
                return reader.Read() ? ReadContainer(reader) : null;
            });

        public VaultContainerSnapshot? SetContainerCapacity(
            VaultContainerRef container, string displayName, int baseCapacity, int capacity)
            => WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var id = EnsureContainer(connection, transaction, container, displayName, baseCapacity);
                var before = ReadContainer(connection, transaction, id);
                if (capacity < 1 || capacity > VaultCapacityLimits.Maximum
                    || capacity < before.UsedSlots)
                {
                    transaction.Rollback();
                    return null;
                }
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE vault_containers
SET admin_adjustment = $capacity - base_capacity - purchased_slots,
    legacy_capacity_override = NULL,
    version = version + 1, purchase_version = purchase_version + 1, updated_utc = $updated
WHERE id = $id;";
                command.Parameters.AddWithValue("$capacity", capacity);
                command.Parameters.AddWithValue("$updated", UtcNow());
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
                var after = ReadContainer(connection, transaction, id);
                transaction.Commit();
                return after;
            });

        public IReadOnlyList<VaultContainerSnapshot> GetContainers()
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT container.id, container.owner_kind, container.owner_key, container.display_name,
       container.base_capacity, container.purchased_slots, container.admin_adjustment,
       container.legacy_capacity_override, container.used_slots,
       container.version, container.purchase_version,
       COUNT(purchase.operation_id), container.status
FROM vault_containers AS container
LEFT JOIN team_vault_purchases AS purchase
  ON purchase.container_id = container.id
 AND purchase.state IN ('pending', 'debiting', 'debited', 'ready', 'refund_pending')
GROUP BY container.id
ORDER BY container.owner_kind, container.updated_utc DESC;";
                using var reader = command.ExecuteReader();
                var result = new List<VaultContainerSnapshot>();
                while (reader.Read()) result.Add(ReadContainer(reader));
                return (IReadOnlyList<VaultContainerSnapshot>)result;
            });

        public IReadOnlyList<VaultContainerSnapshot> GetTeamContainers()
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT container.id, container.owner_kind, container.owner_key, container.display_name,
       container.base_capacity, container.purchased_slots, container.admin_adjustment,
       container.legacy_capacity_override, container.used_slots,
       container.version, container.purchase_version,
       COUNT(purchase.operation_id), container.status
FROM vault_containers AS container
LEFT JOIN team_vault_purchases AS purchase
  ON purchase.container_id = container.id
 AND purchase.state IN ('pending', 'debiting', 'debited', 'ready', 'refund_pending')
WHERE container.owner_kind = 'team'
GROUP BY container.id
ORDER BY container.updated_utc DESC;";
                using var reader = command.ExecuteReader();
                var result = new List<VaultContainerSnapshot>();
                while (reader.Read()) result.Add(ReadContainer(reader));
                return (IReadOnlyList<VaultContainerSnapshot>)result;
            });

        public IReadOnlyList<VaultOwner> GetOwners()
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT i.steam_id, COALESCE(o.game_name, ''), COALESCE(o.steam_name, ''),
       COUNT(*), COALESCE(SUM(i.slot_cost), 0)
FROM vault_items AS i
LEFT JOIN vault_owners AS o ON o.steam_id = i.steam_id
WHERE i.owner_kind = 'player'
GROUP BY i.steam_id, o.game_name, o.steam_name
ORDER BY MAX(i.id) DESC;";
                using var reader = command.ExecuteReader();
                var result = new List<VaultOwner>();
                while (reader.Read())
                {
                    result.Add(new VaultOwner(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetInt32(3), reader.GetInt32(4)));
                }

                return (IReadOnlyList<VaultOwner>)result;
            });

        public void TouchOwner(string steamId, string gameName, string steamName)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO vault_owners(steam_id, game_name, steam_name, updated_utc)
VALUES($steamId, $gameName, $steamName, $updated)
ON CONFLICT(steam_id) DO UPDATE SET
    game_name = CASE WHEN excluded.game_name <> '' THEN excluded.game_name ELSE vault_owners.game_name END,
    steam_name = CASE WHEN excluded.steam_name <> '' THEN excluded.steam_name ELSE vault_owners.steam_name END,
    updated_utc = excluded.updated_utc;";
                command.Parameters.AddWithValue("$steamId", steamId);
                command.Parameters.AddWithValue("$gameName", gameName ?? string.Empty);
                command.Parameters.AddWithValue("$steamName", steamName ?? string.Empty);
                command.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
                return 0;
            });

        public void AddItems(string steamId, IReadOnlyList<StoredItem> items)
            => AddItems(VaultContainerRef.Player(steamId), string.Empty, 1, items);

        public void AddItems(
            VaultContainerRef container,
            string displayName,
            int baseCapacity,
            IReadOnlyList<StoredItem> items)
        {
            if (items.Count == 0)
            {
                return;
            }
            VaultCapacityLimits.RequireValid(baseCapacity, nameof(baseCapacity));

            WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var containerId = EnsureContainer(connection, transaction, container, displayName, baseCapacity);
                var cost = SumSlots(items);
                InsertItems(connection, transaction, containerId, container, items);
                IncrementUsedSlots(connection, transaction, containerId, cost);
                transaction.Commit();
                return 0;
            });
        }

        public bool TryAddItems(
            VaultContainerRef container,
            string displayName,
            int baseCapacity,
            int maximumCapacity,
            IReadOnlyList<StoredItem> items,
            string? operationId = null,
            string? actorSteamId = null)
        {
            if (items.Count == 0) return true;
            VaultCapacityLimits.RequireValid(baseCapacity, nameof(baseCapacity));
            VaultCapacityLimits.RequireValid(maximumCapacity, nameof(maximumCapacity));
            return WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var containerId = EnsureContainer(connection, transaction, container, displayName, baseCapacity);
                var cost = SumSlots(items);
                using (var reserve = connection.CreateCommand())
                {
                    reserve.Transaction = transaction;
                    reserve.CommandText = @"
UPDATE vault_containers
SET used_slots = used_slots + $cost,
    version = version + 1,
    updated_utc = $updated
WHERE id = $id
  AND used_slots + $cost <= MIN(
      CASE WHEN legacy_capacity_override IS NULL
           THEN base_capacity + purchased_slots + admin_adjustment
           ELSE legacy_capacity_override + purchased_slots END,
      $maximum)
  AND used_slots + $cost <= 1000000;";
                    reserve.Parameters.AddWithValue("$cost", cost);
                    reserve.Parameters.AddWithValue("$updated", UtcNow());
                    reserve.Parameters.AddWithValue("$id", containerId);
                    reserve.Parameters.AddWithValue("$maximum",
                        VaultCapacityLimits.RequireValid(maximumCapacity, nameof(maximumCapacity)));
                    if (reserve.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }

                InsertItems(connection, transaction, containerId, container, items);
                if (!string.IsNullOrEmpty(operationId))
                {
                    UpsertTransferAudit(
                        connection,
                        transaction,
                        operationId!,
                        container,
                        actorSteamId ?? string.Empty,
                        "store",
                        items.Count,
                        cost,
                        "database_committed",
                        items);
                }
                transaction.Commit();
                return true;
            });
        }

        /// <summary>Updates one exact row owned by <paramref name="steamId"/> without touching its state blob.</summary>
        public bool UpdateItem(
            string steamId,
            long recordId,
            ushort itemId,
            byte amount,
            byte quality,
            int slotCost,
            byte maxAmount,
            string? replacementState = null)
            => UpdateItem(VaultContainerRef.Player(steamId), recordId, itemId, amount, quality, slotCost, maxAmount, replacementState);

        public bool UpdateItem(
            VaultContainerRef container,
            long recordId,
            ushort itemId,
            byte amount,
            byte quality,
            int slotCost,
            byte maxAmount,
            string? replacementState = null)
            => WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var normalizedSlotCost = Math.Max(1, slotCost);
                int oldCost;
                using (var select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandText = @"
SELECT slot_cost FROM vault_items
WHERE id = $id AND owner_kind = $ownerKind AND owner_key = $ownerKey;";
                    select.Parameters.AddWithValue("$id", recordId);
                    AddOwnerParameters(select, container);
                    var value = select.ExecuteScalar();
                    if (value == null || value == DBNull.Value)
                    {
                        transaction.Rollback();
                        return false;
                    }
                    oldCost = Math.Max(1, Convert.ToInt32(value));
                }

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
UPDATE vault_items
SET item_id = $itemId,
    amount = $amount,
    state = COALESCE($state, state),
    quality = $quality,
    slot_cost = $slotCost,
    max_amount = $maxAmount
WHERE id = $id AND owner_kind = $ownerKind AND owner_key = $ownerKey;";
                command.Parameters.AddWithValue("$itemId", itemId);
                command.Parameters.AddWithValue("$amount", amount);
                command.Parameters.AddWithValue("$state", replacementState == null ? (object)DBNull.Value : DecodeState(replacementState));
                command.Parameters.AddWithValue("$quality", quality);
                command.Parameters.AddWithValue("$slotCost", normalizedSlotCost);
                command.Parameters.AddWithValue("$maxAmount", maxAmount);
                command.Parameters.AddWithValue("$id", recordId);
                AddOwnerParameters(command, container);
                if (command.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return false;
                }
                AdjustUsedSlots(connection, transaction, container, normalizedSlotCost - oldCost);
                transaction.Commit();
                return true;
            });

        /// <summary>Deletes one exact row only when it belongs to <paramref name="steamId"/>.</summary>
        public bool DeleteItem(string steamId, long recordId)
            => DeleteItem(VaultContainerRef.Player(steamId), recordId);

        public bool DeleteItem(VaultContainerRef container, long recordId)
            => WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var item = SelectById(connection, transaction, container, recordId);
                if (item == null)
                {
                    transaction.Rollback();
                    return false;
                }
                DeleteById(connection, transaction, recordId);
                AdjustUsedSlots(connection, transaction, container, -item.SlotCost);
                transaction.Commit();
                return true;
            });

        /// <summary>Deletes every row of one item id owned by one player and returns the affected count.</summary>
        public int DeleteItems(string steamId, ushort itemId)
            => DeleteItems(VaultContainerRef.Player(steamId), itemId);

        public int DeleteItems(VaultContainerRef container, ushort itemId)
            => WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                int slots;
                using (var sum = connection.CreateCommand())
                {
                    sum.Transaction = transaction;
                    sum.CommandText = @"
SELECT COALESCE(SUM(slot_cost), 0) FROM vault_items
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey AND item_id = $itemId;";
                    AddOwnerParameters(sum, container);
                    sum.Parameters.AddWithValue("$itemId", itemId);
                    slots = Convert.ToInt32(sum.ExecuteScalar());
                }
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
DELETE FROM vault_items
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey AND item_id = $itemId;";
                AddOwnerParameters(command, container);
                command.Parameters.AddWithValue("$itemId", itemId);
                var count = command.ExecuteNonQuery();
                if (count > 0) AdjustUsedSlots(connection, transaction, container, -slots);
                transaction.Commit();
                return count;
            });

        /// <summary>
        /// Atomically selects and deletes the first matching item. The returned item is no longer
        /// present in SQLite when this method returns.
        /// </summary>
        public StoredItem? TakeFirst(string steamId, Func<StoredItem, bool> matches)
            => TakeFirst(VaultContainerRef.Player(steamId), matches);

        public StoredItem? TakeFirst(VaultContainerRef container, Func<StoredItem, bool> matches)
        {
            var items = TakeMany(container, matches, 1);
            return items.Count == 0 ? null : items[0];
        }

        public IReadOnlyList<StoredItem> TakeMany(
            VaultContainerRef container,
            Func<StoredItem, bool> matches,
            int maximum,
            string? operationId = null,
            string? actorSteamId = null)
            => WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var selected = new List<StoredItem>();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
SELECT id, item_id, amount, quality, state, slot_cost, max_amount
FROM vault_items
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey
ORDER BY id;";
                    AddOwnerParameters(command, container);
                    using var reader = command.ExecuteReader();
                    while (reader.Read() && selected.Count < Math.Max(0, maximum))
                    {
                        var candidate = ReadItem(reader);
                        if (matches(candidate))
                        {
                            selected.Add(candidate);
                        }
                    }
                }

                if (selected.Count == 0)
                {
                    transaction.Commit();
                    return (IReadOnlyList<StoredItem>)selected;
                }

                foreach (var item in selected)
                {
                    DeleteById(connection, transaction, item.RecordId);
                }
                var selectedSlots = SumSlots(selected);
                AdjustUsedSlots(connection, transaction, container, -selectedSlots);
                if (!string.IsNullOrEmpty(operationId))
                {
                    UpsertTransferAudit(
                        connection,
                        transaction,
                        operationId!,
                        container,
                        actorSteamId ?? string.Empty,
                        "take",
                        selected.Count,
                        selectedSlots,
                        "database_committed",
                        selected);
                }

                transaction.Commit();
                return (IReadOnlyList<StoredItem>)selected;
            });

        public VaultMoveResult MoveMany(
            VaultContainerRef source,
            string sourceDisplayName,
            int sourceBaseCapacity,
            VaultContainerRef destination,
            string destinationDisplayName,
            int destinationBaseCapacity,
            int destinationMaximumCapacity,
            Func<StoredItem, bool> matches,
            int maximum)
            => WithDatabase(connection =>
            {
                if (source.Equals(destination) || maximum <= 0) return new VaultMoveResult(0, false);
                using var transaction = connection.BeginTransaction();
                EnsureContainer(connection, transaction, source, sourceDisplayName, sourceBaseCapacity);
                var destinationId = EnsureContainer(
                    connection, transaction, destination, destinationDisplayName, destinationBaseCapacity);
                var destinationSnapshot = ReadContainer(connection, transaction, destinationId);
                var destinationLimit = Math.Min(
                    destinationSnapshot.Capacity, Math.Max(1, destinationMaximumCapacity));
                var available = Math.Max(0, destinationLimit - destinationSnapshot.UsedSlots);
                var selected = new List<StoredItem>();
                var selectedSlots = 0;
                var hadMatch = false;
                var blockedByCapacity = false;
                using (var select = connection.CreateCommand())
                {
                    select.Transaction = transaction;
                    select.CommandText = @"
SELECT id, item_id, amount, quality, state, slot_cost, max_amount
FROM vault_items
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey
ORDER BY id;";
                    AddOwnerParameters(select, source);
                    using var reader = select.ExecuteReader();
                    while (reader.Read() && selected.Count < maximum)
                    {
                        var item = ReadItem(reader);
                        if (!matches(item)) continue;
                        hadMatch = true;
                        if (selectedSlots + item.SlotCost > available)
                        {
                            blockedByCapacity = true;
                            break;
                        }
                        selected.Add(item);
                        selectedSlots += item.SlotCost;
                    }
                }

                if (selected.Count == 0)
                {
                    transaction.Commit();
                    return new VaultMoveResult(0, hadMatch && (blockedByCapacity || available <= 0));
                }

                foreach (var item in selected)
                {
                    using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = @"
UPDATE vault_items
SET steam_id = $steamId, owner_kind = $destinationKind, owner_key = $destinationKey
WHERE id = $id AND owner_kind = $sourceKind AND owner_key = $sourceKey;";
                    update.Parameters.AddWithValue("$steamId",
                        destination.Kind == VaultOwnerKind.Player ? destination.Key : string.Empty);
                    update.Parameters.AddWithValue("$destinationKind", destination.KindKey);
                    update.Parameters.AddWithValue("$destinationKey", destination.Key);
                    update.Parameters.AddWithValue("$id", item.RecordId);
                    update.Parameters.AddWithValue("$sourceKind", source.KindKey);
                    update.Parameters.AddWithValue("$sourceKey", source.Key);
                    if (update.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Vault row changed concurrently during transfer.");
                }

                AdjustUsedSlots(connection, transaction, source, -selectedSlots);
                IncrementUsedSlots(connection, transaction, destinationId, selectedSlots);
                transaction.Commit();
                return new VaultMoveResult(selected.Count, blockedByCapacity);
            });

        public void BeginTransferAudit(
            string operationId,
            VaultContainerRef container,
            string actorSteamId,
            string direction,
            IReadOnlyList<StoredItem> items)
            => WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                UpsertTransferAudit(
                    connection,
                    transaction,
                    operationId,
                    container,
                    actorSteamId,
                    direction,
                    items.Count,
                    SumSlots(items),
                    "prepared",
                    items);
                transaction.Commit();
                return 0;
            });

        public void SetTransferAuditState(string operationId, string state)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE vault_transfer_audit
SET state = $state, updated_utc = $updated
WHERE operation_id = $operation;";
                command.Parameters.AddWithValue("$state", state);
                command.Parameters.AddWithValue("$updated", UtcNow());
                command.Parameters.AddWithValue("$operation", operationId);
                command.ExecuteNonQuery();
                return 0;
            });

        public string? GetTransferAuditState(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT state FROM vault_transfer_audit
WHERE operation_id = $operation;";
                command.Parameters.AddWithValue("$operation", operationId);
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : Convert.ToString(value);
            });

        public IReadOnlyList<StoredItem> GetTransferAuditItems(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT 0, item_id, amount, quality, state, slot_cost, max_amount
FROM vault_transfer_items
WHERE operation_id = $operation
ORDER BY sequence;";
                command.Parameters.AddWithValue("$operation", operationId);
                using var reader = command.ExecuteReader();
                var result = new List<StoredItem>();
                while (reader.Read()) result.Add(ReadItem(reader));
                return (IReadOnlyList<StoredItem>)result;
            });

        public void SetTransferAuditItemStage(string operationId, int sequence, string stage)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE vault_transfer_items SET item_stage = $stage
WHERE operation_id = $operation AND sequence = $sequence;";
                command.Parameters.AddWithValue("$stage", stage);
                command.Parameters.AddWithValue("$operation", operationId);
                command.Parameters.AddWithValue("$sequence", sequence);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Transfer audit item not found.");
                return 0;
            });

        public int InterruptedTransferCount()
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT COUNT(*) FROM vault_transfer_audit
WHERE state IN ('prepared', 'database_committed');";
                return Convert.ToInt32(command.ExecuteScalar());
            });

        public IReadOnlyList<InterruptedVaultTransfer> GetInterruptedTransfers()
            => WithDatabase(connection =>
            {
                var headers = new List<(string operationId, VaultContainerRef container, string actor,
                    string direction, string state, string created, string updated)>();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
SELECT operation_id, owner_kind, owner_key, actor_steam_id, direction,
       state, created_utc, updated_utc
FROM vault_transfer_audit
WHERE state IN ('prepared', 'database_committed')
ORDER BY updated_utc;";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var kind = string.Equals(reader.GetString(1), "team", StringComparison.OrdinalIgnoreCase)
                            ? VaultOwnerKind.Team
                            : VaultOwnerKind.Player;
                        headers.Add((
                            reader.GetString(0),
                            new VaultContainerRef(kind, reader.GetString(2)),
                            reader.GetString(3),
                            reader.GetString(4),
                            reader.GetString(5),
                            reader.GetString(6),
                            reader.GetString(7)));
                    }
                }

                var result = new List<InterruptedVaultTransfer>();
                foreach (var header in headers)
                {
                    result.Add(new InterruptedVaultTransfer(
                        header.operationId,
                        header.container,
                        header.actor,
                        header.direction,
                        header.state,
                        header.created,
                        header.updated,
                        ReadTransferAuditEntries(connection, header.operationId)));
                }
                return (IReadOnlyList<InterruptedVaultTransfer>)result;
            });

        public string? CreatePendingTeamPurchase(
            VaultContainerRef container,
            string displayName,
            int baseCapacity,
            string buyerSteamId,
            int slots,
            decimal price,
            long? expectedPurchaseVersion = null,
            string economyMode = "durable")
            => WithDatabase(connection =>
            {
                VaultCapacityLimits.RequireValid(baseCapacity, nameof(baseCapacity));
                if (slots < 1 || slots > VaultCapacityLimits.Maximum)
                    throw new ArgumentOutOfRangeException(nameof(slots));
                using var transaction = connection.BeginTransaction();
                var containerId = EnsureContainer(connection, transaction, container, displayName, baseCapacity);
                using (var reserve = connection.CreateCommand())
                {
                    reserve.Transaction = transaction;
                    reserve.CommandText = expectedPurchaseVersion.HasValue ? @"
UPDATE vault_containers
SET purchase_version = purchase_version + 1, updated_utc = $updated
WHERE id = $id AND purchase_version = $version
  AND NOT EXISTS (
      SELECT 1 FROM team_vault_purchases
      WHERE container_id = $id
        AND state IN ('pending', 'debiting', 'debited', 'ready', 'refund_pending')
  );" : @"
UPDATE vault_containers
SET purchase_version = purchase_version + 1, updated_utc = $updated
WHERE id = $id
  AND NOT EXISTS (
      SELECT 1 FROM team_vault_purchases
      WHERE container_id = $id
        AND state IN ('pending', 'debiting', 'debited', 'ready', 'refund_pending')
  );";
                    reserve.Parameters.AddWithValue("$updated", UtcNow());
                    reserve.Parameters.AddWithValue("$id", containerId);
                    if (expectedPurchaseVersion.HasValue)
                        reserve.Parameters.AddWithValue("$version", expectedPurchaseVersion.Value);
                    if (reserve.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return null;
                    }
                }
                var operationId = "vault_capacity:" + Guid.NewGuid().ToString("N");
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO team_vault_purchases(
    operation_id, container_id, buyer_steam_id, slots_added, price, economy_mode, state, created_utc, updated_utc)
VALUES($operation, $container, $buyer, $slots, $price, $economyMode, 'debiting', $created, $updated);";
                command.Parameters.AddWithValue("$operation", operationId);
                command.Parameters.AddWithValue("$container", containerId);
                command.Parameters.AddWithValue("$buyer", buyerSteamId);
                command.Parameters.AddWithValue("$slots", slots);
                command.Parameters.AddWithValue("$price", price.ToString(System.Globalization.CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$economyMode", economyMode);
                command.Parameters.AddWithValue("$created", UtcNow());
                command.Parameters.AddWithValue("$updated", UtcNow());
                command.ExecuteNonQuery();
                transaction.Commit();
                return operationId;
            });

        public bool MarkTeamPurchaseDebiting(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE team_vault_purchases SET state = 'debiting', updated_utc = $updated
WHERE operation_id = $operation AND state = 'pending';";
                command.Parameters.AddWithValue("$updated", UtcNow());
                command.Parameters.AddWithValue("$operation", operationId);
                return command.ExecuteNonQuery() == 1;
            });

        public int CompleteTeamPurchase(string operationId, int maximumCapacity)
            => WithDatabase(connection =>
            {
                VaultCapacityLimits.RequireValid(maximumCapacity, nameof(maximumCapacity));
                using var transaction = connection.BeginTransaction();
                long containerId;
                int slots;
                string state;
                using (var read = connection.CreateCommand())
                {
                    read.Transaction = transaction;
                    read.CommandText = @"
SELECT container_id, slots_added, state
FROM team_vault_purchases WHERE operation_id = $operation;";
                    read.Parameters.AddWithValue("$operation", operationId);
                    using var reader = read.ExecuteReader();
                    if (!reader.Read()) throw new InvalidOperationException("Team vault purchase not found.");
                    containerId = reader.GetInt64(0);
                    slots = reader.GetInt32(1);
                    state = reader.GetString(2);
                }

                if (string.Equals(state, "completed", StringComparison.Ordinal))
                {
                    var current = ReadContainer(connection, transaction, containerId).Capacity;
                    transaction.Commit();
                    return current;
                }

                if (!string.Equals(state, "ready", StringComparison.Ordinal))
                {
                    transaction.Commit();
                    return 0;
                }

                using (var update = connection.CreateCommand())
                {
                    update.Transaction = transaction;
                    update.CommandText = @"
UPDATE vault_containers
SET purchased_slots = purchased_slots + $slots,
    version = version + 1,
    updated_utc = $updated
WHERE id = $id
  AND (CASE WHEN legacy_capacity_override IS NULL
            THEN base_capacity + purchased_slots + admin_adjustment
            ELSE legacy_capacity_override + purchased_slots END) + $slots <= $maximum
  AND purchased_slots + $slots <= 1000000;";
                    update.Parameters.AddWithValue("$slots", slots);
                    update.Parameters.AddWithValue("$updated", UtcNow());
                    update.Parameters.AddWithValue("$id", containerId);
                    update.Parameters.AddWithValue("$maximum", maximumCapacity);
                    if (update.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return 0;
                    }
                }

                using (var finish = connection.CreateCommand())
                {
                    finish.Transaction = transaction;
                    finish.CommandText = @"
UPDATE team_vault_purchases SET state = 'completed', updated_utc = $updated
WHERE operation_id = $operation AND state = 'ready';";
                    finish.Parameters.AddWithValue("$updated", UtcNow());
                    finish.Parameters.AddWithValue("$operation", operationId);
                    finish.ExecuteNonQuery();
                }

                var capacity = ReadContainer(connection, transaction, containerId).Capacity;
                transaction.Commit();
                return capacity;
            });

        public bool MarkTeamPurchaseDebited(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE team_vault_purchases SET state = 'debited', updated_utc = $updated
WHERE operation_id = $operation AND state = 'debiting';";
                command.Parameters.AddWithValue("$updated", UtcNow());
                command.Parameters.AddWithValue("$operation", operationId);
                return command.ExecuteNonQuery() == 1;
            });

        public bool MarkTeamPurchaseReady(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE team_vault_purchases SET state = 'ready', updated_utc = $updated
WHERE operation_id = $operation AND state = 'debited';";
                command.Parameters.AddWithValue("$updated", UtcNow());
                command.Parameters.AddWithValue("$operation", operationId);
                return command.ExecuteNonQuery() == 1;
            });

        public bool MarkTeamPurchaseRefundPending(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE team_vault_purchases SET state = 'refund_pending', updated_utc = $updated
WHERE operation_id = $operation AND state IN ('pending', 'debiting', 'debited', 'ready');";
                command.Parameters.AddWithValue("$updated", UtcNow());
                command.Parameters.AddWithValue("$operation", operationId);
                return command.ExecuteNonQuery() == 1;
            });

        public bool MarkTeamPurchaseRefunded(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE team_vault_purchases SET state = 'refunded', updated_utc = $updated
WHERE operation_id = $operation AND state = 'refund_pending';";
                command.Parameters.AddWithValue("$updated", UtcNow());
                command.Parameters.AddWithValue("$operation", operationId);
                return command.ExecuteNonQuery() == 1;
            });

        public string? GetTeamPurchaseState(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT state FROM team_vault_purchases
WHERE operation_id = $operation;";
                command.Parameters.AddWithValue("$operation", operationId);
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? null : Convert.ToString(value);
            });

        public PendingTeamVaultPurchase? GetPendingTeamPurchase(string operationId)
            => GetPendingTeamPurchases(1, null, operationId).FirstOrDefault();

        public IReadOnlyList<PendingTeamVaultPurchase> GetPendingTeamPurchases(
            int maximum = 100,
            string? buyerSteamId = null,
            string? operationId = null)
            => WithDatabase(connection =>
            {
                if (maximum < 1 || maximum > 1000)
                    throw new ArgumentOutOfRangeException(nameof(maximum));
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT purchase.operation_id, purchase.buyer_steam_id, container.owner_kind, container.owner_key,
       purchase.slots_added, purchase.price, purchase.economy_mode, purchase.state,
       purchase.created_utc, purchase.updated_utc
FROM team_vault_purchases AS purchase
JOIN vault_containers AS container ON container.id = purchase.container_id
WHERE purchase.state IN ('pending', 'debiting', 'debited', 'ready', 'refund_pending')
  AND ($buyer = '' OR purchase.buyer_steam_id = $buyer)
  AND ($operation = '' OR purchase.operation_id = $operation)
ORDER BY purchase.created_utc
LIMIT $maximum;";
                command.Parameters.AddWithValue("$buyer", buyerSteamId ?? string.Empty);
                command.Parameters.AddWithValue("$operation", operationId ?? string.Empty);
                command.Parameters.AddWithValue("$maximum", maximum);
                using var reader = command.ExecuteReader();
                var result = new List<PendingTeamVaultPurchase>();
                while (reader.Read()) result.Add(ReadPendingPurchase(reader));
                return (IReadOnlyList<PendingTeamVaultPurchase>)result;
            });

        public bool TryAbortTeamPurchase(
            string operationId, string expectedState, string actor, string note)
        {
            if (!string.Equals(expectedState, "pending", StringComparison.Ordinal)
                && !string.Equals(expectedState, "debiting", StringComparison.Ordinal))
                throw new ArgumentException("Only an unpaid pending/debiting purchase may be aborted.", nameof(expectedState));
            return TryResolveTeamPurchase(
                operationId, expectedState, "aborted", "abort_unpaid", actor, note);
        }

        public bool TryConfirmTeamPurchaseRefunded(string operationId, string actor, string note)
            => TryConfirmTeamPurchaseRefunded(operationId, "refund_pending", actor, note);

        public bool TryConfirmTeamPurchaseRefunded(
            string operationId, string expectedState, string actor, string note)
        {
            if (!string.Equals(expectedState, "refund_pending", StringComparison.Ordinal)
                && !string.Equals(expectedState, "debiting", StringComparison.Ordinal))
                throw new ArgumentException(
                    "Only refund_pending or an experience debiting purchase may be manually confirmed.",
                    nameof(expectedState));
            return TryResolveTeamPurchase(
                operationId, expectedState, "refunded_manual", "confirm_refunded", actor, note);
        }

        public bool TryResolveRetriedTeamPurchaseRefund(string operationId, string actor, string note)
            => TryResolveTeamPurchase(
                operationId, "refund_pending", "refunded", "retry_refund", actor, note);

        public bool TryResolveCancelledTeamPurchaseRefund(string operationId, string actor, string note)
            => TryResolveTeamPurchase(
                operationId, "refund_pending", "refunded", "abort_refunded", actor, note);

        public IReadOnlyList<TeamVaultPurchaseResolution> GetTeamPurchaseResolutions(int maximum = 200)
            => WithDatabase(connection =>
            {
                if (maximum < 1 || maximum > 1000)
                    throw new ArgumentOutOfRangeException(nameof(maximum));
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT id, operation_id, action, actor, note, from_state, to_state, created_utc
FROM team_vault_purchase_resolutions
ORDER BY id DESC
LIMIT $maximum;";
                command.Parameters.AddWithValue("$maximum", maximum);
                using var reader = command.ExecuteReader();
                var result = new List<TeamVaultPurchaseResolution>();
                while (reader.Read())
                {
                    result.Add(new TeamVaultPurchaseResolution(
                        reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                        reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
                }
                return (IReadOnlyList<TeamVaultPurchaseResolution>)result;
            });

        private bool TryResolveTeamPurchase(
            string operationId, string expectedState, string targetState,
            string action, string actor, string note)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                throw new ArgumentException("Operation ID is required.", nameof(operationId));
            if (string.IsNullOrWhiteSpace(actor))
                throw new ArgumentException("Resolution actor is required.", nameof(actor));
            if (string.IsNullOrWhiteSpace(note))
                throw new ArgumentException("Resolution note is required.", nameof(note));
            return WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                using (var update = connection.CreateCommand())
                {
                    update.Transaction = transaction;
                    update.CommandText = @"
UPDATE team_vault_purchases
SET state = $target, updated_utc = $updated
WHERE operation_id = $operation AND state = $expected;";
                    update.Parameters.AddWithValue("$target", targetState);
                    update.Parameters.AddWithValue("$updated", UtcNow());
                    update.Parameters.AddWithValue("$operation", operationId);
                    update.Parameters.AddWithValue("$expected", expectedState);
                    if (update.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return false;
                    }
                }
                using (var audit = connection.CreateCommand())
                {
                    audit.Transaction = transaction;
                    audit.CommandText = @"
INSERT INTO team_vault_purchase_resolutions(
    operation_id, action, actor, note, from_state, to_state, created_utc)
VALUES($operation, $action, $actor, $note, $from, $to, $created);";
                    audit.Parameters.AddWithValue("$operation", operationId);
                    audit.Parameters.AddWithValue("$action", action);
                    audit.Parameters.AddWithValue("$actor", actor);
                    audit.Parameters.AddWithValue("$note", note.Trim());
                    audit.Parameters.AddWithValue("$from", expectedState);
                    audit.Parameters.AddWithValue("$to", targetState);
                    audit.Parameters.AddWithValue("$created", UtcNow());
                    audit.ExecuteNonQuery();
                }
                transaction.Commit();
                return true;
            });
        }

        private static PendingTeamVaultPurchase ReadPendingPurchase(SqliteDataReader reader)
            => new PendingTeamVaultPurchase(
                reader.GetString(0), reader.GetString(1),
                new VaultContainerRef(
                    string.Equals(reader.GetString(2), "team", StringComparison.OrdinalIgnoreCase)
                        ? VaultOwnerKind.Team : VaultOwnerKind.Player,
                    reader.GetString(3)),
                ReadCapacityValue(reader, 4, "slots_added", minimum: 1),
                decimal.Parse(reader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
                reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9));

        public int PendingTeamPurchaseCount(VaultContainerRef container)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT COUNT(*)
FROM team_vault_purchases AS purchase
JOIN vault_containers AS container ON container.id = purchase.container_id
WHERE container.owner_kind = $ownerKind
  AND container.owner_key = $ownerKey
  AND purchase.state IN ('pending', 'debiting', 'debited', 'ready', 'refund_pending');";
                AddOwnerParameters(command, container);
                return Convert.ToInt32(command.ExecuteScalar());
            });

        private T WithDatabase<T>(Func<SqliteConnection, T> action)
        {
            lock (m_Lock)
            {
                EnsureInitialized();
                using var connection = OpenConnection();
                return action(connection);
            }
        }

        private void EnsureInitialized()
        {
            if (m_Initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
CREATE TABLE IF NOT EXISTS vault_items (
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
CREATE INDEX IF NOT EXISTS ix_vault_items_player
    ON vault_items(steam_id, id);
CREATE INDEX IF NOT EXISTS ix_vault_items_player_item
    ON vault_items(steam_id, item_id);
CREATE TABLE IF NOT EXISTS vault_owners (
    steam_id TEXT NOT NULL PRIMARY KEY,
    game_name TEXT NOT NULL DEFAULT '',
    steam_name TEXT NOT NULL DEFAULT '',
    updated_utc TEXT NOT NULL
);";
            command.ExecuteNonQuery();

            EnsureColumn(connection, "vault_items", "owner_kind", "TEXT NOT NULL DEFAULT 'player'");
            EnsureColumn(connection, "vault_items", "owner_key", "TEXT NOT NULL DEFAULT ''");
            var schemaVersion = ReadUserVersion(connection);
            using (var migrate = connection.CreateCommand())
            {
                using var transaction = connection.BeginTransaction();
                migrate.Transaction = transaction;
                migrate.CommandText = @"
CREATE INDEX IF NOT EXISTS ix_vault_items_owner
    ON vault_items(owner_kind, owner_key, id);
CREATE INDEX IF NOT EXISTS ix_vault_items_owner_item
    ON vault_items(owner_kind, owner_key, item_id);
CREATE TABLE IF NOT EXISTS vault_containers (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    owner_kind TEXT NOT NULL,
    owner_key TEXT NOT NULL,
    display_name TEXT NOT NULL DEFAULT '',
    base_capacity INTEGER NOT NULL CHECK(base_capacity BETWEEN 1 AND 1000000),
    purchased_slots INTEGER NOT NULL DEFAULT 0 CHECK(purchased_slots BETWEEN 0 AND 1000000),
    admin_adjustment INTEGER NOT NULL DEFAULT 0,
    legacy_capacity_override INTEGER NULL,
    used_slots INTEGER NOT NULL DEFAULT 0 CHECK(used_slots BETWEEN 0 AND 1000000),
    version INTEGER NOT NULL DEFAULT 0,
    purchase_version INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'active',
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    UNIQUE(owner_kind, owner_key)
);
CREATE INDEX IF NOT EXISTS ix_vault_containers_kind_updated
    ON vault_containers(owner_kind, updated_utc DESC);
CREATE TABLE IF NOT EXISTS team_vault_purchases (
    operation_id TEXT NOT NULL PRIMARY KEY,
    container_id INTEGER NOT NULL,
    buyer_steam_id TEXT NOT NULL,
    slots_added INTEGER NOT NULL CHECK(slots_added > 0),
    price TEXT NOT NULL,
    economy_mode TEXT NOT NULL DEFAULT 'durable',
    state TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(container_id) REFERENCES vault_containers(id)
);
CREATE INDEX IF NOT EXISTS ix_team_vault_purchases_container_time
    ON team_vault_purchases(container_id, created_utc DESC);
CREATE INDEX IF NOT EXISTS ix_team_vault_purchases_state_time
    ON team_vault_purchases(state, created_utc);
CREATE TABLE IF NOT EXISTS team_vault_purchase_resolutions (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    operation_id TEXT NOT NULL,
    action TEXT NOT NULL,
    actor TEXT NOT NULL,
    note TEXT NOT NULL,
    from_state TEXT NOT NULL,
    to_state TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY(operation_id) REFERENCES team_vault_purchases(operation_id)
);
CREATE INDEX IF NOT EXISTS ix_team_vault_purchase_resolutions_operation
    ON team_vault_purchase_resolutions(operation_id, id DESC);
CREATE TABLE IF NOT EXISTS vault_transfer_audit (
    operation_id TEXT NOT NULL PRIMARY KEY,
    owner_kind TEXT NOT NULL,
    owner_key TEXT NOT NULL,
    actor_steam_id TEXT NOT NULL,
    direction TEXT NOT NULL,
    item_count INTEGER NOT NULL,
    slot_count INTEGER NOT NULL,
    state TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_vault_transfer_audit_state_time
    ON vault_transfer_audit(state, updated_utc);
CREATE TABLE IF NOT EXISTS vault_transfer_items (
    operation_id TEXT NOT NULL,
    sequence INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    amount INTEGER NOT NULL,
    quality INTEGER NOT NULL,
    state BLOB NOT NULL,
    slot_cost INTEGER NOT NULL,
    max_amount INTEGER NOT NULL,
    item_stage TEXT NOT NULL DEFAULT 'candidate',
    PRIMARY KEY(operation_id, sequence),
    FOREIGN KEY(operation_id) REFERENCES vault_transfer_audit(operation_id) ON DELETE CASCADE
);";
                if (schemaVersion < 3)
                {
                    migrate.CommandText += @"
UPDATE vault_items SET owner_kind = 'player' WHERE owner_kind = '' OR owner_kind IS NULL;
UPDATE vault_items SET owner_key = steam_id WHERE owner_key = '' OR owner_key IS NULL;
UPDATE vault_items SET slot_cost = 1 WHERE slot_cost < 1;
INSERT OR IGNORE INTO vault_containers(
    owner_kind, owner_key, display_name, base_capacity, purchased_slots,
    used_slots, version, status, created_utc, updated_utc)
SELECT 'player', steam_id, '', 1, 0, COALESCE(SUM(slot_cost), 0), 0, 'active',
       strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')
FROM vault_items
WHERE owner_kind = 'player'
GROUP BY steam_id;
UPDATE vault_containers
SET used_slots = COALESCE((
        SELECT SUM(i.slot_cost) FROM vault_items AS i
        WHERE i.owner_kind = vault_containers.owner_kind
          AND i.owner_key = vault_containers.owner_key
    ), 0);";
                }
                migrate.ExecuteNonQuery();
                transaction.Commit();
            }
            EnsureColumn(connection, "vault_containers", "purchase_version", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "vault_containers", "admin_adjustment", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "vault_containers", "legacy_capacity_override", "INTEGER NULL");
            EnsureColumn(connection, "team_vault_purchases", "economy_mode", "TEXT NOT NULL DEFAULT 'durable'");
            EnsureColumn(connection, "vault_transfer_items", "item_stage", "TEXT NOT NULL DEFAULT 'candidate'");
            if (schemaVersion < 6)
            {
                using var transaction = connection.BeginTransaction();
                if (TableExists(connection, transaction, "capacity_overrides"))
                {
                    using var legacy = connection.CreateCommand();
                    legacy.Transaction = transaction;
                    legacy.CommandText = @"
INSERT INTO vault_containers(
    owner_kind, owner_key, display_name, base_capacity, purchased_slots, admin_adjustment,
    legacy_capacity_override, used_slots, version, purchase_version, status, created_utc, updated_utc)
SELECT 'player', steam_id, '', 1, 0, 0, capacity, 0, 0, 0, 'active',
       strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')
FROM capacity_overrides
WHERE capacity BETWEEN 1 AND 1000000
ON CONFLICT(owner_kind, owner_key) DO UPDATE SET
    legacy_capacity_override = excluded.legacy_capacity_override,
    updated_utc = excluded.updated_utc
WHERE vault_containers.purchase_version = 0
  AND vault_containers.purchased_slots = 0
  AND vault_containers.admin_adjustment = 0;";
                    legacy.ExecuteNonQuery();
                }
                using var version = connection.CreateCommand();
                version.Transaction = transaction;
                version.CommandText = "PRAGMA user_version = 6;";
                version.ExecuteNonQuery();
                transaction.Commit();
            }
            using (var prune = connection.CreateCommand())
            {
                prune.CommandText = @"
DELETE FROM vault_transfer_audit
WHERE state NOT IN ('prepared', 'database_committed')
  AND updated_utc < strftime('%Y-%m-%dT%H:%M:%fZ', 'now', '-30 days');";
                prune.ExecuteNonQuery();
            }
            m_Initialized = true;
        }

        private SqliteConnection OpenConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };
            var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
            return connection;
        }

        private static StoredItem ReadItem(SqliteDataReader reader)
        {
            var state = (byte[])reader.GetValue(4);
            return new StoredItem
            {
                RecordId = reader.GetInt64(0),
                ItemId = checked((ushort)reader.GetInt64(1)),
                Amount = checked((byte)reader.GetInt64(2)),
                Quality = checked((byte)reader.GetInt64(3)),
                State = state.Length == 0 ? string.Empty : Convert.ToBase64String(state),
                SlotCost = reader.GetInt32(5),
                MaxAmount = checked((byte)reader.GetInt64(6))
            };
        }

        private static byte[] DecodeState(string state)
            => string.IsNullOrEmpty(state) ? Array.Empty<byte>() : Convert.FromBase64String(state);

        private static IReadOnlyList<StoredItem> ReadTransferAuditItems(
            SqliteConnection connection,
            string operationId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT 0, item_id, amount, quality, state, slot_cost, max_amount
FROM vault_transfer_items
WHERE operation_id = $operation
ORDER BY sequence;";
            command.Parameters.AddWithValue("$operation", operationId);
            using var reader = command.ExecuteReader();
            var result = new List<StoredItem>();
            while (reader.Read()) result.Add(ReadItem(reader));
            return result;
        }

        private static IReadOnlyList<VaultTransferAuditItem> ReadTransferAuditEntries(
            SqliteConnection connection,
            string operationId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT 0, item_id, amount, quality, state, slot_cost, max_amount, item_stage
FROM vault_transfer_items
WHERE operation_id = $operation
ORDER BY sequence;";
            command.Parameters.AddWithValue("$operation", operationId);
            using var reader = command.ExecuteReader();
            var result = new List<VaultTransferAuditItem>();
            while (reader.Read()) result.Add(new VaultTransferAuditItem(ReadItem(reader), reader.GetString(7)));
            return result;
        }

        private static void EnsureColumn(
            SqliteConnection connection,
            string table,
            string column,
            string definition)
        {
            using var inspect = connection.CreateCommand();
            inspect.CommandText = "PRAGMA table_info(" + table + ");";
            using var reader = inspect.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
            }

            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE " + table + " ADD COLUMN " + column + " " + definition + ";";
            alter.ExecuteNonQuery();
        }

        private static int ReadUserVersion(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static bool TableExists(SqliteConnection connection, SqliteTransaction transaction, string table)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
            command.Parameters.AddWithValue("$name", table);
            return command.ExecuteScalar() != null;
        }

        private static long EnsureContainer(
            SqliteConnection connection,
            SqliteTransaction transaction,
            VaultContainerRef container,
            string displayName,
            int baseCapacity)
        {
            using (var upsert = connection.CreateCommand())
            {
                upsert.Transaction = transaction;
                upsert.CommandText = @"
INSERT INTO vault_containers(
    owner_kind, owner_key, display_name, base_capacity, purchased_slots,
    used_slots, version, status, created_utc, updated_utc)
VALUES($ownerKind, $ownerKey, $displayName, $baseCapacity, 0, 0, 0, 'active', $created, $updated)
ON CONFLICT(owner_kind, owner_key) DO UPDATE SET
    display_name = CASE WHEN excluded.display_name <> '' THEN excluded.display_name ELSE vault_containers.display_name END,
    base_capacity = excluded.base_capacity,
    status = 'active',
    updated_utc = excluded.updated_utc;";
                AddOwnerParameters(upsert, container);
                upsert.Parameters.AddWithValue("$displayName", displayName ?? string.Empty);
                upsert.Parameters.AddWithValue("$baseCapacity", VaultCapacityLimits.RequireValid(baseCapacity, nameof(baseCapacity)));
                upsert.Parameters.AddWithValue("$created", UtcNow());
                upsert.Parameters.AddWithValue("$updated", UtcNow());
                upsert.ExecuteNonQuery();
            }

            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = @"
SELECT id FROM vault_containers
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey;";
            AddOwnerParameters(select, container);
            return Convert.ToInt64(select.ExecuteScalar());
        }

        private static VaultContainerSnapshot ReadContainer(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long id)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT id, owner_kind, owner_key, display_name, base_capacity, purchased_slots,
       admin_adjustment, legacy_capacity_override, used_slots, version, purchase_version,
       (SELECT COUNT(*) FROM team_vault_purchases AS purchase
        WHERE purchase.container_id = vault_containers.id
          AND purchase.state IN ('pending', 'debiting', 'debited', 'ready', 'refund_pending')),
       status
FROM vault_containers WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Vault container not found.");
            return ReadContainer(reader);
        }

        private static VaultContainerSnapshot ReadContainer(SqliteDataReader reader)
        {
            var kind = string.Equals(reader.GetString(1), "team", StringComparison.OrdinalIgnoreCase)
                ? VaultOwnerKind.Team
                : VaultOwnerKind.Player;
            return new VaultContainerSnapshot(
                reader.GetInt64(0),
                new VaultContainerRef(kind, reader.GetString(2)),
                reader.GetString(3),
                ReadCapacityValue(reader, 4, "base_capacity", minimum: 1),
                ReadCapacityValue(reader, 5, "purchased_slots", minimum: 0),
                checked((int)reader.GetInt64(6)),
                reader.IsDBNull(7) ? (int?)null : ReadCapacityValue(reader, 7, "legacy_capacity_override", minimum: 1),
                ReadCapacityValue(reader, 8, "used_slots", minimum: 0),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetInt32(11),
                reader.GetString(12));
        }

        private static int ReadCapacityValue(SqliteDataReader reader, int ordinal, string column, int minimum)
        {
            var value = reader.GetInt64(ordinal);
            if (value < minimum || value > VaultCapacityLimits.Maximum)
                throw new InvalidDataException(
                    $"Vault database column {column} contains out-of-range value {value}.");
            return (int)value;
        }

        private static void InsertItems(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long containerId,
            VaultContainerRef container,
            IReadOnlyList<StoredItem> items)
        {
            foreach (var item in items)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO vault_items(
    steam_id, owner_kind, owner_key, item_id, amount, quality, state, slot_cost, max_amount)
VALUES($steamId, $ownerKind, $ownerKey, $itemId, $amount, $quality, $state, $slotCost, $maxAmount);";
                command.Parameters.AddWithValue("$steamId", container.Kind == VaultOwnerKind.Player ? container.Key : string.Empty);
                AddOwnerParameters(command, container);
                command.Parameters.AddWithValue("$itemId", item.ItemId);
                command.Parameters.AddWithValue("$amount", item.Amount);
                command.Parameters.AddWithValue("$quality", item.Quality);
                command.Parameters.AddWithValue("$state", DecodeState(item.State));
                command.Parameters.AddWithValue("$slotCost", Math.Max(1, item.SlotCost));
                command.Parameters.AddWithValue("$maxAmount", item.MaxAmount);
                command.ExecuteNonQuery();
            }
        }

        private static void IncrementUsedSlots(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long containerId,
            int amount)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
UPDATE vault_containers
SET used_slots = used_slots + $amount, version = version + 1, updated_utc = $updated
WHERE id = $id AND used_slots + $amount <= 1000000;";
            command.Parameters.AddWithValue("$amount", amount);
            command.Parameters.AddWithValue("$updated", UtcNow());
            command.Parameters.AddWithValue("$id", containerId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Vault hard capacity limit would be exceeded.");
        }

        private static void UpsertTransferAudit(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string operationId,
            VaultContainerRef container,
            string actorSteamId,
            string direction,
            int itemCount,
            int slotCount,
            string state,
            IReadOnlyList<StoredItem>? items = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO vault_transfer_audit(
    operation_id, owner_kind, owner_key, actor_steam_id, direction,
    item_count, slot_count, state, created_utc, updated_utc)
VALUES($operation, $ownerKind, $ownerKey, $actor, $direction,
       $items, $slots, $state, $created, $updated)
ON CONFLICT(operation_id) DO UPDATE SET
    item_count = excluded.item_count,
    slot_count = excluded.slot_count,
    state = excluded.state,
    updated_utc = excluded.updated_utc;";
            command.Parameters.AddWithValue("$operation", operationId);
            AddOwnerParameters(command, container);
            command.Parameters.AddWithValue("$actor", actorSteamId);
            command.Parameters.AddWithValue("$direction", direction);
            command.Parameters.AddWithValue("$items", Math.Max(0, itemCount));
            command.Parameters.AddWithValue("$slots", Math.Max(0, slotCount));
            command.Parameters.AddWithValue("$state", state);
            command.Parameters.AddWithValue("$created", UtcNow());
            command.Parameters.AddWithValue("$updated", UtcNow());
            command.ExecuteNonQuery();

            if (items == null) return;

            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM vault_transfer_items WHERE operation_id = $operation;";
                delete.Parameters.AddWithValue("$operation", operationId);
                delete.ExecuteNonQuery();
            }

            for (var sequence = 0; sequence < items.Count; sequence++)
            {
                var item = items[sequence];
                using var detail = connection.CreateCommand();
                detail.Transaction = transaction;
                detail.CommandText = @"
INSERT INTO vault_transfer_items(
    operation_id, sequence, item_id, amount, quality, state, slot_cost, max_amount, item_stage)
VALUES($operation, $sequence, $itemId, $amount, $quality, $state, $slotCost, $maxAmount, $itemStage);";
                detail.Parameters.AddWithValue("$operation", operationId);
                detail.Parameters.AddWithValue("$sequence", sequence);
                detail.Parameters.AddWithValue("$itemId", item.ItemId);
                detail.Parameters.AddWithValue("$amount", item.Amount);
                detail.Parameters.AddWithValue("$quality", item.Quality);
                detail.Parameters.AddWithValue("$state", DecodeState(item.State));
                detail.Parameters.AddWithValue("$slotCost", Math.Max(1, item.SlotCost));
                detail.Parameters.AddWithValue("$maxAmount", item.MaxAmount);
                detail.Parameters.AddWithValue("$itemStage",
                    string.Equals(state, "database_committed", StringComparison.Ordinal)
                        ? string.Equals(direction, "take", StringComparison.Ordinal)
                            ? "database_removed"
                            : "database_committed"
                        : "candidate");
                detail.ExecuteNonQuery();
            }
        }

        private static void AdjustUsedSlots(
            SqliteConnection connection,
            SqliteTransaction transaction,
            VaultContainerRef container,
            int delta)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
UPDATE vault_containers
SET used_slots = MAX(0, used_slots + $delta), version = version + 1, updated_utc = $updated
WHERE owner_kind = $ownerKind AND owner_key = $ownerKey;";
            command.Parameters.AddWithValue("$delta", delta);
            command.Parameters.AddWithValue("$updated", UtcNow());
            AddOwnerParameters(command, container);
            command.ExecuteNonQuery();
        }

        private static StoredItem? SelectById(
            SqliteConnection connection,
            SqliteTransaction transaction,
            VaultContainerRef container,
            long recordId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT id, item_id, amount, quality, state, slot_cost, max_amount
FROM vault_items
WHERE id = $id AND owner_kind = $ownerKind AND owner_key = $ownerKey;";
            command.Parameters.AddWithValue("$id", recordId);
            AddOwnerParameters(command, container);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadItem(reader) : null;
        }

        private static void DeleteById(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long recordId)
        {
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM vault_items WHERE id = $id;";
            delete.Parameters.AddWithValue("$id", recordId);
            if (delete.ExecuteNonQuery() != 1) throw new InvalidOperationException("Vault row changed concurrently.");
        }

        private static int SumSlots(IEnumerable<StoredItem> items)
        {
            var total = 0;
            foreach (var item in items) total = checked(total + Math.Max(1, item.SlotCost));
            return total;
        }

        private static void AddOwnerParameters(SqliteCommand command, VaultContainerRef container)
        {
            command.Parameters.AddWithValue("$ownerKind", container.KindKey);
            command.Parameters.AddWithValue("$ownerKey", container.Key);
        }

        private static string UtcNow() => DateTime.UtcNow.ToString("O");
    }
}
