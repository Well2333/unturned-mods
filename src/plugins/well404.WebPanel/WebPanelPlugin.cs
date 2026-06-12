using System;
using System.IO;
using System.Linq;
using System.Text;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Plugins;
using OpenMod.Unturned.Plugins;
using UnturnedMods.Shared.WebPanel;

[assembly: PluginMetadata("well404.WebPanel", DisplayName = "Web Panel")]

namespace well404.WebPanel
{
    /// <summary>
    /// Hosts the shared web management panel. It owns the HTTP server (started on
    /// load, stopped on unload) and reads the <see cref="IWebPanelRegistry"/> that
    /// feature plugins register their modules into — so this plugin has no knowledge
    /// of any specific feature (economy, shop, ...).
    /// </summary>
    public class WebPanelPlugin : OpenModUnturnedPlugin
    {
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;
        private readonly ILogger<WebPanelPlugin> m_Logger;

        private WebPanelHttpServer? m_Server;

        public WebPanelPlugin(
            IConfiguration configuration,
            IStringLocalizer stringLocalizer,
            ILogger<WebPanelPlugin> logger,
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

            var settings = m_Configuration.Get<WebPanelSettings>() ?? new WebPanelSettings();
            var web = settings.Web;

            var bind = string.IsNullOrWhiteSpace(web.BindAddress) ? "127.0.0.1" : web.BindAddress.Trim();
            var token = web.Token ?? string.Empty;
            var loopback = bind == "127.0.0.1" || bind == "::1"
                || string.Equals(bind, "localhost", StringComparison.OrdinalIgnoreCase);

            // Security: never expose a non-loopback address without a token unless
            // explicitly allowed. Otherwise downgrade to loopback.
            if (!loopback && token.Length == 0 && !web.AllowInsecurePublic)
            {
                m_Logger.LogWarning(
                    "WebPanel: bindAddress '{Bind}' is not loopback but no token is set — refusing to expose the "
                    + "panel publicly without auth; falling back to 127.0.0.1. Set web.token (recommended), or "
                    + "web.allowInsecurePublic: true to override.", bind);
                bind = "127.0.0.1";
                loopback = true;
            }

            if (!loopback && token.Length == 0)
            {
                m_Logger.LogWarning(
                    "WebPanel: serving on public address '{Bind}' WITHOUT a token (allowInsecurePublic). "
                    + "Anyone who can reach the port has full admin access.", bind);
            }

            // HttpListener expresses "all interfaces" as '+', not 0.0.0.0.
            var host = bind == "0.0.0.0" ? "+" : bind;
            var prefix = $"http://{host}:{web.Port}/";

            var registry = LifetimeScope.Resolve<IWebPanelRegistry>();
            var playerRegistry = LifetimeScope.Resolve<IPlayerMenuRegistry>();
            var sessions = LifetimeScope.Resolve<PlayerWebSessionManager>();
            var html = LoadResource("index.html");
            var playerHtml = LoadResource("player.html");

            var server = new WebPanelHttpServer(
                registry, playerRegistry, sessions, m_Logger, prefix, token, html, playerHtml);
            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                server.Dispose();
                m_Logger.LogError(ex,
                    "WebPanel: failed to start HTTP listener on {Prefix}. Is the port already in use, or the "
                    + "address not assigned to this host?", prefix);
                return;
            }

            m_Server = server;
            m_Logger.LogInformation(
                "WebPanel listening on {Prefix} (auth: {Auth}).",
                prefix, token.Length > 0 ? "token" : "none");
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            m_Server?.Dispose();
            m_Server = null;
            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        /// <summary>Loads an embedded HTML resource (matched by resource-name suffix).</summary>
        private static string LoadResource(string suffix)
        {
            var assembly = typeof(WebPanelPlugin).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                return "<!doctype html><title>Web Panel</title><body>Panel HTML resource missing.</body>";
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return "<!doctype html><title>Web Panel</title><body>Panel HTML resource missing.</body>";
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
