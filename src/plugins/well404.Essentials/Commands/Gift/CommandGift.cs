using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Gift;
using well404.Essentials.Util;

namespace well404.Essentials.Commands.Gift
{
    [Command("gift")]
    [CommandSyntax("[id]")]
    [CommandDescription("Lists the gifts you can claim, or claims one by id.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandGift : Command
    {
        private readonly GiftService m_Gifts;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandGift(
            IServiceProvider serviceProvider,
            GiftService gifts,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Gifts = gifts;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;

            if (Context.Parameters.Length < 1)
            {
                await ListAsync(user);
                return;
            }

            await ClaimAsync(user, Context.Parameters[0]);
        }

        private async Task ListAsync(UnturnedUser user)
        {
            var listings = await m_Gifts.GetListingsAsync(user);
            if (listings.Count == 0)
            {
                await PrintAsync(m_StringLocalizer["gift:none"]);
                return;
            }

            await PrintAsync(m_StringLocalizer["gift:list_header"]);
            foreach (var listing in listings)
            {
                var status = listing.Ready
                    ? m_StringLocalizer["gift:status_ready"]
                    : m_StringLocalizer["gift:status_wait", new { time = TimeFormat.Humanize(listing.RefreshInSeconds) }];
                await PrintAsync(m_StringLocalizer["gift:list_entry", new { id = listing.Id, name = listing.Name, status }]);
            }
        }

        private async Task ClaimAsync(UnturnedUser user, string id)
        {
            var result = await m_Gifts.ClaimAsync(user, id);
            switch (result.Status)
            {
                case GiftClaimStatus.NotFound:
                    throw new UserFriendlyException(m_StringLocalizer["gift:not_found", new { id }]);
                case GiftClaimStatus.NoPermission:
                    throw new UserFriendlyException(m_StringLocalizer["gift:no_permission"]);
                case GiftClaimStatus.OnCooldown:
                    throw new UserFriendlyException(m_StringLocalizer["gift:on_cooldown",
                        new { name = result.GiftName, time = TimeFormat.Humanize(result.RefreshInSeconds) }]);
                default:
                    await PrintAsync(m_StringLocalizer["gift:claimed", new { name = result.GiftName }]);
                    break;
            }
        }
    }
}
