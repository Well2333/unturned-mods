using System;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.Unturned.Plugins;
using UnturnedMods.Shared.WebPanel;
using well404.AdminTools;

[assembly: PluginMetadata("well404.AdminTools", DisplayName = "Admin Tools")]

namespace well404.AdminTools
{
    /// <summary>
    /// Admin/moderation tools: godmode, kick, temporary ban / unban, role assignment and per-role
    /// command grants — usable via commands and, when <c>well404.WebPanel</c> is installed, the panel.
    /// </summary>
    public class AdminToolsPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<AdminToolsPlugin> m_Logger;

        private IWebPanelRegistry? m_WebPanelRegistry;

        public AdminToolsPlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<AdminToolsPlugin> logger,
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
            RegisterWebPanel();
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_WebPanelRegistry?.UnregisterModule(AdminToolsWebPanelModule.ModuleId);
            m_WebPanelRegistry = null;
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        /// <summary>
        /// Registers the admin module with the web panel, if one is installed. Optional, so the
        /// plugin works the same with or without well404.WebPanel.
        /// </summary>
        private void RegisterWebPanel()
        {
            var registry = LifetimeScope.ResolveOptional<IWebPanelRegistry>();
            if (registry == null)
            {
                return;
            }

            var translations = LifetimeScope.Resolve<IWebTranslationRegistry>();
            translations.AddBundle(WebI18n.Zh, WebI18n.ZhTable);

            registry.RegisterModule(AdminToolsWebPanelModule.Create(LifetimeScope.Resolve<AdminToolsService>()));
            m_WebPanelRegistry = registry;
            m_Logger.LogInformation("AdminTools: registered the admin module with the web panel.");
        }
    }
}
