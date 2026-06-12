using System;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Plugins;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;
using well404.Economy;

[assembly: PluginMetadata("well404.Economy", DisplayName = "Economy")]

namespace well404.Economy
{
    public class EconomyPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<EconomyPlugin> m_Logger;

        private IWebPanelRegistry? m_WebPanelRegistry;
        private IPlayerMenuRegistry? m_PlayerMenuRegistry;

        public EconomyPlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<EconomyPlugin> logger,
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

            var settings = m_Configuration.Get<EconomySettings>() ?? new EconomySettings();
            m_Logger.LogInformation(
                "Currency: {Name} ({Symbol}); backend: {Backend}; kill rewards: {KillRewards}.",
                settings.Currency.Name, settings.Currency.Symbol, settings.Backend,
                settings.KillRewards.Enabled ? "enabled" : "disabled");

            RegisterWebPanel();
            RegisterPlayerMenu();
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_WebPanelRegistry?.UnregisterModule(EconomyWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_PlayerMenuRegistry?.UnregisterMenu(EconomyPlayerMenu.MenuId);
            m_PlayerMenuRegistry = null;
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        /// <summary>
        /// Registers the balance-management module with the web panel, if one is installed.
        /// The dependency is optional (<see cref="ILifetimeScope.ResolveOptional"/>) so the
        /// economy works exactly the same with or without well404.WebPanel present.
        /// </summary>
        private void RegisterWebPanel()
        {
            var registry = LifetimeScope.ResolveOptional<IWebPanelRegistry>();
            if (registry == null)
            {
                return;
            }

            if (!(LifetimeScope.Resolve<IEconomyProvider>() is EconomyProvider economy))
            {
                return;
            }

            var userManager = LifetimeScope.Resolve<IUserManager>();
            var configStore = new EconomyConfigStore(m_Configuration, WorkingDirectory);
            registry.RegisterModule(EconomyWebPanelModule.Create(economy, userManager, configStore));
            m_WebPanelRegistry = registry;
            m_Logger.LogInformation("Economy: registered the balance-management module with the web panel.");
        }

        /// <summary>
        /// Registers the player-facing wallet menu (balance + transfer) with the web panel's
        /// player surface, if a panel is installed. Optional, like <see cref="RegisterWebPanel"/>.
        /// </summary>
        private void RegisterPlayerMenu()
        {
            var registry = LifetimeScope.ResolveOptional<IPlayerMenuRegistry>();
            if (registry == null)
            {
                return;
            }

            var menu = new EconomyPlayerMenu(
                LifetimeScope.Resolve<IEconomyProvider>(),
                LifetimeScope.Resolve<IUserManager>(),
                LifetimeScope.Resolve<IUnturnedUserDirectory>(),
                m_Configuration);
            registry.RegisterMenu(menu);
            m_PlayerMenuRegistry = registry;
            m_Logger.LogInformation("Economy: registered the player wallet menu with the web panel.");
        }
    }
}
