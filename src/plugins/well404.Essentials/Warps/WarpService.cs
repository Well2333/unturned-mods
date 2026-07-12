using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMod.API;
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
        private readonly IPermissionRegistry m_PermissionRegistry;
        private readonly IOpenModComponent m_Component;

        public WarpService(
            EssentialsConfigStore store,
            IPermissionChecker permissionChecker,
            IPermissionRegistry permissionRegistry,
            IOpenModComponent component)
        {
            m_Store = store;
            m_PermissionChecker = permissionChecker;
            m_PermissionRegistry = permissionRegistry;
            m_Component = component;

            foreach (var warp in store.Warps)
            {
                RegisterPermission(warp.Name);
            }
        }

        /// <summary>The fully-qualified permission node a player needs to use the named warp.</summary>
        public static string PermissionFor(string name)
            => "well404.Essentials:" + RegistrationPermissionFor(name);

        private static string RegistrationPermissionFor(string name)
            => "well404.essentials.warps." + name.ToLowerInvariant();

        public IReadOnlyList<WarpEntry> All => m_Store.Warps;

        public WarpEntry? Find(string name) => m_Store.FindWarp(name);

        public async Task<bool> HasAccessAsync(IPermissionActor actor, string name)
            => await m_PermissionChecker.CheckPermissionAsync(actor, PermissionFor(name)) == PermissionGrantResult.Grant;

        public void Set(string name, PlayerLocation location, int cooldownSeconds)
        {
            Upsert(new WarpEntry
            {
                Name = name,
                X = (decimal)location.X,
                Y = (decimal)location.Y,
                Z = (decimal)location.Z,
                Yaw = (decimal)location.Yaw,
                CooldownSeconds = cooldownSeconds
            });
        }

        /// <summary>Adds or updates a warp and makes its dynamic permission known to OpenMod.</summary>
        public void Upsert(WarpEntry warp)
        {
            RegisterPermission(warp.Name);
            m_Store.UpsertWarp(warp);
        }

        public bool Delete(string name) => m_Store.RemoveWarp(name);

        private void RegisterPermission(string name)
            => m_PermissionRegistry.RegisterPermission(
                m_Component,
                RegistrationPermissionFor(name),
                $"Allows using the '{name}' warp.");

        /// <summary>Converts a warp entry to a teleport destination.</summary>
        public static PlayerLocation ToLocation(WarpEntry warp)
            => new PlayerLocation((float)warp.X, (float)warp.Y, (float)warp.Z, (float)warp.Yaw);
    }
}
