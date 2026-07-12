using well404.AutoSave;
using Xunit;

namespace well404.AutoSave.Tests
{
    public class ExcludeMatcherTests
    {
        private static readonly string[] DefaultPatterns =
        {
            "Workshop/**",
            "Steam/**",
            "Bundles/**",
            "OpenMod/packages/**",
            "**/logs/**",
            "**/Logs/**",
            "**/*~",
            "**/*.bak"
        };

        private static ExcludeMatcher Default() => new ExcludeMatcher(DefaultPatterns);

        [Theory]
        [InlineData("Workshop/Content/123/Map.unity3d")]
        [InlineData("Workshop/a.dat")]
        [InlineData("OpenMod/packages/SomeLib/lib.dll")]
        [InlineData("OpenMod/logs/server.log")]
        [InlineData("logs/server.log")]
        [InlineData("Config.txt~")]
        [InlineData("Server/Adminlist.dat~")]
        [InlineData("Level/PEI/some.bak")]
        public void Excludes_DownloadableAndTransientContent(string path)
        {
            Assert.True(Default().IsExcluded(path));
        }

        [Theory]
        [InlineData("Level/PEI/Level.dat")]
        [InlineData("Players/76561198000000000_0/Player/Life.dat")]
        [InlineData("Server/Adminlist.dat")]
        [InlineData("Config.txt")]
        [InlineData("OpenMod/openmod.users.yaml")]
        [InlineData("OpenMod/plugins/well404.Economy/economy.sqlite3")]
        public void Keeps_RealSaveData(string path)
        {
            Assert.False(Default().IsExcluded(path));
        }

        [Fact]
        public void Matching_IsCaseInsensitive()
        {
            Assert.True(Default().IsExcluded("workshop/content/a.dat"));
        }

        [Fact]
        public void DirectoryForm_IsPrunedForRecursivePatterns()
        {
            // The backup walk prunes a subtree by testing "<dir>/".
            var matcher = Default();
            Assert.True(matcher.IsExcluded("Workshop/"));
            Assert.True(matcher.IsExcluded("OpenMod/packages/"));
            Assert.True(matcher.IsExcluded("OpenMod/logs/"));
            Assert.False(matcher.IsExcluded("OpenMod/"));
            Assert.False(matcher.IsExcluded("Level/"));
        }

        [Fact]
        public void SingleStar_DoesNotCrossSegments()
        {
            var matcher = new ExcludeMatcher(new[] { "Level/*.dat" });
            Assert.True(matcher.IsExcluded("Level/Level.dat"));
            Assert.False(matcher.IsExcluded("Level/PEI/Level.dat"));
        }

        [Fact]
        public void EmptyPatterns_ExcludeNothing()
        {
            var matcher = new ExcludeMatcher(new string[0]);
            Assert.False(matcher.IsExcluded("anything/at/all.dat"));
        }
    }
}
