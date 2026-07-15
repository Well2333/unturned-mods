using System.IO;
using System.Linq;
using well404.Essentials;
using Xunit;

namespace well404.Essentials.Tests
{
    public class PlayerUiAndWarpTests
    {
        private static string Resource(string suffix)
        {
            var assembly = typeof(EssentialsPlayerMenu).Assembly;
            var name = assembly.GetManifestResourceNames().Single(value => value.EndsWith(suffix));
            using var stream = assembly.GetManifestResourceStream(name);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }

        [Fact]
        public void PlayerUi_MergesPlayersAndPartyAndKeepsPlayersFirst()
        {
            var script = Resource(".player-ui.js");
            Assert.Contains("players-party", script);
            Assert.Contains("subsection(parent,zh?\"在线玩家\"", script);
            Assert.Contains("subsection(parent,zh?\"队伍\"", script);
            Assert.True(script.IndexOf("在线玩家", System.StringComparison.Ordinal)
                < script.LastIndexOf("队伍", System.StringComparison.Ordinal));
        }

        [Fact]
        public void PlayerUi_FiltersWarpsByServerMetadata()
        {
            var script = Resource(".player-ui.js");
            Assert.Contains("warpCategory", script);
            Assert.Contains("well404.essentials.player.warp-filter", script);
            Assert.Contains("categories.includes(value)", script);
        }

        [Fact]
        public void AdminUi_UsesTabbedWarpCollectionStyles()
        {
            var css = Resource(".admin-ui.css");
            var module = File.ReadAllText(Path.Combine(
                System.AppContext.BaseDirectory, "../../../../../src/plugins/well404.Essentials/EssentialsWebPanelModule.cs"));
            Assert.Contains(".tiles", css);
            Assert.Contains("layout: \"tabs-grid\"", module);
            Assert.Contains("reorderHandler", module);
        }
    }
}
