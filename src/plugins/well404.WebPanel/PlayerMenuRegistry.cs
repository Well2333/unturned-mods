using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Ioc;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel
{
    /// <summary>
    /// Global <see cref="IPlayerMenuRegistry"/> implementation — the player-facing counterpart to
    /// <see cref="WebPanelRegistry"/>. A global singleton (no plugin-scoped constructor deps) so
    /// any feature plugin can register a player menu and the host resolves the same instance.
    /// </summary>
    [ServiceImplementation(Lifetime = ServiceLifetime.Singleton)]
    public sealed class PlayerMenuRegistry : IPlayerMenuRegistry
    {
        private readonly object m_Lock = new object();
        private readonly Dictionary<string, IPlayerMenu> m_Menus =
            new Dictionary<string, IPlayerMenu>(StringComparer.OrdinalIgnoreCase);

        public void RegisterMenu(IPlayerMenu menu)
        {
            if (menu == null)
            {
                throw new ArgumentNullException(nameof(menu));
            }

            lock (m_Lock)
            {
                m_Menus[menu.Id] = menu;
            }
        }

        public void UnregisterMenu(string menuId)
        {
            if (string.IsNullOrEmpty(menuId))
            {
                return;
            }

            lock (m_Lock)
            {
                m_Menus.Remove(menuId);
            }
        }

        public IReadOnlyList<IPlayerMenu> GetMenus()
        {
            lock (m_Lock)
            {
                // The home/intro tab is always shown first (and is the player panel's landing tab);
                // the rest keep their registration order. OrderBy is stable, so equal keys are intact.
                return m_Menus.Values
                    .OrderByDescending(m => string.Equals(m.Id, IntroPlayerMenu.MenuId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        public IPlayerMenu? GetMenu(string menuId)
        {
            if (string.IsNullOrEmpty(menuId))
            {
                return null;
            }

            lock (m_Lock)
            {
                return m_Menus.TryGetValue(menuId, out var menu) ? menu : null;
            }
        }
    }
}
