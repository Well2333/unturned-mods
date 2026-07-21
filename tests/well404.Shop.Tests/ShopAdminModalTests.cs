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
            Assert.Contains("const localizedName=text=>", script);
            Assert.Contains("name-secondary", script);
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
            Assert.Contains(".name-secondary", css);
        }

        [Fact]
        public void QuarantineUi_RequiresExplicitResolutionEvidence()
        {
            var assembly = typeof(ShopPlugin).Assembly;
            string Resource(string suffix)
            {
                var name = assembly.GetManifestResourceNames().Single(value => value.EndsWith(suffix));
                using var stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream!);
                return reader.ReadToEnd();
            }

            var html = Resource(".admin-ui.html");
            var script = Resource(".admin-ui.js");
            Assert.Equal(1, Count(html, "data-tab=\"quarantine\""));
            Assert.Equal(1, Count(html, "data-panel=\"quarantine\""));
            Assert.Equal(1, Count(html, "id=\"quarantine-list\""));
            Assert.Contains("panel.records(\"quarantine\")", script);
            Assert.Contains("retry-refund", script);
            Assert.Contains("retry-credit", script);
            Assert.Contains("confirmation:confirmation.value", script);
            Assert.Contains("note:note.value", script);
            Assert.Contains("Nothing is refunded, credited, or replayed automatically", html);
        }
        private static int Count(string text, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = text.IndexOf(value, offset, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }
    }
}
