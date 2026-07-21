using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using well404.Economy;
using Xunit;

namespace well404.Economy.Tests
{
    public sealed class EconomyConfigStoreTests : IDisposable
    {
        private readonly string m_Directory =
            Path.Combine(Path.GetTempPath(), "economy-config-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void InvalidUpdate_DoesNotPoisonInMemorySettings()
        {
            Directory.CreateDirectory(m_Directory);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["backend"] = "database",
                    ["currency:startingBalance"] = "0",
                    ["transfer:enabled"] = "true",
                    ["transfer:minAmount"] = "1",
                    ["transfer:taxPercent"] = "0"
                })
                .Build();
            var store = new EconomyConfigStore(configuration, m_Directory);

            Assert.Throws<InvalidOperationException>(() =>
                store.Update(settings => settings.Transfer.TaxPercent = 101m));

            Assert.Equal(0m, store.Read(settings => settings.Transfer.TaxPercent));
            Assert.False(File.Exists(Path.Combine(m_Directory, "config.yaml")));
        }

        public void Dispose()
        {
            if (Directory.Exists(m_Directory))
                Directory.Delete(m_Directory, recursive: true);
        }
    }
}
