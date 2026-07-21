using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using well404.Essentials.Data;
using well404.Essentials.Teleport;
using well404.Essentials.Warps;
using Xunit;

namespace well404.Essentials.Tests
{
    public class WarpMapProjectionTests
    {
        [Theory]
        [InlineData(0f, 0f, 1000f, 0.5f, 0.5f)]
        [InlineData(-500f, 500f, 1000f, 0f, 0f)]
        [InlineData(500f, -500f, 1000f, 1f, 1f)]
        [InlineData(0f, 250f, 1000f, 0.5f, 0.25f)]
        public void SquareProjection_MatchesNativeChartOrientation(
            float x, float z, float size, float expectedHorizontal, float expectedVertical)
        {
            var success = WarpMapProjection.TryProjectSquare(
                x, z, size, out var horizontal, out var vertical);

            Assert.True(success);
            Assert.Equal(expectedHorizontal, horizontal, 5);
            Assert.Equal(expectedVertical, vertical, 5);
        }

        [Theory]
        [InlineData(501f, 0f, 1000f)]
        [InlineData(0f, -501f, 1000f)]
        [InlineData(0f, 0f, 0f)]
        public void SquareProjection_RejectsInvalidOrOutOfBoundsPoints(float x, float z, float size)
        {
            Assert.False(WarpMapProjection.TryProjectSquare(x, z, size, out _, out _));
        }


        [Fact]
        public void MapState_TracksGpsAndChartAvailabilityIndependently()
        {
            var state = new WarpMapState(
                "PEI",
                "Chart.png",
                true,
                "available",
                "Map.png",
                true,
                "available");

            var gpsOnly = state.WithVisibility(false, true);

            Assert.False(gpsOnly.ChartAvailable);
            Assert.Equal("locked", gpsOnly.ChartReason);
            Assert.True(gpsOnly.GpsAvailable);
            Assert.True(gpsOnly.IsAvailable(WarpMapService.GpsAssetId));
            Assert.False(gpsOnly.IsAvailable(WarpMapService.ChartAssetId));
            Assert.Equal("Map.png", gpsOnly.GetPath(WarpMapService.GpsAssetId));
            Assert.True(gpsOnly.Available);
        }

        [Fact]
        public void MapState_ReportsLockedWhenBothExistingImagesAreHidden()
        {
            var state = new WarpMapState(
                "PEI",
                "Chart.png",
                true,
                "available",
                "Map.png",
                true,
                "available");

            var locked = state.WithVisibility(false, false);

            Assert.False(locked.Available);
            Assert.Equal("locked", locked.Reason);
        }

        [Fact]
        public void PlayerLocation_MapIdentityIsExplicitAndLegacySafe()
        {
            var current = new well404.Essentials.Data.PlayerLocation(1f, 2f, 3f, 4f, " PEI ");
            var legacy = new well404.Essentials.Data.PlayerLocation(1f, 2f, 3f, 4f);

            Assert.Equal("PEI", current.Map);
            Assert.True(WarpMapProjection.MatchesMap(current.Map, "pei"));
            Assert.Equal(string.Empty, legacy.Map);
            Assert.False(WarpMapProjection.MatchesMap(legacy.Map, "PEI"));
        }

        [Theory]
        [InlineData("PEI", "pei", true)]
        [InlineData(" California ", "California", true)]
        [InlineData("", "PEI", false)]
        [InlineData("PEI", "Washington", false)]
        public void MapIdentity_RequiresAnExplicitMatchingMap(string warpMap, string currentMap, bool expected)
        {
            Assert.Equal(expected, WarpMapProjection.MatchesMap(warpMap, currentMap));
        }

        [Fact]
        public void TeleportDestination_RejectsLegacyAndDifferentMapLocations()
        {
            Assert.True(TeleportService.IsDestinationOnCurrentMap(new PlayerLocation(1, 2, 3, 4, "PEI"), "pei"));
            Assert.False(TeleportService.IsDestinationOnCurrentMap(new PlayerLocation(1, 2, 3, 4), "PEI"));
            Assert.False(TeleportService.IsDestinationOnCurrentMap(new PlayerLocation(1, 2, 3, 4, "PEI"), "Washington"));
            Assert.False(TeleportService.IsDestinationOnCurrentMap(null, "PEI"));
        }

        [Fact]
        public async Task MapAssetCache_IsLimitedAndScopedToTheRequestedCurrentMap()
        {
            Assert.Equal(64L * 1024L * 1024L, WarpMapService.MaxMapBytes);
            Assert.Equal(64L * 1024L * 1024L, WarpMapService.MaxCacheBytes);
            var directory = Path.Combine(Path.GetTempPath(), "warp-map-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var first = Path.Combine(directory, "first.png");
                var second = Path.Combine(directory, "second.png");
                var tooLarge = Path.Combine(directory, "too-large.png");
                File.WriteAllBytes(first, new byte[6]);
                File.WriteAllBytes(second, new byte[6]);
                File.WriteAllBytes(tooLarge, new byte[9]);
                var service = new WarpMapService(new ConfigurationBuilder().Build(), 8, 8);

                Assert.NotNull(await service.LoadMapAssetAsync(first, WarpMapService.GpsAssetId, "PEI"));
                Assert.Equal(1, service.CachedAssetCount);
                Assert.Equal(6, service.CachedByteCount);
                Assert.NotNull(await service.LoadMapAssetAsync(second, WarpMapService.ChartAssetId, "PEI"));
                Assert.Equal(1, service.CachedAssetCount);
                Assert.Equal(6, service.CachedByteCount);
                Assert.Null(await service.LoadMapAssetAsync(tooLarge, WarpMapService.GpsAssetId, "PEI"));

                Assert.NotNull(await service.LoadMapAssetAsync(first, WarpMapService.GpsAssetId, "Washington"));
                Assert.Equal(1, service.CachedAssetCount);
                Assert.Equal(6, service.CachedByteCount);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
