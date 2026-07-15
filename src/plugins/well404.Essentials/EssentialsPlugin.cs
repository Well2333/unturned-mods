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
using OpenMod.Core.Permissions;
using OpenMod.Core.Plugins.Events;
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
[assembly: RegisterPermission("well404.essentials.cooldown.exempt", Description = "Exempts a player from Essentials teleport cooldowns.")]

namespace well404.Essentials
{
    public class EssentialsPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<EssentialsPlugin> m_Logger;

        private IWebPanelRegistry? m_WebPanelRegistry;
        private IPlayerMenuRegistry? m_PlayerMenuRegistry;
        // Captured at load so unload never resolves from the (by-then disposed) Autofac scope.
        private IPlayerCommandRegistry? m_PlayerCommandRegistry;

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
            LifetimeScope.Resolve<EssentialsConfigStore>().PersistMigrationIfNeeded(WorkingDirectory);
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_start"]);

            var settings = m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings();
            m_Logger.LogInformation(
                "Essentials loaded: warmup {Warmup}s, {Warps} warp(s), {Gifts} gift(s).",
                settings.Teleport.WarmupSeconds, settings.Warps.Count, settings.Gifts.Count);

            RegisterWebPanelExtensions();
        }

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
            m_WebPanelRegistry?.UnregisterModule(EssentialsWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_PlayerMenuRegistry?.UnregisterMenu(EssentialsPlayerMenu.MenuId);
            m_PlayerMenuRegistry = null;
            m_PlayerCommandRegistry?.Unregister("well404.essentials");
            m_PlayerCommandRegistry = null;
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
            var warps = LifetimeScope.Resolve<WarpService>();
            var itemDirectory = LifetimeScope.Resolve<IItemDirectory>();
            registry.RegisterModule(EssentialsWebPanelModule.Create(store, warps, itemDirectory));
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

            // Descriptions and the group heading are English source strings (the i18n key convention
            // used across the panel); Chinese comes from WebI18n.ZhTable, English falls back to the key.
            m_PlayerCommandRegistry = LifetimeScope.Resolve<IPlayerCommandRegistry>();
            m_PlayerCommandRegistry.Register("well404.essentials", new[]
            {
                new PlayerCommandInfo("/home", "Teleport back to the home you saved. After a short warm-up you return to that spot.", "well404.Essentials:commands.home", "Utilities"),
                new PlayerCommandInfo("/home set", "Save your current position as your home, so /home brings you back here.", "well404.Essentials:commands.home", "Utilities"),
                new PlayerCommandInfo("/back", "Return to the place where you last died (available for a short while after death).", "well404.Essentials:commands.back", "Utilities"),
                new PlayerCommandInfo("/tp <player>", "Ask another online player for permission to teleport to them.", "well404.Essentials:commands.tp", "Utilities"),
                new PlayerCommandInfo("/tpa", "Accept the most recent teleport request someone sent you.", "well404.Essentials:commands.tpa", "Utilities"),
                new PlayerCommandInfo("/tpd", "Decline the most recent teleport request someone sent you.", "well404.Essentials:commands.tpd", "Utilities"),
                new PlayerCommandInfo("/party", "Create or manage a party — invite, accept, leave, kick. Party members can teleport to each other.", "well404.Essentials:commands.party", "Utilities"),
                new PlayerCommandInfo("/warp <name>", "Teleport to a named warp point you have access to.", "well404.Essentials:commands.warp", "Utilities"),
                new PlayerCommandInfo("/warps", "List every warp point you are allowed to teleport to.", "well404.Essentials:commands.warps", "Utilities"),
                new PlayerCommandInfo("/gift", "Claim the free gift packs available to you (some refresh on a schedule).", "well404.Essentials:commands.gift", "Utilities"),
                new PlayerCommandInfo("/sleep", "Cast a sleep vote; once enough online players have voted, the world toggles between day and night.", "well404.Essentials:commands.sleep", "Utilities")
            });
            m_Logger.LogInformation("Essentials: registered the player utilities menu with the web panel.");
        }
    }

    public sealed class WebPanelRegistrationListener : IEventListener<PluginLoadedEvent>
    {
        private readonly IPluginAccessor<EssentialsPlugin> m_PluginAccessor;

        public WebPanelRegistrationListener(IPluginAccessor<EssentialsPlugin> pluginAccessor)
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
