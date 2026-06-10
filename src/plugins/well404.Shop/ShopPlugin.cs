using System;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.Unturned.Plugins;
using well404.Shop;

[assembly: PluginMetadata("well404.Shop", DisplayName = "Shop")]

namespace well404.Shop
{
    public class ShopPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<ShopPlugin> m_Logger;

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
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }
    }
}
