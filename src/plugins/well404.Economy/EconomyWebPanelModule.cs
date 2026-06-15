using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using UnturnedMods.Shared.WebPanel;
using well404.Economy.Commands;

namespace well404.Economy
{
    /// <summary>
    /// Builds the economy's <see cref="WebPanelModule"/>: list everyone's balance, set a
    /// balance, and add/subtract a balance. Handlers run on a web server thread, so each
    /// switches to the main thread before touching the user manager / Unturned APIs.
    /// </summary>
    internal static class EconomyWebPanelModule
    {
        public const string ModuleId = "well404.economy";

        public static WebPanelModule Create(EconomyProvider economy, IUserManager userManager, EconomyConfigStore configStore)
        {
            var balances = new WebPanelAction(
                id: "balances",
                label: "All balances",
                kind: WebActionKind.Collection,
                handler: request => SetBalanceAsync(economy, userManager, request),
                fields: new[]
                {
                    new WebField("player", "Player", WebFieldType.Text, required: true, placeholder: "Name or SteamID (17 digits)"),
                    new WebField("amount", "Balance", WebFieldType.Number, required: true)
                },
                description: "The database backend lists every account; the experience backend lists online players only. Click a row to edit its balance; Add sets a player's balance.",
                recordsLoader: () => LoadBalanceRecordsAsync(economy, userManager),
                deleteHandler: request => DeleteBalanceAsync(economy, request),
                keyField: "player",
                layout: "list");

            var currency = new WebPanelAction(
                id: "currency",
                label: "Currency & backend",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveCurrency(configStore, request)),
                fields: new[]
                {
                    new WebField("name", "Currency name", WebFieldType.Text),
                    new WebField("symbol", "Symbol", WebFieldType.Text),
                    new WebField("startingBalance", "Starting balance", WebFieldType.Number),
                    new WebField("backend", "Backend", WebFieldType.Select, options: new[] { "database", "experience" })
                },
                description: "Change the currency display and storage backend.",
                loader: () => Load(configStore, s => new Dictionary<string, string>
                {
                    ["name"] = s.Currency.Name,
                    ["symbol"] = s.Currency.Symbol,
                    ["startingBalance"] = Num(s.Currency.StartingBalance),
                    ["backend"] = s.Backend
                }));

            var killRewards = new WebPanelAction(
                id: "killrewards",
                label: "Kill rewards (income)",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveKillRewards(configStore, request)),
                fields: new[]
                {
                    new WebField("enabled", "Master switch", WebFieldType.Boolean),
                    new WebField("player", "Kill a player", WebFieldType.Number, placeholder: "0 = disabled"),
                    new WebField("zombie", "Kill a zombie", WebFieldType.Number, placeholder: "0 = disabled"),
                    new WebField("megaZombie", "Kill a mega zombie", WebFieldType.Number, placeholder: "0 = disabled"),
                    new WebField("animal", "Kill an animal", WebFieldType.Number, placeholder: "0 = disabled")
                },
                description: "How much currency each kill source grants. 0 disables that source.",
                loader: () => Load(configStore, s => new Dictionary<string, string>
                {
                    ["enabled"] = Bool(s.KillRewards.Enabled),
                    ["player"] = Num(s.KillRewards.Player),
                    ["zombie"] = Num(s.KillRewards.Zombie),
                    ["megaZombie"] = Num(s.KillRewards.MegaZombie),
                    ["animal"] = Num(s.KillRewards.Animal)
                }));

            var transfer = new WebPanelAction(
                id: "transfer",
                label: "Transfers",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveTransfer(configStore, request)),
                fields: new[]
                {
                    new WebField("enabled", "Master switch", WebFieldType.Boolean),
                    new WebField("minAmount", "Min transfer", WebFieldType.Number),
                    new WebField("taxPercent", "Tax (%)", WebFieldType.Number, placeholder: "0-100")
                },
                description: "The /pay toggle, minimum amount and tax percentage.",
                loader: () => Load(configStore, s => new Dictionary<string, string>
                {
                    ["enabled"] = Bool(s.Transfer.Enabled),
                    ["minAmount"] = Num(s.Transfer.MinAmount),
                    ["taxPercent"] = Num(s.Transfer.TaxPercent)
                }));

            return new WebPanelModule(
                ModuleId, "Economy / Balances",
                new[] { balances, currency, killRewards, transfer },
                icon: "💰");
        }

        /// <summary>Reads current settings under the store lock into a values map for form pre-fill.</summary>
        private static Task<IReadOnlyDictionary<string, string>> Load(
            EconomyConfigStore store, Func<EconomySettings, Dictionary<string, string>> select)
            => Task.FromResult(store.Read(s => (IReadOnlyDictionary<string, string>)select(s)));

        private static WebActionResult SaveCurrency(EconomyConfigStore store, WebActionRequest request)
        {
            var name = request.Get("name");
            var symbol = request.Get("symbol");
            var startingBalance = request.GetDecimal("startingBalance");
            var backend = request.Get("backend");

            store.Update(s =>
            {
                if (name != null) s.Currency.Name = name;
                if (symbol != null) s.Currency.Symbol = symbol;
                if (startingBalance != null) s.Currency.StartingBalance = startingBalance.Value;
                if (backend == "database" || backend == "experience") s.Backend = backend;
            });

            return WebActionResult.Ok("Saved currency & backend settings.");
        }

        private static WebActionResult SaveKillRewards(EconomyConfigStore store, WebActionRequest request)
        {
            var enabled = ParseToggle(request.Get("enabled"));
            var player = request.GetDecimal("player");
            var zombie = request.GetDecimal("zombie");
            var megaZombie = request.GetDecimal("megaZombie");
            var animal = request.GetDecimal("animal");

            store.Update(s =>
            {
                if (enabled != null) s.KillRewards.Enabled = enabled.Value;
                if (player != null) s.KillRewards.Player = player.Value;
                if (zombie != null) s.KillRewards.Zombie = zombie.Value;
                if (megaZombie != null) s.KillRewards.MegaZombie = megaZombie.Value;
                if (animal != null) s.KillRewards.Animal = animal.Value;
            });

            return WebActionResult.Ok("Saved kill-reward settings.");
        }

        private static WebActionResult SaveTransfer(EconomyConfigStore store, WebActionRequest request)
        {
            var enabled = ParseToggle(request.Get("enabled"));
            var minAmount = request.GetDecimal("minAmount");
            var taxPercent = request.GetDecimal("taxPercent");

            store.Update(s =>
            {
                if (enabled != null) s.Transfer.Enabled = enabled.Value;
                if (minAmount != null) s.Transfer.MinAmount = minAmount.Value;
                if (taxPercent != null) s.Transfer.TaxPercent = taxPercent.Value;
            });

            return WebActionResult.Ok("Saved transfer settings.");
        }

        /// <summary>Maps a boolean-field value to true / false / keep(null) when absent.</summary>
        private static bool? ParseToggle(string? value)
        {
            if (value == "true") return true;
            if (value == "false") return false;
            return null;
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static async Task<IReadOnlyList<WebRecord>> LoadBalanceRecordsAsync(EconomyProvider economy, IUserManager userManager)
        {
            await UniTask.SwitchToMainThread();
            var accounts = await economy.ListAccountsAsync();

            // Resolve display names for currently online players.
            var online = await userManager.GetUsersAsync(KnownActorTypes.Player);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var user in online)
            {
                names[user.Id] = user.DisplayName;
            }

            var symbol = economy.CurrencySymbol;
            var records = new List<WebRecord>();
            foreach (var account in accounts.OrderByDescending(a => a.Balance))
            {
                names.TryGetValue(account.OwnerId, out var name);
                var balance = account.Balance.ToString(CultureInfo.InvariantCulture);
                var display = string.IsNullOrEmpty(name) ? account.OwnerId : name;
                records.Add(new WebRecord(
                    account.OwnerId,
                    display,
                    new Dictionary<string, string>
                    {
                        ["player"] = account.OwnerId,
                        ["amount"] = balance
                    },
                    new[] { symbol + balance }));
            }

            return records;
        }

        private static async Task<WebActionResult> SetBalanceAsync(
            EconomyProvider economy, IUserManager userManager, WebActionRequest request)
        {
            await UniTask.SwitchToMainThread();
            var search = request.Get("player");
            var amount = request.GetDecimal("amount");
            if (search == null || amount == null)
            {
                return WebActionResult.Fail("Enter a player and a balance.");
            }

            var target = await PlayerResolver.ResolveAsync(userManager, search);
            if (target == null)
            {
                return WebActionResult.Fail($"Player not found: {search}");
            }

            await economy.SetBalanceAsync(target.Id, KnownActorTypes.Player, amount.Value);
            return WebActionResult.Ok(
                $"Set {target.DisplayName}'s balance to {economy.CurrencySymbol}{amount.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        private static async Task<WebActionResult> DeleteBalanceAsync(EconomyProvider economy, WebActionRequest request)
        {
            var key = request.Get("key");
            if (key == null)
            {
                return WebActionResult.Fail("Missing account ID.");
            }

            await UniTask.SwitchToMainThread();
            await economy.DeleteAccountAsync(key, KnownActorTypes.Player);
            return WebActionResult.Ok($"Deleted account {key}.");
        }
    }
}
