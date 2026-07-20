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
            Assert.Contains("for(const card of visible)grid.append(item(card))", script);
            Assert.Contains("for(const child of card.children||[])variants.append(item(child,false))", script);
            Assert.DoesNotContain("for(const child of c.children||[])grid.append", script);
            Assert.Contains("pressedOutside&&event.target===overlay", script);
            Assert.Contains("aria-modal", script);
            Assert.Contains("const localizedName=text=>", script);
            Assert.Contains("name-secondary", script);
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
            Assert.Contains(".name-secondary", css);
            Assert.Contains(".item.rarity-common", css);
            Assert.Contains(".item.rarity-mythical", css);
            Assert.Contains(".rarity-label::before", css);
            Assert.Contains(".type-filter-row", css);
        }

        [Fact]
        public void PlayerUi_ProvidesPersistentSortingAndHierarchicalNativeTypeFilters()
        {
            var assembly = typeof(VaultPlayerMenu).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .Single(name => name.EndsWith(".player-ui.js"));
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            var script = reader.ReadToEnd();

            Assert.Contains("sortMode=getStored(storage.sort,\"slots\")", script);
            Assert.Contains("[\"slots\",copy.slots]", script);
            Assert.Contains("[\"id\",copy.id]", script);
            Assert.Contains("[\"count\",copy.count]", script);
            Assert.Contains("[\"rarity\",copy.rarity]", script);
            Assert.Contains("const nativeItemTypes=", script);
            Assert.Contains("typeFilter=getStored(storage.typeFilter,\"all\")", script);
            Assert.Contains("categoryGroup=filter===\"all\"?sourceGroup:sourceGroup.filter(card=>category(card)===filter)", script);
            Assert.Contains("typeFilter===\"all\"||itemType(card)===typeFilter", script);
            Assert.Contains("sessionStorage.setItem", script);
            Assert.Contains("canMoveForScope", script);
            Assert.Contains("!hasBothScopes||!canMoveForScope(activeScope)", script);
            Assert.Contains("setStored(storage.filter,filter)", script);
            Assert.Contains("setStored(storage.typeFilter,typeFilter)", script);
            Assert.Contains("rarity-\"+itemRarity", script);
            Assert.Contains("hiddenContainers", script);
            Assert.Contains("inventoryContainer(card)", script);
            Assert.Contains("sourceGroup", script);
            Assert.Contains("well404.vault.player.hidden-containers", script);
            Assert.Contains("well404.vault.player.type-filter", script);
        }

    }
}
