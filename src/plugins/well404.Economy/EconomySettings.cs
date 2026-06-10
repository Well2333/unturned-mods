using System.Collections.Generic;

namespace well404.Economy
{
    /// <summary>
    /// Strongly-typed view of <c>config.yaml</c>. Bound from the plugin's
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> on demand.
    /// </summary>
    public class EconomySettings
    {
        public CurrencySettings Currency { get; set; } = new CurrencySettings();

        /// <summary>The active currency backend: <c>database</c> or <c>experience</c>.</summary>
        public string Backend { get; set; } = "database";

        public DatabaseSettings Database { get; set; } = new DatabaseSettings();

        public TransferSettings Transfer { get; set; } = new TransferSettings();

        public KillRewardSettings KillRewards { get; set; } = new KillRewardSettings();
    }

    public class CurrencySettings
    {
        public string Name { get; set; } = "Credit";
        public string Symbol { get; set; } = "$";

        /// <summary>Balance assumed for accounts that have never been touched (database backend only).</summary>
        public decimal StartingBalance { get; set; } = 0m;
    }

    public class DatabaseSettings
    {
        /// <summary>LiteDB file name, relative to the plugin working directory.</summary>
        public string FileName { get; set; } = "economy.db";
    }

    public class TransferSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Minimum amount allowed in a single <c>/pay</c>.</summary>
        public decimal MinAmount { get; set; } = 1m;

        /// <summary>Percentage (0-100) taken from the transferred amount as tax.</summary>
        public decimal TaxPercent { get; set; } = 0m;
    }

    public class KillRewardSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Reward for killing another player. 0 disables.</summary>
        public decimal Player { get; set; } = 0m;

        /// <summary>Reward for killing a normal zombie. 0 disables.</summary>
        public decimal Zombie { get; set; } = 0m;

        /// <summary>Reward for killing a mega/boss zombie. 0 disables.</summary>
        public decimal MegaZombie { get; set; } = 0m;

        /// <summary>Reward for killing an animal. 0 disables.</summary>
        public decimal Animal { get; set; } = 0m;
    }
}
