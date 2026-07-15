using System.IO;
using System.Linq;
using well404.WebPanel;
using Xunit;

namespace well404.WebPanel.Tests
{
    public class PopupFieldSubmissionTests
    {
        [Fact]
        public void AdminRowAction_CopiesEveryUrlSearchParamIntoRequest()
        {
            var html = ReadHtml(".index.html");

            Assert.Contains(
                "for (const [key, value] of extra.entries()) body.set(key, value);",
                html);
            Assert.DoesNotContain("for (const k in extra)", html);
        }

        [Theory]
        [InlineData(".index.html")]
        [InlineData(".player.html")]
        public void ModalBackdrop_RequiresPressAndReleaseOutside(string suffix)
        {
            var html = ReadHtml(suffix);

            Assert.Contains("pressedOutside = e.target === overlay", html);
            Assert.Contains("pressedOutside && e.target === overlay", html);
            Assert.DoesNotContain("overlay.addEventListener(\"click\"", html);
        }

        [Theory]
        [InlineData(".index.html")]
        [InlineData(".player.html")]
        public void AutoRefresh_PausesForPluginOwnedShadowDomModals(string suffix)
        {
            var html = ReadHtml(suffix);

            Assert.Contains("function pluginUiModalOpen()", html);
            Assert.Contains("pluginUiModalOpen()", html);
            Assert.Contains("host.shadowRoot?.querySelector('[aria-modal=\"true\"]')", html);
        }

        [Fact]
        public void AdminPluginApiHelper_IsScopedToCurrentModule()
        {
            var html = ReadHtml(".index.html");

            Assert.Contains("const modulePrefix = `api/modules/${enc(module.id)}/`", html);
            Assert.Contains("api:scopedApi", html);
            Assert.Contains("candidate.includes(\"..\")", html);
        }

        [Theory]
        [InlineData(".index.html")]
        [InlineData(".player.html")]
        public void GenericRenderer_SupportsLocalizedPrimaryAndSecondaryNames(string suffix)
        {
            var html = ReadHtml(suffix);

            Assert.Contains("function localizedText(value, className=\"\")", html);
            Assert.Contains("localized-secondary", html);
            Assert.Contains("lines[lines.length - 1]", html);
        }

        private static string ReadHtml(string suffix)
        {
            var assembly = typeof(WebPanelPlugin).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(suffix));
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }
    }
}
