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
}
