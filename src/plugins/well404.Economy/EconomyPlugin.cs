using System;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.Unturned.Plugins;
using well404.Economy;

[assembly: PluginMetadata("well404.Economy", DisplayName = "Economy")]

namespace well404.Economy
{
    public class EconomyPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<EconomyPlugin> m_Logger;

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
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }
    }
}
