using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMod.API.Permissions;
using well404.Essentials.Data;

namespace well404.Essentials.Warps
{
    /// <summary>
    /// Reads/writes warps through the shared <see cref="EssentialsConfigStore"/> (so in-game
    /// <c>/warp set</c> and the web panel stay consistent) and enforces the per-warp permission.
    /// Registered as a plugin-scoped singleton.
    /// </summary>
    public sealed class WarpService
    {
        private readonly EssentialsConfigStore m_Store;
        private readonly IPermissionChecker m_PermissionChecker;

        public WarpService(EssentialsConfigStore store, IPermissionChecker permissionChecker)
        {
            m_Store = store;
            m_PermissionChecker = permissionChecker;
        }

        /// <summary>The permission node a player needs to use the warp with the given name.</summary>
        public static string PermissionFor(string name) => "well404.essentials.warps." + name.ToLowerInvariant();

        public IReadOnlyList<WarpEntry> All => m_Store.Warps;

        public WarpEntry? Find(string name) => m_Store.FindWarp(name);

        public async Task<bool> HasAccessAsync(IPermissionActor actor, string name)
            => await m_PermissionChecker.CheckPermissionAsync(actor, PermissionFor(name)) == PermissionGrantResult.Grant;

        public void Set(string name, PlayerLocation location, int cooldownSeconds)
        {
            m_Store.UpsertWarp(new WarpEntry
            {
                Name = name,
                X = (decimal)location.X,
                Y = (decimal)location.Y,
                Z = (decimal)location.Z,
                Yaw = (decimal)location.Yaw,
                CooldownSeconds = cooldownSeconds
            });
        }

        public bool Delete(string name) => m_Store.RemoveWarp(name);

        /// <summary>Converts a warp entry to a teleport destination.</summary>
        public static PlayerLocation ToLocation(WarpEntry warp)
            => new PlayerLocation((float)warp.X, (float)warp.Y, (float)warp.Z, (float)warp.Yaw);
    }
}
