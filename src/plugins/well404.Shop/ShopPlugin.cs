using System;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Extensions.Economy.Abstractions;
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
        private IPlayerMenuRegistry? m_PlayerMenuRegistry;

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
                "Shop loaded with {Items} item(s) and {Bundles} bundle(s); discounts {State}.",
                settings.Items.Count, settings.Bundles.Count, settings.Discounts.Enabled ? "enabled" : "disabled");

            RegisterWebPanel();
            RegisterPlayerMenu();
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_WebPanelRegistry?.UnregisterModule(ShopWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_PlayerMenuRegistry?.UnregisterMenu(ShopPlayerMenu.MenuId);
            LifetimeScope.ResolveOptional<IPlayerCommandRegistry>()?.Unregister("well404.shop");
            m_PlayerMenuRegistry = null;
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

        /// <summary>
        /// Registers the player-facing shop menu with the web panel's player surface, if a panel
        /// is installed. Skipped when no economy provider is present (the shop can't transact
        /// without one). Optional, like <see cref="RegisterWebPanel"/>.
        /// </summary>
        private void RegisterPlayerMenu()
        {
            var registry = LifetimeScope.ResolveOptional<IPlayerMenuRegistry>();
            if (registry == null)
            {
                return;
            }

            var economy = LifetimeScope.ResolveOptional<IEconomyProvider>();
            if (economy == null)
            {
                return;
            }

            var translations = LifetimeScope.Resolve<IWebTranslationRegistry>();
            translations.AddBundle(WebI18n.Zh, WebI18n.ZhTable);

            var menu = new ShopPlayerMenu(
                LifetimeScope.Resolve<ShopCatalog>(),
                LifetimeScope.Resolve<ShopService>(),
                LifetimeScope.Resolve<DiscountService>(),
                economy,
                LifetimeScope.Resolve<IUserManager>(),
                LifetimeScope.Resolve<IItemDirectory>(),
                translations);
            registry.RegisterMenu(menu);
            m_PlayerMenuRegistry = registry;

            LifetimeScope.Resolve<IPlayerCommandRegistry>().Register("well404.shop", new[]
            {
                new PlayerCommandInfo("/shop", "Browse the server shop and see item prices.", "well404.Shop:commands.shop", "Shop"),
                new PlayerCommandInfo("/buy <id> [amount]", "Buy a plain item by its item id, or a bundle by its id, with your money.", "well404.Shop:commands.buy", "Shop"),
                new PlayerCommandInfo("/sell <id> [amount]", "Sell a plain item by its item id, or a bundle by its id, back to the shop for money.", "well404.Shop:commands.sell", "Shop")
            });
            m_Logger.LogInformation("Shop: registered the player shop menu with the web panel.");
        }
    }
}
