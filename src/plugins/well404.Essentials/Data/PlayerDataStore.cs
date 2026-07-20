using System;
using System.Threading;
using System.Threading.Tasks;
using OpenMod.API.Plugins;

namespace well404.Essentials.Data
{
    /// <summary>
    /// Persists per-player state (home, last death point, gift-claim times) through the
    /// plugin's <see cref="OpenMod.API.Persistence.IDataStore"/> (a YAML file in the working
    /// directory, key <c>players</c>).
    /// <para>
    /// The whole document is loaded into memory once and rewritten on each change. Like the
    /// economy provider, this is a global-friendly design: it reaches the plugin lazily via
    /// <see cref="IPluginAccessor{TPlugin}"/> so it can be a plugin-scoped singleton without
    /// capturing plugin services in its constructor.
    /// </para>
    /// </summary>
    public sealed class PlayerDataStore
    {
        private const string DataKey = "players";

        private readonly IPluginAccessor<EssentialsPlugin> m_PluginAccessor;
        private readonly SemaphoreSlim m_Gate = new SemaphoreSlim(1, 1);
        private PlayerDataDocument? m_Document;

        public PlayerDataStore(IPluginAccessor<EssentialsPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
        }

        private EssentialsPlugin Plugin
        {
            get
            {
                var plugin = m_PluginAccessor.Instance;
                if (plugin == null || !plugin.IsComponentAlive)
                {
                    throw new InvalidOperationException("The Essentials plugin is not loaded.");
                }

                return plugin;
            }
        }

        private async Task<PlayerDataDocument> GetDocumentAsync()
        {
            if (m_Document != null)
            {
                return m_Document;
            }

            await m_Gate.WaitAsync();
            try
            {
                if (m_Document == null)
                {
                    var store = Plugin.DataStore;
                    m_Document = await store.ExistsAsync(DataKey)
                        ? await store.LoadAsync<PlayerDataDocument>(DataKey) ?? new PlayerDataDocument()
                        : new PlayerDataDocument();
                }

                return m_Document;
            }
            finally
            {
                m_Gate.Release();
            }
        }

        private async Task SaveAsync()
        {
            await m_Gate.WaitAsync();
            try
            {
                if (m_Document != null)
                {
                    await Plugin.DataStore.SaveAsync(DataKey, m_Document);
                }
            }
            finally
            {
                m_Gate.Release();
            }
        }

        private static PlayerRecord GetOrAdd(PlayerDataDocument document, string steamId)
        {
            if (!document.Players.TryGetValue(steamId, out var record))
            {
                record = new PlayerRecord();
                document.Players[steamId] = record;
            }

            return record;
        }

        public async Task<PlayerLocation?> GetHomeAsync(string steamId)
        {
            var document = await GetDocumentAsync();
            return document.Players.TryGetValue(steamId, out var record) ? record.Home : null;
        }

        public async Task SetHomeAsync(string steamId, PlayerLocation location)
        {
            var document = await GetDocumentAsync();
            GetOrAdd(document, steamId).Home = location;
            await SaveAsync();
        }

        public async Task<PlayerLocation?> GetLastDeathAsync(string steamId)
        {
            var document = await GetDocumentAsync();
            return document.Players.TryGetValue(steamId, out var record) ? record.LastDeath : null;
        }

        public async Task SetLastDeathAsync(string steamId, PlayerLocation location)
        {
            var document = await GetDocumentAsync();
            GetOrAdd(document, steamId).LastDeath = location;
            await SaveAsync();
        }

        public async Task<string> GetWarpMapSizeAsync(string steamId)
        {
            var document = await GetDocumentAsync();
            return document.Players.TryGetValue(steamId, out var record)
                ? NormalizeWarpMapSize(record.WarpMapSize)
                : "compact";
        }

        public async Task SetWarpMapSizeAsync(string steamId, string value)
        {
            var document = await GetDocumentAsync();
            GetOrAdd(document, steamId).WarpMapSize = NormalizeWarpMapSize(value);
            await SaveAsync();
        }

        internal static string NormalizeWarpMapSize(string? value)
            => string.Equals(value, "large", StringComparison.OrdinalIgnoreCase)
                ? "large"
                : "compact";

        /// <summary>Last claim time (Unix UTC seconds) for the gift, or null if never claimed.</summary>
        public async Task<long?> GetGiftClaimAsync(string steamId, string giftId)
        {
            var document = await GetDocumentAsync();
            if (document.Players.TryGetValue(steamId, out var record)
                && record.GiftClaims.TryGetValue(giftId.ToLowerInvariant(), out var when))
            {
                return when;
            }

            return null;
        }

        public async Task SetGiftClaimAsync(string steamId, string giftId, long unixSeconds)
        {
            var document = await GetDocumentAsync();
            GetOrAdd(document, steamId).GiftClaims[giftId.ToLowerInvariant()] = unixSeconds;
            await SaveAsync();
        }
    }
}
