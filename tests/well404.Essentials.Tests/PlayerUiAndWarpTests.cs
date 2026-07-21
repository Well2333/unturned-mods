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
            var script = Resource(".player-map-ui.js");
            Assert.Contains("players-party", script);
            Assert.Contains("subsection(parent, zh ? \"在线玩家\"", script);
            Assert.Contains("subsection(parent, zh ? \"队伍\"", script);
            Assert.True(script.IndexOf("在线玩家", System.StringComparison.Ordinal)
                < script.LastIndexOf("队伍", System.StringComparison.Ordinal));
        }

        [Fact]
        public void PlayerUi_FiltersWarpsByServerMetadata()
        {
            var script = Resource(".player-map-ui.js");
            Assert.Contains("warpTags", script);
            Assert.Contains("warps.flatMap(warpTags)", script);
            Assert.Contains("warpTags(card).includes(selected)", script);
            Assert.Contains("well404.essentials.player.warp-filter", script);
            Assert.Contains("tags.includes(value)", script);
            Assert.Contains("快捷操作", script);
            Assert.Contains("warp-section", script);
            Assert.Contains("aria-pressed", script);
        }

        [Fact]
        public void AdminUi_UsesPlayerStyleWarpCatalogAndCrud()
        {
            var css = Resource(".admin-ui.css");
            var script = Resource(".admin-map-ui.js");
            var html = Resource(".admin-ui.html");
            Assert.Contains("Player-panel aligned admin navigation", css);
            Assert.Contains("id=\"warp-tabs\"", html);
            Assert.Contains("id=\"warp-grid\"", html);
            Assert.Contains("panel.records(\"warps\")", script);
            Assert.Contains("panel.invoke(\"warps\"", script);
            Assert.Contains("splitTags", script);
            Assert.Contains("tagsOf(record).includes", script);
            Assert.Contains("tag:warpState.active", script);
            Assert.Contains("aria-pressed", script);
            Assert.Contains("warpState.records.filter", script);
            Assert.DoesNotContain("cooldownSeconds:cooldown.value", script);
            Assert.Contains("/reorder", script);
            Assert.Contains("/delete", script);
            Assert.Contains("pressedOutside && event.target === overlay", script);
        }

        [Fact]
        public void PlayerUi_ProvidesInteractiveMapAndListFallback()
        {
            var script = Resource(".player-map-ui.js");
            var css = Resource(".player-ui.css");

            Assert.Contains("panel.assetUrl", script);
            Assert.Contains("kind + \"Available\"", script);
            Assert.Contains("kind + \"AssetId\"", script);
            Assert.Contains("gpsButton", script);
            Assert.Contains("chartButton", script);
            Assert.Contains("mapX", script);
            Assert.Contains("mapY", script);
            Assert.Contains("installPanZoom", script);
            Assert.Contains("well404.essentials.player.warp-view", script);
            Assert.Contains("well404.essentials.player.warp-viewport", script);
            Assert.Contains("sessionStorage.setItem(viewportKey", script);
            Assert.Contains("stage.append(canvas, markers, controlsNode)", script);
            Assert.DoesNotContain("quickNode", script);
            Assert.DoesNotContain("canvas.append(image, markers)", script);
            Assert.Contains("panel.setAutoRefreshPaused?.(mapMode)", script);
            Assert.Contains("clearActiveMap()", script);
            Assert.Contains("observer?.disconnect()", script);
            Assert.Contains("window.removeEventListener(\"resize\", resize)", script);
            Assert.Contains("extensionLifetime?.observe", script);
            var lifetimeStart = script.IndexOf("new MutationObserver", System.StringComparison.Ordinal);
            var lifetimeEnd = script.IndexOf("extensionLifetime.disconnect()", lifetimeStart, System.StringComparison.Ordinal);
            Assert.True(lifetimeStart >= 0 && lifetimeEnd > lifetimeStart);
            Assert.DoesNotContain("setAutoRefreshPaused?.(false)",
                script.Substring(lifetimeStart, lifetimeEnd - lifetimeStart));
            Assert.Contains("quickSection.hidden = mapMode", script);
            Assert.Contains("panel.invoke(card, action, marker)", script);
            Assert.Contains("window.visualViewport?.height", script);
            Assert.Contains("viewportHeight * 0.72", script);
            Assert.Contains("compact: 1040", script);
            Assert.Contains("Math.min(shellWidth, fitWidth, sizeLimits.compact)", script);
            Assert.DoesNotContain("mapsize-auto", script);
            Assert.Contains("sizeMode === \"large\"", script);
            Assert.Contains("normalizedMapCoordinate", script);
            Assert.Contains("coordinate >= 0 && coordinate <= 1", script);
            Assert.Contains("mapMarkerKind", script);
            Assert.Contains("map-marker-location", css);
            Assert.DoesNotContain("map-quick-actions", css);
            Assert.Contains("width:28px", css);
            Assert.Contains("quick-actions", css);
            Assert.Contains("flex:0 0 auto", css);
        }

        [Fact]
        public void AdminUi_ProvidesConfigBackedTagLibraryAndMultiSelect()
        {
            var script = Resource(".admin-map-ui.js");
            var css = Resource(".admin-ui.css");
            var html = Resource(".admin-ui.html");

            Assert.Contains("manage-tags", html);
            Assert.Contains("warp-tags", script);
            Assert.Contains("createTagPicker", script);
            Assert.Contains("tag-choice", script);
            Assert.Contains("customTagIds", script);
            Assert.Contains("openTagManager", script);
            Assert.Contains("nameEn", script);
            Assert.Contains("nameZh", script);
            Assert.Contains("tagEmoji(tagsOf(record))", script);
            Assert.Contains("Config-backed warp tag library", css);
        }

        [Fact]
        public void AdminUi_ShowsCurrentMapAndPreservesMapWhenEditing()
        {
            var script = Resource(".admin-map-ui.js");

            Assert.Contains("panel.values(\"warp-map-info\")", script);
            Assert.Contains("panel.assetUrl", script);
            Assert.Contains("values.mapX", script);
            Assert.Contains("openWarpModal(record)", script);
            Assert.Contains("map:map.value", script);
            Assert.Contains("gpsButton", script);
            Assert.Contains("chartButton", script);
            Assert.Contains("well404.essentials.admin.warp-view", script);
            Assert.Contains("well404.essentials.admin.warp-viewport", script);
            Assert.Contains("stage.append(canvas, markers, controlsNode)", script);
            Assert.DoesNotContain("canvas.append(image, markers)", script);
            Assert.Contains("panel.setAutoRefreshPaused?.(active === \"warps\" && spatialView)", script);
            Assert.Contains("panel.onDispose?.", script);
            Assert.Contains("disposeMap = installPanZoom", script);
            Assert.Contains("observer?.disconnect()", script);
        }
    }
}
