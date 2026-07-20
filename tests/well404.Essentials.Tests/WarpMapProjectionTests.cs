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
    }
}
