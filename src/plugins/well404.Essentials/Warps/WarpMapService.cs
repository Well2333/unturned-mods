using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenMod.Unturned.Users;
using SDG.Unturned;
using UnityEngine;
using UnturnedMods.Shared.WebPanel;
using well404.Essentials.Data;

namespace well404.Essentials.Warps
{
    /// <summary>
    /// Resolves the active map's paper chart and GPS satellite image, mirrors Unturned's
    /// independent visibility rules for each, and projects world positions into normalized map
    /// coordinates. Public methods touching Unturned or Unity state run on the main thread.
    /// </summary>
    public sealed class WarpMapService : IWebPanelModuleAssetProvider
    {
        public const string ChartAssetId = "chart";
        public const string GpsAssetId = "gps";
        internal const long MaxMapBytes = 64L * 1024L * 1024L;
        internal const long MaxCacheBytes = 64L * 1024L * 1024L;

        private readonly IConfiguration m_Configuration;
        private readonly long m_MaxMapBytes;
        private readonly long m_MaxCacheBytes;
        private readonly object m_CacheSync = new object();
        private readonly Dictionary<string, CachedMapAsset> m_Cache = new Dictionary<string, CachedMapAsset>();
        private string m_CachedMapName = string.Empty;
        private long m_CachedBytes;
        private long m_CacheSequence;

        public WarpMapService(IConfiguration configuration)
            : this(configuration, MaxMapBytes, MaxCacheBytes)
        {
        }

        internal WarpMapService(IConfiguration configuration, long maxMapBytes, long maxCacheBytes)
        {
            m_Configuration = configuration;
            m_MaxMapBytes = maxMapBytes > 0 ? maxMapBytes : throw new ArgumentOutOfRangeException(nameof(maxMapBytes));
            m_MaxCacheBytes = maxCacheBytes > 0 ? maxCacheBytes : throw new ArgumentOutOfRangeException(nameof(maxCacheBytes));
        }

        internal int CachedAssetCount { get { lock (m_CacheSync) return m_Cache.Count; } }
        internal long CachedByteCount { get { lock (m_CacheSync) return m_CachedBytes; } }

        public string CurrentMapName => Level.info?.name?.Trim() ?? string.Empty;

        public bool IsCurrentMap(WarpEntry warp)
            => warp != null && WarpMapProjection.MatchesMap(warp.Map, CurrentMapName);

        public bool IsCurrentMap(PlayerLocation location)
            => location != null && WarpMapProjection.MatchesMap(location.Map, CurrentMapName);

        public WarpMapState GetState(UnturnedUser user)
        {
            var settings = m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings();
            if (!settings.WarpMap.Enabled)
            {
                return WarpMapState.Unavailable(CurrentMapName, "disabled");
            }

            var state = GetAdminState();
            return state.WithVisibility(
                CanViewChart(user, settings.WarpMap.Visibility),
                CanViewGps(user, settings.WarpMap.Visibility));
        }

        public WarpMapState GetAdminState() => ResolveMapState();

        public bool TryProject(WarpEntry warp, out float horizontal, out float vertical)
        {
            horizontal = 0f;
            vertical = 0f;
            if (!IsCurrentMap(warp))
            {
                return false;
            }

            return TryProjectPosition(
                new Vector3((float)warp.X, (float)warp.Y, (float)warp.Z),
                out horizontal,
                out vertical);
        }

        public bool TryProject(PlayerLocation location, out float horizontal, out float vertical)
        {
            horizontal = 0f;
            vertical = 0f;
            if (!IsCurrentMap(location))
            {
                return false;
            }

            return TryProjectPosition(
                new Vector3(location.X, location.Y, location.Z),
                out horizontal,
                out vertical);
        }

        private static bool TryProjectPosition(
            Vector3 worldPosition, out float horizontal, out float vertical)
        {
            var manager = CartographyVolumeManager.Get();
            var volume = manager?.GetMainVolume();
            if (volume != null)
            {
                var local = volume.transform.InverseTransformPoint(worldPosition);
                horizontal = local.x + 0.5f;
                vertical = 0.5f - local.z;
            }
            else
            {
                var levelSize = Level.size - (Level.border * 2f);
                return WarpMapProjection.TryProjectSquare(
                    worldPosition.x, worldPosition.z, levelSize, out horizontal, out vertical);
            }

            return WarpMapProjection.IsNormalized(horizontal, vertical);
        }

        public async Task<PlayerMenuAsset?> GetAssetAsync(UnturnedUser user, string assetId)
        {
            if (!IsMapAssetId(assetId))
            {
                return null;
            }

            await UniTask.SwitchToMainThread();
            var state = GetState(user);
            return state.IsAvailable(assetId)
                ? await LoadMapAssetAsync(state.GetPath(assetId), assetId, state.MapName).ConfigureAwait(false)
                : null;
        }

        public async Task<PlayerMenuAsset?> GetAssetAsync(string assetId)
        {
            if (!IsMapAssetId(assetId))
            {
                return null;
            }

            await UniTask.SwitchToMainThread();
            var state = GetAdminState();
            return state.IsAvailable(assetId)
                ? await LoadMapAssetAsync(state.GetPath(assetId), assetId, state.MapName).ConfigureAwait(false)
                : null;
        }

        public static bool IsMapAssetId(string assetId)
            => string.Equals(assetId, ChartAssetId, StringComparison.Ordinal) ||
               string.Equals(assetId, GpsAssetId, StringComparison.Ordinal);

        internal async Task<PlayerMenuAsset?> LoadMapAssetAsync(string path, string assetId, string mapName)
        {
            var info = new FileInfo(path);
            var length = info.Length;
            if (length <= 0 || length > m_MaxMapBytes)
            {
                return null;
            }
            var writeTicks = info.LastWriteTimeUtc.Ticks;
            lock (m_CacheSync)
            {
                if (!string.Equals(m_CachedMapName, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    m_Cache.Clear();
                    m_CachedBytes = 0;
                    m_CachedMapName = mapName;
                }
                if (m_Cache.TryGetValue(path, out var cached) && cached.Length == length &&
                    cached.WriteTicks == writeTicks)
                {
                    cached.LastAccess = ++m_CacheSequence;
                    return new PlayerMenuAsset(cached.Bytes, "image/png", cached.EntityTag);
                }
            }

            var bytes = await Task.Run(() => ReadBoundedFile(path, m_MaxMapBytes)).ConfigureAwait(false);
            if (bytes == null)
            {
                return null;
            }

            var entityTag = assetId + "-" + length + "-" + writeTicks;
            lock (m_CacheSync)
            {
                // A request for a previous map can finish after a map switch. Return its bytes to
                // that caller, but never let it evict assets belonging to the current map.
                if (!string.Equals(m_CachedMapName, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    return new PlayerMenuAsset(bytes, "image/png", entityTag);
                }

                if (m_Cache.TryGetValue(path, out var replaced))
                {
                    m_CachedBytes -= replaced.Bytes.LongLength;
                    m_Cache.Remove(path);
                }
                while (m_Cache.Count > 0 && m_CachedBytes + bytes.LongLength > m_MaxCacheBytes)
                {
                    var oldest = m_Cache.OrderBy(pair => pair.Value.LastAccess).First();
                    m_CachedBytes -= oldest.Value.Bytes.LongLength;
                    m_Cache.Remove(oldest.Key);
                }
                if (bytes.LongLength <= m_MaxCacheBytes)
                {
                    m_Cache[path] = new CachedMapAsset(length, writeTicks, bytes, entityTag, ++m_CacheSequence);
                    m_CachedBytes += bytes.LongLength;
                }
            }

            return new PlayerMenuAsset(bytes, "image/png", entityTag);
        }

        private static byte[]? ReadBoundedFile(string path, long maximumBytes)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length <= 0 || stream.Length > maximumBytes || stream.Length > int.MaxValue)
            {
                return null;
            }

            var bytes = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0) return null;
                offset += read;
            }

            return bytes;
        }

        private WarpMapState ResolveMapState()
        {
            var mapName = CurrentMapName;
            if (Level.info == null || string.IsNullOrEmpty(mapName))
            {
                return WarpMapState.Unavailable(string.Empty, "unavailable");
            }

            var chartPath = Path.Combine(Level.info.path, "Chart.png");
            var gpsPath = Path.Combine(Level.info.path, "Map.png");
            ResolveAsset(chartPath, out var chartAvailable, out var chartReason);
            ResolveAsset(gpsPath, out var gpsAvailable, out var gpsReason);
            return new WarpMapState(
                mapName,
                chartPath,
                chartAvailable,
                chartReason,
                gpsPath,
                gpsAvailable,
                gpsReason);
        }

        private void ResolveAsset(string path, out bool available, out string reason)
        {
            if (!File.Exists(path))
            {
                available = false;
                reason = "missing";
                return;
            }

            var length = new FileInfo(path).Length;
            if (length <= 0 || length > m_MaxMapBytes)
            {
                available = false;
                reason = "too-large";
                return;
            }

            available = true;
            reason = "available";
        }

        private static bool CanViewChart(UnturnedUser user, string visibility)
        {
            if (HasUnrestrictedMapVisibility(visibility) || Provider.modeConfigData.Gameplay.Chart)
            {
                return true;
            }

            return HasMapItem(user, false);
        }

        private static bool CanViewGps(UnturnedUser user, string visibility)
        {
            if (HasUnrestrictedMapVisibility(visibility) || Provider.modeConfigData.Gameplay.Satellite)
            {
                return true;
            }

            return HasMapItem(user, true);
        }

        private static bool HasUnrestrictedMapVisibility(string visibility)
            => string.Equals(visibility?.Trim(), "always", StringComparison.OrdinalIgnoreCase) ||
               (Level.info != null && Level.info.type != ELevelType.SURVIVAL);

        private static bool HasMapItem(UnturnedUser user, bool satellite)
        {
            var inventory = user.Player.Player.inventory;
            for (byte page = 0; page < PlayerInventory.PAGES - 2; page++)
            {
                var items = inventory.items[page];
                if (items == null)
                {
                    continue;
                }

                foreach (var jar in items.items)
                {
                    var map = jar?.GetAsset<ItemMapAsset>();
                    if (map != null && (satellite ? map.enablesMap : map.enablesChart))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private sealed class CachedMapAsset
        {
            public CachedMapAsset(long length, long writeTicks, byte[] bytes, string entityTag, long lastAccess)
            {
                Length = length;
                WriteTicks = writeTicks;
                Bytes = bytes;
                EntityTag = entityTag;
                LastAccess = lastAccess;
            }

            public long Length { get; }
            public long WriteTicks { get; }
            public byte[] Bytes { get; }
            public string EntityTag { get; }
            public long LastAccess { get; set; }
        }
    }

    public sealed class WarpMapState
    {
        public WarpMapState(
            string mapName,
            string chartPath,
            bool chartAvailable,
            string chartReason,
            string gpsPath,
            bool gpsAvailable,
            string gpsReason)
        {
            MapName = mapName;
            ChartPath = chartPath;
            ChartAvailable = chartAvailable;
            ChartReason = chartReason;
            GpsPath = gpsPath;
            GpsAvailable = gpsAvailable;
            GpsReason = gpsReason;
        }

        public string MapName { get; }
        public string ChartPath { get; }
        public bool ChartAvailable { get; }
        public string ChartReason { get; }
        public string GpsPath { get; }
        public bool GpsAvailable { get; }
        public string GpsReason { get; }
        public bool Available => ChartAvailable || GpsAvailable;

        public string Reason
        {
            get
            {
                if (Available) return "available";
                if (ChartReason == "locked" || GpsReason == "locked") return "locked";
                if (ChartReason == "too-large" || GpsReason == "too-large") return "too-large";
                if (ChartReason == "missing" || GpsReason == "missing") return "missing";
                if (ChartReason == "disabled" || GpsReason == "disabled") return "disabled";
                return "unavailable";
            }
        }

        public bool IsAvailable(string assetId)
            => string.Equals(assetId, WarpMapService.GpsAssetId, StringComparison.Ordinal)
                ? GpsAvailable
                : string.Equals(assetId, WarpMapService.ChartAssetId, StringComparison.Ordinal) && ChartAvailable;

        public string GetPath(string assetId)
            => string.Equals(assetId, WarpMapService.GpsAssetId, StringComparison.Ordinal) ? GpsPath : ChartPath;

        public WarpMapState WithVisibility(bool chartVisible, bool gpsVisible)
            => new WarpMapState(
                MapName,
                ChartPath,
                ChartAvailable && chartVisible,
                ChartAvailable && !chartVisible ? "locked" : ChartReason,
                GpsPath,
                GpsAvailable && gpsVisible,
                GpsAvailable && !gpsVisible ? "locked" : GpsReason);

        public static WarpMapState Unavailable(string mapName, string reason)
            => new WarpMapState(mapName, string.Empty, false, reason, string.Empty, false, reason);
    }
}
