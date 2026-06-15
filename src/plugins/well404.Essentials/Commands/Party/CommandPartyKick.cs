using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Party;

namespace well404.Essentials.Commands.Party
{
    [Command("kick")]
    [CommandParent(typeof(CommandParty))]
    [CommandSyntax("<player>")]
    [CommandDescription("Removes a player from your party (party leader only).")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandPartyKick : Command
    {
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly PartyService m_Party;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandPartyKick(
            IServiceProvider serviceProvider,
            IUnturnedUserDirectory userDirectory,
            PartyService party,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_UserDirectory = userDirectory;
            m_Party = party;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            if (Context.Parameters.Length < 1)
            {
                throw new CommandWrongUsageException(Context);
            }

            var user = (UnturnedUser)Context.Actor;
            var search = Context.Parameters[0];

            var target = m_UserDirectory.FindUser(search, UserSearchMode.FindByNameOrId);
            if (target == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:player_not_found", new { player = search }]);
            }

            await UniTask.SwitchToMainThread();
            var status = m_Party.Kick(user, target);
            switch (status)
            {
                case PartyKickStatus.NotInParty:
                    throw new UserFriendlyException(m_StringLocalizer["party:none_self"]);
                case PartyKickStatus.NotLeader:
                    throw new UserFriendlyException(m_StringLocalizer["party:not_leader"]);
                case PartyKickStatus.CannotKickSelf:
                    throw new UserFriendlyException(m_StringLocalizer["party:cannot_kick_self"]);
                case PartyKickStatus.TargetNotInParty:
                    throw new UserFriendlyException(m_StringLocalizer["party:target_not_member", new { player = target.DisplayName }]);
                default:
                    await PrintAsync(m_StringLocalizer["party:kicked", new { player = target.DisplayName }]);
                    await target.PrintMessageAsync(m_StringLocalizer["party:kicked_other"]);
                    break;
            }
        }
    }
}
