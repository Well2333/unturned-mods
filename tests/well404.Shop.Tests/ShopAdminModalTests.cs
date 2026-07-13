using System.IO;
using System.Linq;
using well404.Shop;
using Xunit;

namespace well404.Shop.Tests
{
    public class ShopAdminModalTests
    {
        [Fact]
        public void Backdrop_RequiresPressAndReleaseOutside()
        {
            var assembly = typeof(ShopPlugin).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(".admin-ui.js"));
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            var script = reader.ReadToEnd();

            Assert.Contains("pressedOutside=event.target===overlay", script);
            Assert.Contains("pressedOutside&&releasedOutside", script);
            Assert.DoesNotContain("overlay.onclick", script);
            Assert.Contains("aria-modal", script);
        }

        [Theory]
        [InlineData(".player-ui.css")]
        [InlineData(".admin-ui.css")]
        public void ProductCards_UseCompactSixColumnDesktopGrid(string suffix)
        {
            var assembly = typeof(ShopPlugin).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(suffix));
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            var css = reader.ReadToEnd();

            Assert.Contains("grid-template-columns:repeat(6,minmax(0,1fr))", css);
            Assert.Contains("max-width:1280px", css);
        }
    }
}
