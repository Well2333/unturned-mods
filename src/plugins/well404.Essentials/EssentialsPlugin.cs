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
using well404.Essentials;

[assembly: PluginMetadata("well404.Essentials", DisplayName = "Essentials")]

namespace well404.Essentials
{
    public class EssentialsPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<EssentialsPlugin> m_Logger;

        private IWebPanelRegistry? m_WebPanelRegistry;

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
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_WebPanelRegistry?.UnregisterModule(EssentialsWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
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
    }
}
