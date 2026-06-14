using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.Vault
{
    /// <summary>
    /// The player-facing vault surface for the web panel: a compact list with two sections — the
    /// player's current backpack (a Store button per item) and the vault contents (a Take button per
    /// item) — plus a capacity header. Mirrors <c>/vault store</c> / <c>/vault take</c>. Driven by the
    /// generic player-menu model, so the host needs no knowledge of the vault.
    /// </summary>
    public sealed class VaultPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "vault";

        private readonly VaultService m_Vault;
        private readonly IUserManager m_UserManager;
        private readonly IWebTranslationRegistry m_Tr;

        public VaultPlayerMenu(VaultService vault, IUserManager userManager, IWebTranslationRegistry translations)
        {
            m_Vault = vault;
            m_UserManager = userManager;
            m_Tr = translations;
        }

        public string Id => MenuId;

        public string Title => "Vault";

        public string? Icon => "🧳";

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var lang = context.Language;
            var header = m_Tr.Format(lang, "Vault: {0} / {1} slots", m_Vault.UsedSlots(context.SteamId), m_Vault.MaxSlots);

            var user = await ResolveOnlineAsync(context.SteamId);
            if (user == null)
            {
                return new PlayerMenuView(m_Tr.Resolve("Vault", lang), header, new List<PlayerCard>(),
                    m_Tr.Resolve("You must be online to use the vault.", lang), null, "list");
            }

            var backpackGroup = m_Tr.Resolve("Backpack", lang);
            var vaultGroup = m_Tr.Resolve("Vault", lang);
            var cards = new List<PlayerCard>();

            // Backpack: each distinct item with a Store button.
            var backpack = await m_Vault.ListBackpackAsync(user);
            foreach (var entry in backpack.OrderBy(e => VaultService.NameOf(e.ItemId)))
            {
                var buttons = new[]
                {
                    new PlayerButton("store", m_Tr.Resolve("Store", lang), "primary", m_Tr.Resolve("Amount to store", lang))
                };
                cards.Add(new PlayerCard(
                    entry.ItemId.ToString(CultureInfo.InvariantCulture),
                    VaultService.NameOf(entry.ItemId),
                    null,
                    new[] { "×" + entry.Count.ToString(CultureInfo.InvariantCulture) },
                    buttons,
                    backpackGroup,
                    "#" + entry.ItemId.ToString(CultureInfo.InvariantCulture)));
            }

            // Vault: each distinct stored item with a Take button.
            foreach (var group in m_Vault.Get(context.SteamId).GroupBy(x => x.ItemId).OrderBy(g => VaultService.NameOf(g.Key)))
            {
                var count = group.Count();
                var slots = group.Sum(x => x.SlotCost);
                var buttons = new[]
                {
                    new PlayerButton("take", m_Tr.Resolve("Take", lang), "success", m_Tr.Resolve("Amount to take", lang))
                };
                cards.Add(new PlayerCard(
                    group.Key.ToString(CultureInfo.InvariantCulture),
                    VaultService.NameOf(group.Key),
                    null,
                    new[]
                    {
                        "×" + count.ToString(CultureInfo.InvariantCulture),
                        m_Tr.Format(lang, "{0} slots", slots)
                    },
                    buttons,
                    vaultGroup,
                    "#" + group.Key.ToString(CultureInfo.InvariantCulture)));
            }

            return new PlayerMenuView(m_Tr.Resolve("Vault", lang), header, cards, null, null, "list");
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var lang = context.Language;
            if (!ushort.TryParse(cardKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId) || itemId == 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Item not found.", lang));
            }

            var amount = 1;
            if (!string.IsNullOrEmpty(value)
                && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0))
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Enter a valid quantity.", lang));
            }

            var user = await ResolveOnlineAsync(context.SteamId);
            if (user == null)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("You must be online to use the vault.", lang));
            }

            var name = VaultService.NameOf(itemId);
            switch (actionId)
            {
                case "store":
                {
                    var result = await m_Vault.StoreAsync(user, itemId, amount);
                    if (result.Stored == 0)
                    {
                        return PlayerActionResult.Fail(result.CapacityReached
                            ? m_Tr.Resolve("Vault is full.", lang)
                            : m_Tr.Format(lang, "You don't have {0} in your backpack.", name));
                    }

                    var msg = m_Tr.Format(lang, "Stored {0}× {1}.", result.Stored, name);
                    if (result.CapacityReached)
                    {
                        msg += " " + m_Tr.Resolve("Vault is full.", lang);
                    }

                    return PlayerActionResult.Ok(msg);
                }

                case "take":
                {
                    var taken = await m_Vault.TakeAsync(user, itemId, amount);
                    return taken == 0
                        ? PlayerActionResult.Fail(m_Tr.Format(lang, "You have no {0} in the vault.", name))
                        : PlayerActionResult.Ok(m_Tr.Format(lang, "Took {0}× {1}.", taken, name));
                }

                default:
                    return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
            }
        }

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;
    }
}
