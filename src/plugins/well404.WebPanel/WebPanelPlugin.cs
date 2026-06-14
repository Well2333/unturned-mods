using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenMod.API.Permissions;
using OpenMod.API.Plugins;
using OpenMod.API.Users;
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
        private ITunnelProvider? m_Tunnel;
        private CancellationTokenSource? m_TunnelMonitorCts;

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

            // The admin panel is ALWAYS behind a secret token path (/<token>/…). A token is
            // mandatory: if config leaves it empty we mint a random one for this run so the panel
            // is never served unauthenticated. Set web.token to keep the URL stable across restarts.
            var token = (web.Token ?? string.Empty).Trim();
            var generatedToken = false;
            if (token.Length == 0)
            {
                token = NewToken();
                generatedToken = true;
                PersistGeneratedToken(token);
            }

            // HttpListener expresses "all interfaces" as '+', not 0.0.0.0.
            var host = bind == "0.0.0.0" ? "+" : bind;
            var prefix = $"http://{host}:{web.Port}/";

            var registry = LifetimeScope.Resolve<IWebPanelRegistry>();
            var playerRegistry = LifetimeScope.Resolve<IPlayerMenuRegistry>();
            var translations = LifetimeScope.Resolve<IWebTranslationRegistry>();
            var sessions = LifetimeScope.Resolve<PlayerWebSessionManager>();

            // Panel content owned by this plugin: the player home/intro tab, its admin editor, and
            // this plugin's own translations + /menu command help.
            translations.AddBundle(WebPanelI18n.Zh, WebPanelI18n.ZhTable);
            var introStore = new IntroStore(WorkingDirectory);
            var commands = LifetimeScope.Resolve<IPlayerCommandRegistry>();
            playerRegistry.RegisterMenu(new IntroPlayerMenu(
                introStore, commands, translations,
                LifetimeScope.Resolve<IPermissionChecker>(), LifetimeScope.Resolve<IUserManager>()));
            registry.RegisterModule(WebPanelIntroModule.Create(introStore, translations));
            commands.Register("well404.webpanel", new[]
            {
                new PlayerCommandInfo("/menu", "Open your personal web panel link in a browser to manage things from a page.", "well404.WebPanel:commands.menu", "Web Panel")
            });

            var html = LoadResource("index.html");
            var playerHtml = LoadResource("player.html");
            var playerLanguages = new PlayerLanguageStore(WorkingDirectory);
            var adminLanguage = new AdminLanguageStore(WorkingDirectory);

            var server = new WebPanelHttpServer(
                registry, playerRegistry, translations, sessions, playerLanguages, adminLanguage,
                m_Logger, prefix, token, html, playerHtml, web.DevPlayer);
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

            // Start the optional outbound tunnel (cloudflared / ngrok / …). When it yields a public
            // URL we feed it to the player-link generator so /menu links work without port-forwarding.
            var tunnelUrl = await StartTunnelAsync(web, sessions);

            var displayHost = host == "+" ? "<server-ip>" : host;
            var adminBase = tunnelUrl ?? $"http://{displayHost}:{web.Port}";
            var adminUrl = $"{adminBase}/{token}/";
            if (generatedToken)
            {
                m_Logger.LogWarning(
                    "WebPanel: no web.token was set — generated one and saved it to config.yaml. The admin "
                    + "panel is at {AdminUrl} — keep this URL secret.", adminUrl);
            }
            else
            {
                m_Logger.LogInformation("WebPanel admin panel at {AdminUrl} — keep this URL secret.", adminUrl);
            }

            if (web.DevPlayer != null && web.DevPlayer.Enabled && !string.IsNullOrWhiteSpace(web.DevPlayer.SteamId))
            {
                m_Logger.LogWarning(
                    "WebPanel: DEV player preview is ON — {DevUrl} opens the player panel as {SteamId} without "
                    + "joining the game. Disable web.devPlayer in production.", adminBase + "/" + token + "/dev-player",
                    web.DevPlayer.SteamId);
            }

            // Keep the (quick) tunnel alive: it can drop after a while and would otherwise leave the
            // panel unreachable until a manual restart. Watch it and bring it back with a fresh URL.
            if (tunnelUrl != null)
            {
                var effective = ResolveEffectiveTunnel(web.Tunnel);
                if (effective.AutoRestart)
                {
                    m_TunnelMonitorCts = new CancellationTokenSource();
                    _ = MonitorTunnelAsync(web, token, sessions, tunnelUrl, effective, m_TunnelMonitorCts.Token);
                }
            }
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            try
            {
                m_TunnelMonitorCts?.Cancel();
            }
            catch
            {
                // ignored
            }

            m_TunnelMonitorCts?.Dispose();
            m_TunnelMonitorCts = null;
            m_Tunnel?.Stop();
            m_Tunnel?.Dispose();
            m_Tunnel = null;
            m_Server?.Dispose();
            m_Server = null;

            LifetimeScope.Resolve<IPlayerMenuRegistry>().UnregisterMenu(IntroPlayerMenu.MenuId);
            LifetimeScope.Resolve<IWebPanelRegistry>().UnregisterModule(WebPanelIntroModule.ModuleId);
            LifetimeScope.Resolve<IPlayerCommandRegistry>().Unregister("well404.webpanel");

            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        /// <summary>
        /// Starts the optional outbound tunnel and, on success, publishes its public base URL to the
        /// player-link generator. Returns the URL (no trailing slash) or null if disabled / failed.
        /// </summary>
        private async Task<string?> StartTunnelAsync(WebServerSettings web, PlayerWebSessionManager sessions, CancellationToken ct = default)
        {
            var tunnel = web.Tunnel;
            if (tunnel == null || !tunnel.Enabled)
            {
                return null;
            }

            tunnel = ResolveEffectiveTunnel(tunnel);
            var provider = new ProcessTunnelProvider(tunnel, m_Logger);
            try
            {
                var url = await provider.StartAsync(web.Port, ct);
                if (url == null)
                {
                    provider.Stop();
                    m_Logger.LogWarning(
                        "WebPanel: tunnel '{Command}' did not report a public URL within {Timeout}s; player "
                        + "links fall back to web.publicBaseUrl.", tunnel.Command, tunnel.ReadyTimeoutSeconds);
                    return null;
                }

                m_Tunnel = provider;
                sessions.SetTunnelBaseUrl(url);
                m_Logger.LogInformation(
                    "WebPanel: tunnel up at {Url} (player /menu links and the admin URL use it).", url);
                return url;
            }
            catch (Exception ex)
            {
                provider.Stop();
                m_Logger.LogError(ex,
                    "WebPanel: failed to start tunnel '{Command}'. Is it installed and on PATH?", tunnel.Command);
                return null;
            }
        }

        /// <summary>
        /// Watches the live tunnel and restarts it (publishing a fresh URL) whenever its process exits
        /// or its public URL stops responding. The HTTP probe must succeed at least once before it is
        /// allowed to trigger a restart, so an environment that cannot reach the URL (e.g. no outbound
        /// HTTPS) silently falls back to process-exit detection rather than looping on restarts.
        /// </summary>
        private async Task MonitorTunnelAsync(
            WebServerSettings web, string token, PlayerWebSessionManager sessions,
            string initialUrl, TunnelSettings effective, CancellationToken ct)
        {
            var intervalSeconds = effective.HealthCheckSeconds > 0 ? effective.HealthCheckSeconds : 60;
            var probeEnabled = effective.HealthCheckSeconds > 0;
            var currentUrl = initialUrl;
            var probeProven = false;   // becomes true after the first successful probe
            var probeFailures = 0;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var tunnel = m_Tunnel;
                var restart = false;

                if (tunnel == null || !tunnel.IsRunning)
                {
                    m_Logger.LogWarning("WebPanel: tunnel process is no longer running — restarting it.");
                    restart = true;
                }
                else if (probeEnabled)
                {
                    var healthy = await ProbeAsync(http, currentUrl, ct).ConfigureAwait(false);
                    if (healthy)
                    {
                        probeProven = true;
                        probeFailures = 0;
                    }
                    else if (probeProven)
                    {
                        probeFailures++;
                        m_Logger.LogWarning("WebPanel: tunnel URL {Url} did not respond (attempt {Count}).", currentUrl, probeFailures);
                        if (probeFailures >= 2)
                        {
                            restart = true;
                        }
                    }
                    // If the probe has never succeeded, assume this host just can't reach the URL and
                    // don't restart on it — process-exit detection still covers a dead tunnel.
                }

                if (!restart)
                {
                    continue;
                }

                m_Tunnel?.Stop();
                m_Tunnel?.Dispose();
                m_Tunnel = null;

                var newUrl = await StartTunnelAsync(web, sessions, ct).ConfigureAwait(false);
                probeProven = false;
                probeFailures = 0;
                if (newUrl != null)
                {
                    currentUrl = newUrl;
                    m_Logger.LogWarning(
                        "WebPanel: tunnel restarted — new admin panel URL is {AdminUrl} (it changed; players must "
                        + "re-open /menu for fresh links).", newUrl + "/" + token + "/");
                }
                else
                {
                    m_Logger.LogWarning("WebPanel: tunnel restart did not yield a URL; will retry in {Seconds}s.", intervalSeconds);
                }
            }
        }

        /// <summary>True if a GET to the tunnel's public root returns a success status (the request
        /// reached our local server through the tunnel). Any error / non-2xx (incl. Cloudflare's
        /// tunnel-down 5xx) counts as unhealthy.</summary>
        private static async Task<bool> ProbeAsync(HttpClient http, string baseUrl, CancellationToken ct)
        {
            try
            {
                using var response = await http.GetAsync(baseUrl + "/", ct).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Applies the built-in preset for <c>type: cloudflare</c> (fixed cloudflared args + URL
        /// pattern, only the binary path is taken from config). <c>type: custom</c> is used verbatim.
        /// </summary>
        private static TunnelSettings ResolveEffectiveTunnel(TunnelSettings t)
        {
            if (string.Equals(t.Type, "custom", StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }

            // Default = cloudflare: a no-account Cloudflare Quick Tunnel.
            return new TunnelSettings
            {
                Enabled = t.Enabled,
                Type = "cloudflare",
                Command = string.IsNullOrWhiteSpace(t.Command) ? "cloudflared" : t.Command,
                Args = "tunnel --url http://127.0.0.1:{port} --no-autoupdate",
                UrlPattern = "https://[a-z0-9-]+\\.trycloudflare\\.com",
                ApiUrl = string.Empty,
                ReadyTimeoutSeconds = t.ReadyTimeoutSeconds > 0 ? t.ReadyTimeoutSeconds : 30,
                AutoRestart = t.AutoRestart,
                HealthCheckSeconds = t.HealthCheckSeconds
            };
        }

        private const string TokenAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        /// <summary>A 12-character mixed-case alphanumeric token (URL-path safe).</summary>
        private static string NewToken()
        {
            var bytes = new byte[12];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var chars = new char[12];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = TokenAlphabet[bytes[i] % TokenAlphabet.Length];
            }

            return new string(chars);
        }

        /// <summary>
        /// Writes a freshly generated admin token into the plugin's on-disk <c>config.yaml</c> so it
        /// stays stable across restarts. Best-effort: only fills an empty <c>token:</c> line and never
        /// throws (a failure just means the token is regenerated next load).
        /// </summary>
        private void PersistGeneratedToken(string token)
        {
            try
            {
                var path = Path.Combine(WorkingDirectory, "config.yaml");
                if (!File.Exists(path))
                {
                    return;
                }

                var text = File.ReadAllText(path);
                var updated = Regex.Replace(
                    text,
                    "(?m)^(?<indent>[ \\t]*)token:[ \\t]*(\"\"|'')?[ \\t]*\\r?$",
                    "${indent}token: \"" + token + "\"");

                if (updated == text)
                {
                    m_Logger.LogWarning(
                        "WebPanel: generated an admin token but could not find an empty 'token:' line in {Path} "
                        + "to save it; it will change next restart unless you set web.token manually.", path);
                    return;
                }

                File.WriteAllText(path, updated);
                m_Logger.LogInformation("WebPanel: saved the generated admin token to {Path}.", path);
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: failed to save the generated admin token to config.yaml.");
            }
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
