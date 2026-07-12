using System;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Eventing;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Core.Plugins.Events;
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
        // Captured at load so unload never resolves from the (by-then disposed) Autofac scope.
        private IPlayerCommandRegistry? m_PlayerCommandRegistry;

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

            RegisterWebPanelExtensions();
        }

        /// <summary>Registers every optional WebPanel contribution that is not registered yet.
        /// Called both during normal load and after later plugins load, because on an OpenMod reload
        /// WebPanel may be activated after Economy.</summary>
        internal void RegisterWebPanelExtensions()
        {
            if (m_WebPanelRegistry == null)
            {
                RegisterWebPanel();
            }

            if (m_PlayerMenuRegistry == null)
            {
                RegisterPlayerMenu();
            }
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_WebPanelRegistry?.UnregisterModule(EconomyWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_PlayerMenuRegistry?.UnregisterMenu(EconomyPlayerMenu.MenuId);
            m_PlayerMenuRegistry = null;
            m_PlayerCommandRegistry?.Unregister("well404.economy");
            m_PlayerCommandRegistry = null;
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

            var translations = LifetimeScope.Resolve<IWebTranslationRegistry>();
            translations.AddBundle(WebI18n.Zh, WebI18n.ZhTable);

            var menu = new EconomyPlayerMenu(
                LifetimeScope.Resolve<IEconomyProvider>(),
                LifetimeScope.Resolve<IUserManager>(),
                LifetimeScope.Resolve<IUnturnedUserDirectory>(),
                m_Configuration,
                translations,
                m_StringLocalizer);
            registry.RegisterMenu(menu);
            m_PlayerMenuRegistry = registry;

            m_PlayerCommandRegistry = LifetimeScope.Resolve<IPlayerCommandRegistry>();
            m_PlayerCommandRegistry.Register("well404.economy", new[]
            {
                new PlayerCommandInfo("/balance", "Check your current account balance.", "well404.Economy:commands.balance", "Economy"),
                new PlayerCommandInfo("/pay <player> <amount>", "Transfer money from your account to another online player.", "well404.Economy:commands.pay", "Economy")
            });
            m_Logger.LogInformation("Economy: registered the player wallet menu with the web panel.");
        }
    }

    public sealed class WebPanelRegistrationListener : IEventListener<PluginLoadedEvent>
    {
        private readonly IPluginAccessor<EconomyPlugin> m_PluginAccessor;

        public WebPanelRegistrationListener(IPluginAccessor<EconomyPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
        }

        public Task HandleEventAsync(object? sender, PluginLoadedEvent @event)
        {
            m_PluginAccessor.Instance?.RegisterWebPanelExtensions();
            return Task.CompletedTask;
        }
    }
}
