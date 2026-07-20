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
        public void PlayerPluginUi_CanPauseOnlyAutomaticRefreshAndNavigationReleasesIt()
        {
            var html = ReadHtml(".player.html");

            Assert.Contains("let autoRefreshPaused = false;", html);
            Assert.Contains("autoRefreshPaused ||", html);
            Assert.Contains("setAutoRefreshPaused:paused => { autoRefreshPaused = !!paused; }", html);
            Assert.Contains("function renderView(menu, view)", html);
            Assert.Contains("autoRefreshPaused = false;", html);
            Assert.Contains("refreshBtn", html);
        }

        [Fact]
        public void AdminPluginApiHelper_IsScopedToCurrentModule()
        {
            var html = ReadHtml(".index.html");

            Assert.Contains("const modulePrefix = `api/modules/${enc(module.id)}/`", html);
            Assert.Contains("api:scopedApi", html);
            Assert.Contains("candidate.includes(\"..\")", html);
        }

        [Fact]
        public void PlayerMutation_NetworkFailureStillRequestsAuthoritativeView()
        {
            var html = ReadHtml(".player.html");
            var catchStart = html.IndexOf("} catch (e) {", html.IndexOf("/api/p/invoke/"));
            var catchEnd = html.IndexOf("return;", catchStart);

            Assert.True(catchStart >= 0 && catchEnd > catchStart);
            var catchBody = html.Substring(catchStart, catchEnd - catchStart);
            Assert.Contains("actionInFlight--", catchBody);
            Assert.Contains("if (actionInFlight === 0) load(true);", catchBody);
            Assert.Contains("if (loading || actionInFlight > 0) { refreshPending = true; return; }", html);
            Assert.Contains("queueMicrotask(() => load(true));", html);
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


        [Fact]
        public void PluginUiCapabilities_ExposeOnlyScopedAuthenticatedAssetUrls()
        {
            var admin = ReadHtml(".index.html");
            var player = ReadHtml(".player.html");

            Assert.Contains(
                "assetUrl:assetId => `api/modules/${enc(module.id)}/asset/${enc(assetId)}`",
                admin);
            Assert.Contains(
                "assetUrl:(assetId) => \"/api/p/asset/\" + enc(menu.id) + \"/\" + enc(assetId) + \"?t=\" + enc(TOKEN)",
                player);
            Assert.DoesNotContain("assetUrl:(path)", admin);
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
