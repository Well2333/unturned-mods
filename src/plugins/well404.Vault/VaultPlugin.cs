using System;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Unturned.Plugins;
using UnturnedMods.Shared.WebPanel;

[assembly: PluginMetadata("well404.Vault", DisplayName = "Personal Vault")]

namespace well404.Vault
{
    /// <summary>
    /// A per-player personal storage: store and withdraw backpack items via commands or the web
    /// panel, with full item-state fidelity, capacity counted in inventory grid cells. The web panel
    /// is an optional integration — without it, the commands still work.
    /// </summary>
    public class VaultPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<VaultPlugin> m_Logger;

        private IPlayerMenuRegistry? m_PlayerMenuRegistry;
        private IWebPanelRegistry? m_WebPanelRegistry;

        public VaultPlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<VaultPlugin> logger,
            IServiceProvider serviceProvider) : base(serviceProvider)
        {
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
            m_Logger = logger;
        }

        protected override async UniTask OnLoadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_start"]);

            var vault = LifetimeScope.Resolve<VaultService>();
            await vault.InitializeAsync(DataStore);

            var translations = LifetimeScope.ResolveOptional<IWebTranslationRegistry>();
            translations?.AddBundle(VaultI18n.Zh, VaultI18n.ZhTable);

            RegisterPlayerMenu(vault, translations);
            RegisterWebPanel();

            m_Logger.LogInformation("Vault loaded: capacity {Slots} grid cells.", vault.MaxSlots);
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_PlayerMenuRegistry?.UnregisterMenu(VaultPlayerMenu.MenuId);
            m_PlayerMenuRegistry = null;
            m_WebPanelRegistry?.UnregisterModule(VaultWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            LifetimeScope.ResolveOptional<IPlayerCommandRegistry>()?.Unregister("well404.vault");
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        /// <summary>Registers the player-facing vault menu + intro command help, if a web panel is present.</summary>
        private void RegisterPlayerMenu(VaultService vault, IWebTranslationRegistry? translations)
        {
            var registry = LifetimeScope.ResolveOptional<IPlayerMenuRegistry>();
            if (registry == null || translations == null)
            {
                return;
            }

            registry.RegisterMenu(new VaultPlayerMenu(vault, LifetimeScope.Resolve<IUserManager>(), translations));
            m_PlayerMenuRegistry = registry;

            LifetimeScope.ResolveOptional<IPlayerCommandRegistry>()?.Register("well404.vault", new[]
            {
                new PlayerCommandInfo("/vault", "Open the personal vault — store, take and list your items.", "well404.Vault:commands.vault", "Vault"),
                new PlayerCommandInfo("/vault store <id> [amount]", "Store items from your backpack into the vault (by item id).", "well404.Vault:commands.vault.store", "Vault"),
                new PlayerCommandInfo("/vault take <id> [amount]", "Withdraw items from the vault into your backpack (by item id).", "well404.Vault:commands.vault.take", "Vault"),
                new PlayerCommandInfo("/vault list", "List your vault contents and how full it is.", "well404.Vault:commands.vault.list", "Vault")
            });

            m_Logger.LogInformation("Vault: registered the player vault menu with the web panel.");
        }

        /// <summary>Registers the capacity setting with the web panel, if one is present (optional).</summary>
        private void RegisterWebPanel()
        {
            var registry = LifetimeScope.ResolveOptional<IWebPanelRegistry>();
            if (registry == null)
            {
                return;
            }

            registry.RegisterModule(VaultWebPanelModule.Create(new VaultConfigStore(m_Configuration, WorkingDirectory)));
            m_WebPanelRegistry = registry;
        }
    }
}
