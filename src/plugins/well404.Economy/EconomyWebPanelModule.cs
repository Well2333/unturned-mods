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
                label: "所有余额",
                kind: WebActionKind.Collection,
                handler: request => SetBalanceAsync(economy, userManager, request),
                fields: new[]
                {
                    new WebField("player", "玩家", WebFieldType.Text, required: true, placeholder: "名字或 SteamID(17位)"),
                    new WebField("amount", "余额", WebFieldType.Number, required: true)
                },
                description: "数据库后端列全部账户，经验后端仅在线玩家。点条目编辑其余额，「新增」设置某玩家余额。",
                recordsLoader: () => LoadBalanceRecordsAsync(economy, userManager),
                deleteHandler: request => DeleteBalanceAsync(economy, request),
                keyField: "player",
                layout: "list");

            var currency = new WebPanelAction(
                id: "currency",
                label: "货币与后端",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveCurrency(configStore, request)),
                fields: new[]
                {
                    new WebField("name", "货币名", WebFieldType.Text),
                    new WebField("symbol", "符号", WebFieldType.Text),
                    new WebField("startingBalance", "初始余额", WebFieldType.Number),
                    new WebField("backend", "后端", WebFieldType.Select, options: new[] { "database", "experience" })
                },
                description: "修改货币显示与存储后端。",
                loader: () => Load(configStore, s => new Dictionary<string, string>
                {
                    ["name"] = s.Currency.Name,
                    ["symbol"] = s.Currency.Symbol,
                    ["startingBalance"] = Num(s.Currency.StartingBalance),
                    ["backend"] = s.Backend
                }));

            var killRewards = new WebPanelAction(
                id: "killrewards",
                label: "击杀奖励（经济来源）",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveKillRewards(configStore, request)),
                fields: new[]
                {
                    new WebField("enabled", "总开关", WebFieldType.Select, options: new[] { "开", "关" }),
                    new WebField("player", "击杀玩家", WebFieldType.Number, placeholder: "0=禁用"),
                    new WebField("zombie", "击杀僵尸", WebFieldType.Number, placeholder: "0=禁用"),
                    new WebField("megaZombie", "击杀Boss僵尸", WebFieldType.Number, placeholder: "0=禁用"),
                    new WebField("animal", "击杀动物", WebFieldType.Number, placeholder: "0=禁用")
                },
                description: "击杀获得货币的各来源金额。设为 0 禁用该来源。",
                loader: () => Load(configStore, s => new Dictionary<string, string>
                {
                    ["enabled"] = OnOff(s.KillRewards.Enabled),
                    ["player"] = Num(s.KillRewards.Player),
                    ["zombie"] = Num(s.KillRewards.Zombie),
                    ["megaZombie"] = Num(s.KillRewards.MegaZombie),
                    ["animal"] = Num(s.KillRewards.Animal)
                }));

            var transfer = new WebPanelAction(
                id: "transfer",
                label: "转账设置",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveTransfer(configStore, request)),
                fields: new[]
                {
                    new WebField("enabled", "总开关", WebFieldType.Select, options: new[] { "开", "关" }),
                    new WebField("minAmount", "最小转账额", WebFieldType.Number),
                    new WebField("taxPercent", "税率(%)", WebFieldType.Number, placeholder: "0-100")
                },
                description: "玩家 /pay 转账的开关、最小额与税率比例。",
                loader: () => Load(configStore, s => new Dictionary<string, string>
                {
                    ["enabled"] = OnOff(s.Transfer.Enabled),
                    ["minAmount"] = Num(s.Transfer.MinAmount),
                    ["taxPercent"] = Num(s.Transfer.TaxPercent)
                }));

            return new WebPanelModule(
                ModuleId, "经济 / 余额",
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

            return WebActionResult.Ok("已保存货币与后端设置。");
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

            return WebActionResult.Ok("已保存击杀奖励（经济来源）设置。");
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

            return WebActionResult.Ok("已保存转账设置。");
        }

        /// <summary>Maps a 不变/开/关 select value to keep(null) / true / false.</summary>
        private static bool? ParseToggle(string? value)
        {
            if (value == "开") return true;
            if (value == "关") return false;
            return null;
        }

        private static string OnOff(bool value) => value ? "开" : "关";

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
                return WebActionResult.Fail("请填写玩家与余额。");
            }

            var target = await PlayerResolver.ResolveAsync(userManager, search);
            if (target == null)
            {
                return WebActionResult.Fail($"找不到玩家：{search}");
            }

            await economy.SetBalanceAsync(target.Id, KnownActorTypes.Player, amount.Value);
            return WebActionResult.Ok(
                $"已将 {target.DisplayName} 的余额设为 {economy.CurrencySymbol}{amount.Value.ToString(CultureInfo.InvariantCulture)}。");
        }

        private static async Task<WebActionResult> DeleteBalanceAsync(EconomyProvider economy, WebActionRequest request)
        {
            var key = request.Get("key");
            if (key == null)
            {
                return WebActionResult.Fail("缺少账户 ID。");
            }

            await UniTask.SwitchToMainThread();
            await economy.DeleteAccountAsync(key, KnownActorTypes.Player);
            return WebActionResult.Ok($"已删除账户 {key}。");
        }
    }
}
