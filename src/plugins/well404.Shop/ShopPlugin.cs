using System;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Plugins;
using UnturnedMods.Shared.WebPanel;

[assembly: PluginMetadata("well404.Shop", DisplayName = "Shop")]

namespace well404.Shop
{
    public class ShopPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<ShopPlugin> m_Logger;

        private IWebPanelRegistry? m_WebPanelRegistry;

        public ShopPlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<ShopPlugin> logger,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
            m_Logger = logger;
        }

        protected override async UniTask OnLoadAsync()
        {
            // Switch to the main thread before calling any Unturned / UnityEngine API.
            await UniTask.SwitchToMainThread();
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_start"]);

            var settings = m_Configuration.Get<ShopSettings>() ?? new ShopSettings();
            m_Logger.LogInformation(
                "Shop loaded with {Count} item(s); discounts {State}.",
                settings.Items.Count, settings.Discounts.Enabled ? "enabled" : "disabled");

            RegisterWebPanel();
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_WebPanelRegistry?.UnregisterModule(ShopWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        /// <summary>
        /// Registers the catalog-editing module with the web panel, if one is installed.
        /// Optional (<see cref="ILifetimeScope.ResolveOptional"/>) so the shop works the
        /// same with or without well404.WebPanel present.
        /// </summary>
        private void RegisterWebPanel()
        {
            var registry = LifetimeScope.ResolveOptional<IWebPanelRegistry>();
            if (registry == null)
            {
                return;
            }

            var store = new ShopConfigStore(m_Configuration, WorkingDirectory);
            var itemDirectory = LifetimeScope.Resolve<IItemDirectory>();
            registry.RegisterModule(ShopWebPanelModule.Create(store, itemDirectory));
            m_WebPanelRegistry = registry;
            m_Logger.LogInformation("Shop: registered the catalog-editing module with the web panel.");
        }
    }
}
