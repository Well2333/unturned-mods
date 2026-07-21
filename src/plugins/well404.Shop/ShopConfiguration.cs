using System;
using System.Collections.Generic;

namespace well404.Shop
{
    public static class ShopConfiguration
    {
        public const string DefaultGroupId = "default";

        public static bool Normalize(ShopSettings settings)
        {
            var changed = false;
            if (settings.Discounts == null) { settings.Discounts = new DiscountSettings(); changed = true; }
            if (settings.Discounts.Tiers == null)
            {
                settings.Discounts.Tiers = new Dictionary<string, decimal>();
                changed = true;
            }
            if (settings.Groups == null) { settings.Groups = new List<ShopGroupConfig>(); changed = true; }
            if (settings.Items == null) { settings.Items = new List<ShopItemConfig>(); changed = true; }

            var normalizedGroups = new List<ShopGroupConfig>();
            var seenGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in settings.Groups)
            {
                var id = string.IsNullOrWhiteSpace(source.Id) ? DefaultGroupId : source.Id.Trim();
                if (string.Equals(id, DefaultGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    id = DefaultGroupId;
                }
                var name = string.IsNullOrWhiteSpace(source.Name) ? id : source.Name.Trim();
                if (!seenGroups.Add(id))
                {
                    changed = true;
                    continue;
                }
                if (source.Id != id || source.Name != name) changed = true;
                normalizedGroups.Add(new ShopGroupConfig { Id = id, Name = name });
            }

            var defaultIndex = normalizedGroups.FindIndex(group => string.Equals(
                group.Id, DefaultGroupId, StringComparison.OrdinalIgnoreCase));
            if (defaultIndex < 0)
            {
                normalizedGroups.Insert(0, new ShopGroupConfig
                    { Id = DefaultGroupId, Name = DefaultGroupId });
                changed = true;
            }
            else if (defaultIndex > 0)
            {
                var defaultGroup = normalizedGroups[defaultIndex];
                normalizedGroups.RemoveAt(defaultIndex);
                normalizedGroups.Insert(0, defaultGroup);
                changed = true;
            }
            settings.Groups = normalizedGroups;

            var canonicalGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in settings.Groups) canonicalGroups[group.Id] = group.Id;

            var normalizedItems = new List<ShopItemConfig>();
            var seenItems = new HashSet<ushort>();
            foreach (var item in settings.Items)
            {
                if (!seenItems.Add(item.ItemId))
                {
                    changed = true;
                    continue;
                }
                changed |= NormalizeProduct(item.Group, item.Note, canonicalGroups,
                    (group, note) => { item.Group = group; item.Note = note; });
                normalizedItems.Add(item);
            }
            settings.Items = normalizedItems;

            var positions = new List<CatalogPosition>();
            foreach (var item in settings.Items)
            {
                positions.Add(new CatalogPosition(item.Order, value => item.Order = value));
            }

            var seenPositions = new HashSet<int>();
            var validOrder = true;
            foreach (var position in positions)
            {
                if (position.Order < 1 || !seenPositions.Add(position.Order))
                {
                    validOrder = false;
                    break;
                }
            }
            if (!validOrder)
            {
                for (var i = 0; i < positions.Count; i++) positions[i].Set(i + 1);
                changed = positions.Count > 0 || changed;
            }
            return changed;
        }

        private static bool NormalizeProduct(
            string? originalGroup, string? originalNote,
            IReadOnlyDictionary<string, string> canonicalGroups,
            Action<string, string> apply)
        {
            var requested = string.IsNullOrWhiteSpace(originalGroup)
                ? DefaultGroupId : originalGroup.Trim();
            if (!canonicalGroups.TryGetValue(requested, out var group))
            {
                group = DefaultGroupId;
            }
            var note = originalNote ?? string.Empty;
            var changed = originalGroup != group || originalNote != note;
            apply(group, note);
            return changed;
        }

        private sealed class CatalogPosition
        {
            public CatalogPosition(int order, Action<int> set)
            {
                Order = order;
                Set = set;
            }
            public int Order { get; }
            public Action<int> Set { get; }
        }
    }
}
