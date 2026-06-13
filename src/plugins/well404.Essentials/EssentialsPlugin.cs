using System;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Plugins;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;
using well404.Essentials;
using well404.Essentials.Data;
using well404.Essentials.Gift;
using well404.Essentials.Party;
using well404.Essentials.Sleep;
using well404.Essentials.Teleport;
using well404.Essentials.Tp;
using well404.Essentials.Warps;

[assembly: PluginMetadata("well404.Essentials", DisplayName = "Essentials")]

namespace well404.Essentials
{
    public class EssentialsPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<EssentialsPlugin> m_Logger;

        private IWebPanelRegistry? m_WebPanelRegistry;
        private IPlayerMenuRegistry? m_PlayerMenuRegistry;

        public EssentialsPlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<EssentialsPlugin> logger,
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

            var settings = m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings();
            m_Logger.LogInformation(
                "Essentials loaded: warmup {Warmup}s, {Warps} warp(s), {Gifts} gift(s).",
                settings.Teleport.WarmupSeconds, settings.Warps.Count, settings.Gifts.Count);

            RegisterWebPanel();
            RegisterPlayerMenu();
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_WebPanelRegistry?.UnregisterModule(EssentialsWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_PlayerMenuRegistry?.UnregisterMenu(EssentialsPlayerMenu.MenuId);
            LifetimeScope.ResolveOptional<IPlayerCommandRegistry>()?.Unregister("well404.essentials");
            m_PlayerMenuRegistry = null;
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        /// <summary>
        /// Registers the settings/warps/gifts module with the web panel, if one is installed.
        /// Optional (<see cref="Autofac.ResolutionExtensions.ResolveOptional{T}"/>) so Essentials
        /// works the same with or without well404.WebPanel present.
        /// </summary>
        private void RegisterWebPanel()
        {
            var registry = LifetimeScope.ResolveOptional<IWebPanelRegistry>();
            if (registry == null)
            {
                return;
            }

            var store = LifetimeScope.Resolve<EssentialsConfigStore>();
            var itemDirectory = LifetimeScope.Resolve<IItemDirectory>();
            registry.RegisterModule(EssentialsWebPanelModule.Create(store, itemDirectory));
            m_WebPanelRegistry = registry;
            m_Logger.LogInformation("Essentials: registered the management module with the web panel.");
        }

        /// <summary>
        /// Registers the player-facing convenience menu (home/back/warps) with the web panel's
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

            var menu = new EssentialsPlayerMenu(
                LifetimeScope.Resolve<PlayerDataStore>(),
                LifetimeScope.Resolve<WarpService>(),
                LifetimeScope.Resolve<TeleportService>(),
                LifetimeScope.Resolve<TeleportRequestManager>(),
                LifetimeScope.Resolve<PartyInviteManager>(),
                LifetimeScope.Resolve<PartyService>(),
                LifetimeScope.Resolve<GiftService>(),
                LifetimeScope.Resolve<SleepVoteService>(),
                LifetimeScope.Resolve<IUnturnedUserDirectory>(),
                m_Configuration,
                translations,
                m_StringLocalizer);
            registry.RegisterMenu(menu);
            m_PlayerMenuRegistry = registry;

            LifetimeScope.Resolve<IPlayerCommandRegistry>().Register("well404.essentials", new[]
            {
                new PlayerCommandInfo("/home", "essentials.cmd.home", "well404.Essentials:home", "essentials.group"),
                new PlayerCommandInfo("/back", "essentials.cmd.back", "well404.Essentials:back", "essentials.group"),
                new PlayerCommandInfo("/tp", "essentials.cmd.tp", "well404.Essentials:tp", "essentials.group"),
                new PlayerCommandInfo("/tpa", "essentials.cmd.tpa", "well404.Essentials:tpa", "essentials.group"),
                new PlayerCommandInfo("/tpd", "essentials.cmd.tpd", "well404.Essentials:tpd", "essentials.group"),
                new PlayerCommandInfo("/party", "essentials.cmd.party", "well404.Essentials:party", "essentials.group"),
                new PlayerCommandInfo("/warp", "essentials.cmd.warp", "well404.Essentials:warp", "essentials.group"),
                new PlayerCommandInfo("/warps", "essentials.cmd.warps", "well404.Essentials:warps", "essentials.group"),
                new PlayerCommandInfo("/gift", "essentials.cmd.gift", "well404.Essentials:gift", "essentials.group"),
                new PlayerCommandInfo("/sleep", "essentials.cmd.sleep", "well404.Essentials:sleep", "essentials.group")
            });
            m_Logger.LogInformation("Essentials: registered the player utilities menu with the web panel.");
        }
    }
}
