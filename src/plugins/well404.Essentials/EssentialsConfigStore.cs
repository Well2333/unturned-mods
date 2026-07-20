using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using OpenMod.API.Plugins;

namespace well404.Essentials
{
    /// <summary>
    /// Authoritative in-memory config shared by WebPanel and in-game warp commands. Writes rewrite
    /// config.yaml atomically from the caller's perspective; a new store is created on plugin reload.
    /// </summary>
    public sealed class EssentialsConfigStore
    {
        private readonly IPluginAccessor<EssentialsPlugin> m_PluginAccessor;
        private readonly object m_Lock = new object();
        private readonly EssentialsSettings m_Settings;
        private bool m_ConfigMigrationPending;

        public EssentialsConfigStore(IConfiguration configuration, IPluginAccessor<EssentialsPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
            m_Settings = configuration.Get<EssentialsSettings>() ?? new EssentialsSettings();
            m_Settings.WarpTags = m_Settings.WarpTags ?? new WarpTagSettings();
            m_ConfigMigrationPending = NormalizeWarps(m_Settings.Warps)
                | NormalizeWarpTagSettings(m_Settings.WarpTags, m_Settings.Warps);
        }

        /// <summary>
        /// Persists legacy warp fields, default tag definitions and unknown-tag migration after the
        /// plugin instance and working directory are available.
        /// </summary>
        public void PersistMigrationIfNeeded(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                throw new ArgumentException("A plugin working directory is required.", nameof(workingDirectory));
            }

            lock (m_Lock)
            {
                if (!m_ConfigMigrationPending) return;
                var path = Path.Combine(workingDirectory, "config.yaml");
                File.WriteAllText(path, EssentialsYaml.Serialize(m_Settings), new UTF8Encoding(false));
                m_ConfigMigrationPending = false;
            }
        }

        private string ConfigPath
        {
            get
            {
                var plugin = m_PluginAccessor.Instance
                    ?? throw new InvalidOperationException("The Essentials plugin is not loaded.");
                return Path.Combine(plugin.WorkingDirectory, "config.yaml");
            }
        }

        public T Read<T>(Func<EssentialsSettings, T> reader)
        {
            lock (m_Lock) return reader(m_Settings);
        }

        public void Update(Action<EssentialsSettings> mutate)
        {
            lock (m_Lock)
            {
                mutate(m_Settings);
                Save();
            }
        }

        public IReadOnlyList<WarpEntry> Warps
        {
            get
            {
                lock (m_Lock) return m_Settings.Warps.OrderBy(w => w.Order).ToList();
            }
        }

        public WarpEntry? FindWarp(string name)
        {
            lock (m_Lock)
            {
                return m_Settings.Warps.Find(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void UpsertWarp(WarpEntry entry)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Warps.FindIndex(w => string.Equals(w.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    var existing = m_Settings.Warps[index];
                    if (entry.Tags == null || entry.Tags.Count == 0) entry.Tags = new List<string>(existing.Tags);
                    if (entry.Order <= 0) entry.Order = existing.Order;
                    m_Settings.Warps[index] = entry;
                }
                else
                {
                    entry.Order = m_Settings.Warps.Count == 0 ? 1 : m_Settings.Warps.Max(w => w.Order) + 1;
                    m_Settings.Warps.Add(entry);
                }

                entry.Tags = NormalizeTags(entry.Tags);
                entry.Category = string.Empty;
                NormalizeWarps(m_Settings.Warps);
                EnsureCustomWarpTagsLocked(entry.Tags);
                Save();
            }
        }

        /// <summary>Reorders the entries visible through one tag filter while preserving the others.</summary>
        public bool ReorderWarps(string tag, IReadOnlyList<string> names)
        {
            lock (m_Lock)
            {
                tag = NormalizeTag(tag);
                var ordered = m_Settings.Warps.OrderBy(w => w.Order).ToList();
                var taggedEntries = string.Equals(tag, "__all__", StringComparison.Ordinal)
                    ? ordered
                    : ordered.Where(w => HasTag(w, tag)).ToList();
                if (taggedEntries.Count != names.Count
                    || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count
                    || names.Any(name => taggedEntries.All(w => !string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase))))
                {
                    return false;
                }

                var byName = taggedEntries.ToDictionary(w => w.Name, StringComparer.OrdinalIgnoreCase);
                var replacements = names.Select(name => byName[name]).ToList();
                var replacementIndex = 0;
                for (var i = 0; i < ordered.Count; i++)
                {
                    if (string.Equals(tag, "__all__", StringComparison.Ordinal) || HasTag(ordered[i], tag))
                    {
                        ordered[i] = replacements[replacementIndex++];
                    }
                }

                m_Settings.Warps.Clear();
                m_Settings.Warps.AddRange(ordered);
                NormalizeWarps(m_Settings.Warps, true);
                Save();
                return true;
            }
        }

        public bool RemoveWarp(string name)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Warps.FindIndex(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return false;
                m_Settings.Warps.RemoveAt(index);
                NormalizeWarps(m_Settings.Warps, true);
                Save();
                return true;
            }
        }

        public IReadOnlyList<WarpTagDefinition> PresetWarpTags
        {
            get
            {
                lock (m_Lock) return m_Settings.WarpTags.Presets.Select(CloneTag).ToList();
            }
        }

        public IReadOnlyList<WarpTagDefinition> CustomWarpTags
        {
            get
            {
                lock (m_Lock) return m_Settings.WarpTags.Custom.Select(CloneTag).ToList();
            }
        }

        public WarpTagDefinition? FindWarpTag(string id)
        {
            lock (m_Lock)
            {
                var found = AllWarpTagsLocked().FirstOrDefault(tag =>
                    string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase));
                return found == null ? null : CloneTag(found);
            }
        }

        public string ResolveWarpTagLabel(string id, string language, bool includeEmoji = true)
        {
            lock (m_Lock)
            {
                var found = AllWarpTagsLocked().FirstOrDefault(tag =>
                    string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase));
                if (found == null) return id;
                var name = string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase)
                    ? found.NameZh : found.NameEn;
                if (string.IsNullOrWhiteSpace(name)) name = found.Id;
                return includeEmoji && !string.IsNullOrWhiteSpace(found.Emoji)
                    ? found.Emoji + " " + name : name;
            }
        }

        public string ResolveWarpTagEmoji(IEnumerable<string>? ids)
        {
            lock (m_Lock)
            {
                foreach (var id in ids ?? Array.Empty<string>())
                {
                    var found = AllWarpTagsLocked().FirstOrDefault(tag =>
                        string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (found != null && !string.IsNullOrWhiteSpace(found.Emoji)) return found.Emoji;
                }

                return string.Empty;
            }
        }

        public void UpsertWarpTag(WarpTagDefinition definition, bool preset)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            lock (m_Lock)
            {
                var normalized = NormalizeDefinition(definition);
                if (normalized.Id.Length == 0) throw new ArgumentException("A tag ID is required.", nameof(definition));
                m_Settings.WarpTags.Presets.RemoveAll(tag =>
                    string.Equals(tag.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
                m_Settings.WarpTags.Custom.RemoveAll(tag =>
                    string.Equals(tag.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
                (preset ? m_Settings.WarpTags.Presets : m_Settings.WarpTags.Custom).Add(normalized);
                Save();
            }
        }

        public bool RemoveWarpTag(string id)
        {
            lock (m_Lock)
            {
                var removed = m_Settings.WarpTags.Presets.RemoveAll(tag =>
                                  string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase))
                              + m_Settings.WarpTags.Custom.RemoveAll(tag =>
                                  string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase));
                if (removed == 0) return false;
                Save();
                return true;
            }
        }

        public void EnsureCustomWarpTags(IEnumerable<string>? ids)
        {
            lock (m_Lock)
            {
                if (EnsureCustomWarpTagsLocked(ids)) Save();
            }
        }

        private bool EnsureCustomWarpTagsLocked(IEnumerable<string>? ids)
        {
            var changed = false;
            foreach (var id in NormalizeTags(ids))
            {
                if (AllWarpTagsLocked().Any(tag => string.Equals(tag.Id, id, StringComparison.OrdinalIgnoreCase))) continue;
                m_Settings.WarpTags.Custom.Add(new WarpTagDefinition
                {
                    Id = id,
                    NameEn = id,
                    NameZh = id,
                    Emoji = string.Empty
                });
                changed = true;
            }

            return changed;
        }

        private IEnumerable<WarpTagDefinition> AllWarpTagsLocked()
            => m_Settings.WarpTags.Presets.Concat(m_Settings.WarpTags.Custom);

        public IReadOnlyList<GiftEntry> Gifts
        {
            get
            {
                lock (m_Lock) return new List<GiftEntry>(m_Settings.Gifts);
            }
        }

        public void UpsertGift(GiftEntry entry)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Gifts.FindIndex(g => string.Equals(g.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) m_Settings.Gifts[index] = entry;
                else m_Settings.Gifts.Add(entry);
                Save();
            }
        }

        public bool RemoveGift(string id)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Gifts.FindIndex(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return false;
                m_Settings.Gifts.RemoveAt(index);
                Save();
                return true;
            }
        }

        internal static string NormalizeTag(string? tag)
            => string.IsNullOrWhiteSpace(tag) ? "default" : tag.Trim().ToLowerInvariant();

        internal static List<string> ParseTags(IEnumerable<string>? values)
        {
            var tags = new List<string>();
            if (values != null)
            {
                foreach (var value in values)
                {
                    foreach (var tag in (value ?? string.Empty)
                                 .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var normalized = NormalizeTag(tag);
                        if (!tags.Contains(normalized, StringComparer.OrdinalIgnoreCase)) tags.Add(normalized);
                    }
                }
            }

            if (tags.Count == 0) tags.Add("default");
            return tags;
        }

        internal static List<string> NormalizeTags(IEnumerable<string>? tags) => ParseTags(tags);

        internal static bool HasTag(WarpEntry warp, string tag)
            => warp.Tags.Any(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));

        internal static bool NormalizeWarps(List<WarpEntry> warps, bool forceOrder = false)
        {
            var changed = false;
            var used = new HashSet<int>();
            var next = 1;
            foreach (var warp in warps)
            {
                IEnumerable<string> sourceTags = warp.Tags == null || warp.Tags.Count == 0
                    ? new[] { warp.Category }
                    : warp.Tags;
                var tags = NormalizeTags(sourceTags);
                if (warp.Tags == null || !warp.Tags.SequenceEqual(tags, StringComparer.Ordinal)) changed = true;
                warp.Tags = tags;
                if (!string.IsNullOrEmpty(warp.Category)) changed = true;
                warp.Category = string.Empty;

                if (forceOrder || warp.Order <= 0 || used.Contains(warp.Order))
                {
                    while (used.Contains(next)) next++;
                    if (warp.Order != next) changed = true;
                    warp.Order = next;
                }
                used.Add(warp.Order);
                next = Math.Max(next, warp.Order + 1);
            }
            return changed;
        }

        internal static bool NormalizeWarpTagSettings(WarpTagSettings settings, IEnumerable<WarpEntry>? warps)
        {
            var changed = false;
            settings.Presets = settings.Presets ?? new List<WarpTagDefinition>();
            settings.Custom = settings.Custom ?? new List<WarpTagDefinition>();
            if (!settings.Initialized)
            {
                foreach (var definition in DefaultWarpTags())
                {
                    if (!settings.Presets.Concat(settings.Custom).Any(tag =>
                            string.Equals(tag.Id, definition.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        settings.Presets.Add(definition);
                    }
                }

                settings.Initialized = true;
                changed = true;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            changed |= NormalizeDefinitions(settings.Presets, seen);
            changed |= NormalizeDefinitions(settings.Custom, seen);
            foreach (var id in (warps ?? Array.Empty<WarpEntry>()).SelectMany(warp => NormalizeTags(warp.Tags)))
            {
                if (seen.Add(id))
                {
                    settings.Custom.Add(new WarpTagDefinition
                    {
                        Id = id,
                        NameEn = id,
                        NameZh = id,
                        Emoji = string.Empty
                    });
                    changed = true;
                }
            }

            return changed;
        }

        internal static IReadOnlyList<WarpTagDefinition> DefaultWarpTags()
            => new[]
            {
                new WarpTagDefinition { Id = "default", NameEn = "Other", NameZh = "其他", Emoji = "📍" },
                new WarpTagDefinition { Id = "city", NameEn = "City", NameZh = "城市", Emoji = "🏙️" },
                new WarpTagDefinition { Id = "countryside", NameEn = "Countryside", NameZh = "乡村", Emoji = "🌾" },
                new WarpTagDefinition { Id = "military-base", NameEn = "Military Base", NameZh = "军事基地", Emoji = "🪖" },
                new WarpTagDefinition { Id = "safe-zone", NameEn = "Safe Zone", NameZh = "安全区", Emoji = "🛡️" },
                new WarpTagDefinition { Id = "virus-zone", NameEn = "Virus Zone", NameZh = "病毒区", Emoji = "☣️" },
                new WarpTagDefinition { Id = "resource-point", NameEn = "Resource Point", NameZh = "资源点", Emoji = "⛏️" },
                new WarpTagDefinition { Id = "npc", NameEn = "NPC", NameZh = "NPC", Emoji = "🧑" }
            };

        private static bool NormalizeDefinitions(List<WarpTagDefinition> definitions, ISet<string> seen)
        {
            var changed = false;
            for (var i = definitions.Count - 1; i >= 0; i--)
            {
                var original = definitions[i];
                if (original == null || string.IsNullOrWhiteSpace(original.Id))
                {
                    definitions.RemoveAt(i);
                    changed = true;
                    continue;
                }

                var normalized = NormalizeDefinition(original);
                if (!seen.Add(normalized.Id))
                {
                    definitions.RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (original.Id != normalized.Id || original.NameEn != normalized.NameEn
                    || original.NameZh != normalized.NameZh || original.Emoji != normalized.Emoji)
                {
                    definitions[i] = normalized;
                    changed = true;
                }
            }

            return changed;
        }

        private static WarpTagDefinition NormalizeDefinition(WarpTagDefinition definition)
        {
            var id = string.IsNullOrWhiteSpace(definition.Id)
                ? string.Empty : definition.Id.Trim().ToLowerInvariant();
            var nameEn = string.IsNullOrWhiteSpace(definition.NameEn) ? id : definition.NameEn.Trim();
            var nameZh = string.IsNullOrWhiteSpace(definition.NameZh) ? nameEn : definition.NameZh.Trim();
            return new WarpTagDefinition
            {
                Id = id,
                NameEn = nameEn,
                NameZh = nameZh,
                Emoji = definition.Emoji?.Trim() ?? string.Empty
            };
        }

        private static WarpTagDefinition CloneTag(WarpTagDefinition definition)
            => new WarpTagDefinition
            {
                Id = definition.Id,
                NameEn = definition.NameEn,
                NameZh = definition.NameZh,
                Emoji = definition.Emoji
            };

        private void Save()
            => File.WriteAllText(ConfigPath, EssentialsYaml.Serialize(m_Settings), new UTF8Encoding(false));
    }
}
