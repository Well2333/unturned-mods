using System.IO;
using System.Linq;
using well404.Vault;
using Xunit;

namespace well404.Vault.Tests
{
    public class VaultPlayerUiTests
    {
        [Fact]
        public void VariantChildren_AreCollapsedIntoModalByDefault()
        {
            var assembly = typeof(VaultPlayerMenu).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(".player-ui.js"));
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            var script = reader.ReadToEnd();

            Assert.Contains("function openVariants(card)", script);
            Assert.Contains("for(const c of visible)grid.append(item(c))", script);
            Assert.Contains("for(const child of card.children||[])variants.append(item(child,false))", script);
            Assert.DoesNotContain("for(const child of c.children||[])grid.append", script);
            Assert.Contains("pressedOutside&&event.target===overlay", script);
            Assert.Contains("aria-modal", script);
        }

        [Fact]
        public void PlayerCards_UseCompactSixColumnDesktopGrid()
        {
            var assembly = typeof(VaultPlayerMenu).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(".player-ui.css"));
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            var css = reader.ReadToEnd();

            Assert.Contains("grid-template-columns:repeat(6,minmax(0,1fr))", css);
            Assert.Contains("min-height:116px", css);
            Assert.Contains("@media(max-width:520px)", css);
        }
    }
}
