using OpenMod.API.Permissions;
using OpenMod.Core.Users;

namespace well404.AdminTools
{
    /// <summary>
    /// A minimal <see cref="IPermissionActor"/> for a player identified by Steam ID, so roles and
    /// permissions can be assigned even when the player is offline (the stores key by type + id).
    /// </summary>
    internal sealed class PlayerPermissionActor : IPermissionActor
    {
        public PlayerPermissionActor(string steamId, string? displayName = null)
        {
            Id = steamId;
            DisplayName = displayName ?? steamId;
        }

        public string Id { get; }

        public string Type => KnownActorTypes.Player;

        public string DisplayName { get; }

        public string FullActorName => Type + "/" + Id;
    }
}
