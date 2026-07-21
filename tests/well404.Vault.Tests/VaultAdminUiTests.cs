using System.IO;
using System.Linq;
using Xunit;

namespace well404.Vault.Tests
{
    public class VaultAdminUiTests
    {
        private static string Resource(string suffix)
        {
            var assembly = typeof(VaultWebPanelModule).Assembly;
            var name = assembly.GetManifestResourceNames().Single(value => value.EndsWith(suffix));
            using var stream = assembly.GetManifestResourceStream(name);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            return reader.ReadToEnd();
        }

        [Fact]
        public void AdminUi_ProvidesScopedEditorAndConfirmedDeletes()
        {
            var script = Resource(".admin-ui.js");
            Assert.Contains("panel.records(\"owners\")", script);
            Assert.Contains("panel.invoke(\"inventory\"", script);
            Assert.Contains("panel.invoke(\"containerinfo\"", script);
            Assert.Contains("panel.invoke(\"setcapacity\"", script);
            Assert.Contains("panel.invoke(\"resolvepurchase\"", script);
            Assert.Contains("panel.records(\"purchaseresolutions\"", script);
            Assert.Contains("confirmation!==operationId", script);
            Assert.Contains("capacity>1000000", script);
            Assert.Contains("listGeneration", script);
            Assert.Contains("panel.invoke(\"additem\"", script);
            Assert.Contains("panel.invoke(\"updateitem\"", script);
            Assert.Contains("panel.invoke(\"deleteitem\"", script);
            Assert.Contains("panel.invoke(\"deleteitems\"", script);
            Assert.Contains("confirm(L.deleteConfirm.replace", script);
            Assert.Contains("confirm(L.bulkConfirm", script);
            Assert.Contains("pressedOutside&&event.target===overlay", script);
        }

        [Fact]
        public void AdminUi_UsesCompactResponsiveItemGrid()
        {
            var css = Resource(".admin-ui.css");
            Assert.Contains(".inventory-grid", css);
            Assert.Contains("grid-template-columns:repeat(6,minmax(0,1fr))", css);
            Assert.Contains(".name-secondary", css);
            Assert.Contains(".owner-list", css);
            Assert.Contains(".owner-card", css);
            Assert.Contains("Final player-panel aligned overrides", css);
            Assert.DoesNotContain(".inventory-grid{grid-template-columns:repeat(auto-fill", css);
        }
    }
}
