using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;

namespace well404.Vault.Commands
{
    [Command("vault")]
    [CommandAlias("storage")]
    [CommandDescription("Personal vault: store and withdraw items. Use store/take/list, or /menu vault for the web UI.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVault : Command
    {
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVault(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
            : base(serviceProvider)
        {
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            await PrintAsync(m_StringLocalizer["vault:help"]);
        }
    }

    [Command("store")]
    [CommandAlias("deposit")]
    [CommandParent(typeof(CommandVault))]
    [CommandSyntax("<itemId> [amount]")]
    [CommandDescription("Stores items (by their item id) from your backpack into the vault.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultStore : Command
    {
        private readonly VaultService m_Vault;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultStore(IServiceProvider serviceProvider, VaultService vault, IItemDirectory itemDirectory, IStringLocalizer stringLocalizer)
            : base(serviceProvider)
        {
            m_Vault = vault;
            m_ItemDirectory = itemDirectory;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            var (itemId, amount) = ParseArgs(Context, m_StringLocalizer);

            var result = await m_Vault.StoreAsync(user, itemId, amount);
            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);
            var name = VaultNames.NameOf(itemId, names);
            if (result.Stored == 0)
            {
                throw new UserFriendlyException(result.CapacityReached
                    ? m_StringLocalizer["store:full"]
                    : m_StringLocalizer["store:none", new { name }]);
            }

            await PrintAsync(m_StringLocalizer["store:done", new { amount = result.Stored, name }]);
            if (result.CapacityReached)
            {
                await PrintAsync(m_StringLocalizer["store:full"]);
            }
        }

        internal static (ushort itemId, int amount) ParseArgs(ICommandContext context, IStringLocalizer localizer)
        {
            if (context.Parameters.Length < 1)
            {
                throw new CommandWrongUsageException(context);
            }

            if (!ushort.TryParse(context.Parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId) || itemId == 0)
            {
                throw new UserFriendlyException(localizer["errors:invalid_item"]);
            }

            var amount = 1;
            if (context.Parameters.Length >= 2
                && (!int.TryParse(context.Parameters[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0))
            {
                throw new UserFriendlyException(localizer["errors:invalid_amount"]);
            }

            return (itemId, amount);
        }
    }

    [Command("take")]
    [CommandAlias("withdraw")]
    [CommandParent(typeof(CommandVault))]
    [CommandSyntax("<itemId> [amount]")]
    [CommandDescription("Withdraws items (by their item id) from the vault into your backpack.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultTake : Command
    {
        private readonly VaultService m_Vault;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultTake(IServiceProvider serviceProvider, VaultService vault, IItemDirectory itemDirectory, IStringLocalizer stringLocalizer)
            : base(serviceProvider)
        {
            m_Vault = vault;
            m_ItemDirectory = itemDirectory;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            var (itemId, amount) = CommandVaultStore.ParseArgs(Context, m_StringLocalizer);

            var taken = await m_Vault.TakeAsync(user, itemId, amount);
            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);
            var name = VaultNames.NameOf(itemId, names);
            if (taken == 0)
            {
                throw new UserFriendlyException(m_StringLocalizer["take:none", new { name }]);
            }

            await PrintAsync(m_StringLocalizer["take:done", new { amount = taken, name }]);
        }
    }

    [Command("list")]
    [CommandAlias("info")]
    [CommandParent(typeof(CommandVault))]
    [CommandDescription("Lists what is in your vault and how full it is.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultList : Command
    {
        private readonly VaultService m_Vault;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultList(IServiceProvider serviceProvider, VaultService vault, IItemDirectory itemDirectory, IStringLocalizer stringLocalizer)
            : base(serviceProvider)
        {
            m_Vault = vault;
            m_ItemDirectory = itemDirectory;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            var items = m_Vault.Get(user.Id);
            var max = await m_Vault.GetMaxSlotsAsync(user);
            await PrintAsync(m_StringLocalizer["list:header", new { used = m_Vault.UsedSlots(user.Id), max }]);
            if (items.Count == 0)
            {
                await PrintAsync(m_StringLocalizer["list:empty"]);
                return;
            }

            var names = await VaultNames.BuildMapAsync(m_ItemDirectory);

            // Group by item id for a compact list (id | name | count | slots).
            foreach (var group in System.Linq.Enumerable.GroupBy(items, x => x.ItemId))
            {
                var count = 0;
                var slots = 0;
                foreach (var entry in group)
                {
                    count++;
                    slots += entry.SlotCost;
                }

                await PrintAsync(m_StringLocalizer["list:line", new { id = group.Key, name = VaultNames.NameOf(group.Key, names), amount = count, slots }]);
            }
        }
    }

    [Command("upgrade")]
    [CommandAlias("buy")]
    [CommandParent(typeof(CommandVault))]
    [CommandDescription("Buys personal vault capacity using your own balance.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultUpgrade : Command
    {
        private readonly VaultService m_Vault;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultUpgrade(
            IServiceProvider serviceProvider, VaultService vault, IStringLocalizer stringLocalizer)
            : base(serviceProvider)
        {
            m_Vault = vault;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var result = await m_Vault.PurchasePersonalCapacityAsync((UnturnedUser)Context.Actor);
            if (!result.Success)
                throw new UserFriendlyException(PurchaseError(result));
            await PrintAsync(m_StringLocalizer["upgrade:done", new
            {
                slots = result.SlotsAdded,
                price = result.Price,
                max = result.NewCapacity
            }]);
        }

        private string PurchaseError(TeamVaultPurchaseResult result)
        {
            switch (result.Status)
            {
                case TeamVaultPurchaseStatus.Disabled:
                    return m_StringLocalizer["upgrade:disabled"];
                case TeamVaultPurchaseStatus.EconomyUnavailable:
                    return m_StringLocalizer["upgrade:economy_missing"];
                case TeamVaultPurchaseStatus.MaximumReached:
                    return m_StringLocalizer["upgrade:maximum"];
                case TeamVaultPurchaseStatus.InvalidConfiguration:
                    return m_StringLocalizer["upgrade:invalid_config"];
                default:
                    return result.Error ?? m_StringLocalizer["upgrade:failed"];
            }
        }
    }

    [Command("team")]
    [CommandAlias("party")]
    [CommandParent(typeof(CommandVault))]
    [CommandDescription("Shared party vault: store, take, list, or buy capacity.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultTeam : Command
    {
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultTeam(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer)
            : base(serviceProvider)
        {
            m_StringLocalizer = stringLocalizer;
        }

        protected override Task OnExecuteAsync()
            => PrintAsync(m_StringLocalizer["team:help"]);
    }

    [Command("store")]
    [CommandAlias("deposit")]
    [CommandParent(typeof(CommandVaultTeam))]
    [CommandSyntax("<itemId> [amount]")]
    [CommandDescription("Stores backpack items in the current party's shared vault.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultTeamStore : Command
    {
        private readonly VaultService m_Vault;
        private readonly IItemDirectory m_Items;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultTeamStore(
            IServiceProvider serviceProvider,
            VaultService vault,
            IItemDirectory items,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Vault = vault;
            m_Items = items;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            if (await m_Vault.GetTeamVaultAsync(user) == null)
                throw new UserFriendlyException(m_StringLocalizer["team:not_in_team"]);
            var (itemId, amount) = CommandVaultStore.ParseArgs(Context, m_StringLocalizer);
            var result = await m_Vault.StoreTeamAsync(user, itemId, amount);
            var names = await VaultNames.BuildMapAsync(m_Items);
            var name = VaultNames.NameOf(itemId, names);
            if (result.Stored == 0)
            {
                throw new UserFriendlyException(result.CapacityReached
                    ? m_StringLocalizer["team:full"]
                    : m_StringLocalizer["store:none", new { name }]);
            }
            await PrintAsync(m_StringLocalizer["team:store_done", new { amount = result.Stored, name }]);
        }
    }

    [Command("take")]
    [CommandAlias("withdraw")]
    [CommandParent(typeof(CommandVaultTeam))]
    [CommandSyntax("<itemId> [amount]")]
    [CommandDescription("Takes items from the current party's shared vault.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultTeamTake : Command
    {
        private readonly VaultService m_Vault;
        private readonly IItemDirectory m_Items;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultTeamTake(
            IServiceProvider serviceProvider,
            VaultService vault,
            IItemDirectory items,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Vault = vault;
            m_Items = items;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            if (await m_Vault.GetTeamVaultAsync(user) == null)
                throw new UserFriendlyException(m_StringLocalizer["team:not_in_team"]);
            var (itemId, amount) = CommandVaultStore.ParseArgs(Context, m_StringLocalizer);
            var taken = await m_Vault.TakeTeamAsync(user, itemId, amount);
            var names = await VaultNames.BuildMapAsync(m_Items);
            var name = VaultNames.NameOf(itemId, names);
            if (taken == 0) throw new UserFriendlyException(m_StringLocalizer["team:take_none", new { name }]);
            await PrintAsync(m_StringLocalizer["team:take_done", new { amount = taken, name }]);
        }
    }

    [Command("list")]
    [CommandAlias("info")]
    [CommandParent(typeof(CommandVaultTeam))]
    [CommandDescription("Lists the current party's shared vault.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultTeamList : Command
    {
        private readonly VaultService m_Vault;
        private readonly IItemDirectory m_Items;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultTeamList(
            IServiceProvider serviceProvider,
            VaultService vault,
            IItemDirectory items,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Vault = vault;
            m_Items = items;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            var team = await m_Vault.GetTeamVaultAsync(user);
            if (team == null) throw new UserFriendlyException(m_StringLocalizer["team:not_in_team"]);
            var container = VaultContainerRef.Team(team.Value.Context.Key);
            var items = m_Vault.Get(container);
            await PrintAsync(m_StringLocalizer["team:header", new
            {
                team = team.Value.Context.DisplayName,
                used = m_Vault.UsedSlots(container),
                max = team.Value.Container.Capacity
            }]);
            if (items.Count == 0)
            {
                await PrintAsync(m_StringLocalizer["team:empty"]);
                return;
            }

            var names = await VaultNames.BuildMapAsync(m_Items);
            foreach (var group in System.Linq.Enumerable.GroupBy(items, x => x.ItemId))
            {
                var count = System.Linq.Enumerable.Count(group);
                var slots = System.Linq.Enumerable.Sum(group, item => item.SlotCost);
                await PrintAsync(m_StringLocalizer["list:line", new
                {
                    id = group.Key,
                    name = VaultNames.NameOf(group.Key, names),
                    amount = count,
                    slots
                }]);
            }
        }
    }

    [Command("upgrade")]
    [CommandAlias("buy")]
    [CommandParent(typeof(CommandVaultTeam))]
    [CommandDescription("Buys shared vault capacity for the party using your own balance.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandVaultTeamUpgrade : Command
    {
        private readonly VaultService m_Vault;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandVaultTeamUpgrade(
            IServiceProvider serviceProvider,
            VaultService vault,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Vault = vault;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var result = await m_Vault.PurchaseTeamCapacityAsync((UnturnedUser)Context.Actor);
            if (!result.Success)
                throw new UserFriendlyException(PurchaseError(result));
            await PrintAsync(m_StringLocalizer["team:upgrade_done", new
            {
                slots = result.SlotsAdded,
                price = result.Price,
                max = result.NewCapacity
            }]);
        }

        private string PurchaseError(TeamVaultPurchaseResult result)
        {
            switch (result.Status)
            {
                case TeamVaultPurchaseStatus.Disabled:
                    return m_StringLocalizer["team:upgrade_disabled"];
                case TeamVaultPurchaseStatus.NotInTeam:
                    return m_StringLocalizer["team:not_in_team"];
                case TeamVaultPurchaseStatus.EconomyUnavailable:
                    return m_StringLocalizer["team:upgrade_economy_missing"];
                case TeamVaultPurchaseStatus.MaximumReached:
                    return m_StringLocalizer["team:upgrade_maximum"];
                case TeamVaultPurchaseStatus.InvalidConfiguration:
                    return m_StringLocalizer["team:upgrade_invalid_config"];
                default:
                    return result.Error ?? m_StringLocalizer["team:upgrade_failed"];
            }
        }
    }
}
