using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Ioc;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel
{
    /// <summary>
    /// Global <see cref="IWebPanelRegistry"/> implementation. Registered with
    /// <see cref="ServiceImplementationAttribute"/> so <b>any</b> plugin can inject it
    /// and contribute management modules.
    /// <para>
    /// It is a global singleton activated in the global scope, so it must NOT take any
    /// plugin-scoped service in its constructor — it takes none. The panel HTTP host
    /// (owned by <see cref="WebPanelPlugin"/>) and the feature plugins all resolve this
    /// same instance, so registration order across plugin loads does not matter.
    /// </para>
    /// </summary>
    [ServiceImplementation(Lifetime = ServiceLifetime.Singleton)]
    public sealed class WebPanelRegistry : IWebPanelRegistry
    {
        private readonly object m_Lock = new object();
        private readonly Dictionary<string, WebPanelModule> m_Modules =
            new Dictionary<string, WebPanelModule>(StringComparer.OrdinalIgnoreCase);

        public void RegisterModule(WebPanelModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            lock (m_Lock)
            {
                m_Modules[module.Id] = module;
            }
        }

        public void UnregisterModule(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId))
            {
                return;
            }

            lock (m_Lock)
            {
                m_Modules.Remove(moduleId);
            }
        }

        public IReadOnlyList<WebPanelModule> GetModules()
        {
            lock (m_Lock)
            {
                return m_Modules.Values.ToList();
            }
        }
    }
}
