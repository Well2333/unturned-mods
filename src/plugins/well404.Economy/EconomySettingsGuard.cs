using System;

namespace well404.Economy
{
    internal static class EconomySettingsGuard
    {
        public static void Validate(EconomySettings settings)
        {
            if (settings.Currency == null || settings.Transfer == null || settings.KillRewards == null)
                throw new InvalidOperationException("Economy configuration sections cannot be null.");
            if (!string.Equals(settings.Backend, "database", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(settings.Backend, "experience", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("backend must be database or experience.");
            if (settings.Currency.StartingBalance < 0m)
                throw new InvalidOperationException("currency.startingBalance cannot be negative.");
            if (settings.Transfer.MinAmount < 0m)
                throw new InvalidOperationException("transfer.minAmount cannot be negative.");
            if (settings.Transfer.TaxPercent < 0m || settings.Transfer.TaxPercent > 100m)
                throw new InvalidOperationException("transfer.taxPercent must be between 0 and 100.");
            if (settings.KillRewards.Player < 0m
                || settings.KillRewards.Zombie < 0m
                || settings.KillRewards.MegaZombie < 0m
                || settings.KillRewards.Animal < 0m)
                throw new InvalidOperationException("Kill rewards cannot be negative.");

            if (string.Equals(settings.Backend, "experience", StringComparison.OrdinalIgnoreCase)
                && (decimal.Truncate(settings.Currency.StartingBalance) != settings.Currency.StartingBalance
                    || decimal.Truncate(settings.Transfer.MinAmount) != settings.Transfer.MinAmount
                    || decimal.Truncate(settings.KillRewards.Player) != settings.KillRewards.Player
                    || decimal.Truncate(settings.KillRewards.Zombie) != settings.KillRewards.Zombie
                    || decimal.Truncate(settings.KillRewards.MegaZombie) != settings.KillRewards.MegaZombie
                    || decimal.Truncate(settings.KillRewards.Animal) != settings.KillRewards.Animal))
                throw new InvalidOperationException("The experience backend only supports whole-number amounts.");
        }

        public static EconomySettings Clone(EconomySettings source)
            => new EconomySettings
            {
                Backend = source.Backend,
                Currency = new CurrencySettings
                {
                    Name = source.Currency.Name,
                    Symbol = source.Currency.Symbol,
                    StartingBalance = source.Currency.StartingBalance
                },
                Transfer = new TransferSettings
                {
                    Enabled = source.Transfer.Enabled,
                    MinAmount = source.Transfer.MinAmount,
                    TaxPercent = source.Transfer.TaxPercent
                },
                KillRewards = new KillRewardSettings
                {
                    Enabled = source.KillRewards.Enabled,
                    Player = source.KillRewards.Player,
                    Zombie = source.KillRewards.Zombie,
                    MegaZombie = source.KillRewards.MegaZombie,
                    Animal = source.KillRewards.Animal
                }
            };
    }
}
