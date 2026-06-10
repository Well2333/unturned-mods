using System.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;

namespace well404.Economy.Commands
{
    /// <summary>The outcome of resolving a player argument to an economy account.</summary>
    public sealed class ResolvedPlayer
    {
        public ResolvedPlayer(string id, string displayName, UnturnedUser? online)
        {
            Id = id;
            DisplayName = displayName;
            Online = online;
        }

        /// <summary>The economy owner id (Steam ID string).</summary>
        public string Id { get; }

        public string DisplayName { get; }

        /// <summary>The online user, or null if resolved as an offline Steam ID.</summary>
        public UnturnedUser? Online { get; }
    }

    public static class PlayerResolver
    {
        /// <summary>
        /// Resolves a name or Steam ID to an account. Online players are matched by
        /// name or id; a bare 17-digit Steam ID also resolves while offline (so
        /// admins can adjust offline balances on the database backend).
        /// Returns null if nothing matched.
        /// </summary>
        public static async Task<ResolvedPlayer?> ResolveAsync(IUserManager userManager, string search)
        {
            var user = await userManager.FindUserAsync(
                KnownActorTypes.Player, search, UserSearchMode.FindByNameOrId) as UnturnedUser;

            if (user != null)
            {
                return new ResolvedPlayer(user.Id, user.DisplayName, user);
            }

            if (search.Length == 17 && ulong.TryParse(search, out _))
            {
                return new ResolvedPlayer(search, search, null);
            }

            return null;
        }
    }
}
