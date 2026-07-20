using System;
using System.Collections.Generic;
using System.IO;
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
        private const long MaxMapBytes = 16L * 1024L * 1024L;

        private readonly IConfiguration m_Configuration;
        private readonly object m_CacheSync = new object();
        private readonly Dictionary<string, CachedMapAsset> m_Cache = new Dictionary<string, CachedMapAsset>();

        public WarpMapService(IConfiguration configuration)
        {
            m_Configuration = configuration;
        }

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
                ? await LoadMapAssetAsync(state.GetPath(assetId), assetId).ConfigureAwait(false)
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
                ? await LoadMapAssetAsync(state.GetPath(assetId), assetId).ConfigureAwait(false)
                : null;
        }

        public static bool IsMapAssetId(string assetId)
            => string.Equals(assetId, ChartAssetId, StringComparison.Ordinal) ||
               string.Equals(assetId, GpsAssetId, StringComparison.Ordinal);

        private async Task<PlayerMenuAsset?> LoadMapAssetAsync(string path, string assetId)
        {
            var info = new FileInfo(path);
            var length = info.Length;
            var writeTicks = info.LastWriteTimeUtc.Ticks;
            lock (m_CacheSync)
            {
                if (m_Cache.TryGetValue(path, out var cached) && cached.Length == length &&
                    cached.WriteTicks == writeTicks)
                {
                    return new PlayerMenuAsset(cached.Bytes, "image/png", cached.EntityTag);
                }
            }

            var bytes = await Task.Run(() => File.ReadAllBytes(path)).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.LongLength > MaxMapBytes)
            {
                return null;
            }

            var entityTag = assetId + "-" + length + "-" + writeTicks;
            lock (m_CacheSync)
            {
                m_Cache[path] = new CachedMapAsset(length, writeTicks, bytes, entityTag);
            }

            return new PlayerMenuAsset(bytes, "image/png", entityTag);
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

        private static void ResolveAsset(string path, out bool available, out string reason)
        {
            if (!File.Exists(path))
            {
                available = false;
                reason = "missing";
                return;
            }

            var length = new FileInfo(path).Length;
            if (length <= 0 || length > MaxMapBytes)
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
            public CachedMapAsset(long length, long writeTicks, byte[] bytes, string entityTag)
            {
                Length = length;
                WriteTicks = writeTicks;
                Bytes = bytes;
                EntityTag = entityTag;
            }

            public long Length { get; }
            public long WriteTicks { get; }
            public byte[] Bytes { get; }
            public string EntityTag { get; }
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
