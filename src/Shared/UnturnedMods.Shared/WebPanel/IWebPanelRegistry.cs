using System.Collections.Generic;
using OpenMod.API.Ioc;

namespace UnturnedMods.Shared.WebPanel
{
    /// <summary>
    /// The registration surface a feature plugin uses to expose management actions
    /// on the shared web panel, <b>without</b> the panel plugin depending on it.
    /// <para>
    /// Implemented by <c>well404.WebPanel</c> as a global singleton service (the same
    /// cross-plugin pattern as <c>IEconomyProvider</c>): a feature plugin injects this
    /// abstraction — optionally, so it still works when the panel is not installed —
    /// and registers a <see cref="WebPanelModule"/> describing its actions. The panel
    /// host reads the registry at request time and renders a generic, schema-driven UI
    /// from the descriptors, so no feature logic is hard-coded into the panel itself.
    /// </para>
    /// </summary>
    [Service]
    public interface IWebPanelRegistry
    {
        /// <summary>Adds (or replaces, by <see cref="WebPanelModule.Id"/>) a module.</summary>
        void RegisterModule(WebPanelModule module);

        /// <summary>Removes a module by id. Safe to call for an unknown id.</summary>
        void UnregisterModule(string moduleId);

        /// <summary>A snapshot of the currently registered modules.</summary>
        IReadOnlyList<WebPanelModule> GetModules();
    }
}
