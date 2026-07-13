using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using SDG.Unturned;
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
        // Drives every background task this plugin starts (tunnel bring-up, monitor, post-host warning).
        private CancellationTokenSource? m_BackgroundCts;
        // The configured tunnel command (when a tunnel is enabled), so unload/startup can clean up the
        // cloudflared we spawned even if it wasn't tracked in m_Tunnel yet (startup race / crash).
        private string? m_TunnelCommand;

        // Registry singletons are captured at load so OnUnloadAsync never resolves from the Autofac
        // scope — at full server shutdown that scope is already disposed (ObjectDisposedException).
        private IWebPanelRegistry? m_WebPanelRegistry;
        private IPlayerMenuRegistry? m_PlayerMenuRegistry;
        private IPlayerCommandRegistry? m_PlayerCommandRegistry;
        private PlayerWebSessionManager? m_PlayerSessions;
        private long m_TunnelGeneration;

        // Problems noticed during load (panel/tunnel) that get re-surfaced prominently AFTER the server
        // finishes hosting, so the admin sees them below the noisy startup log rather than buried in it.
        private readonly object m_IssuesLock = new object();
        private readonly List<string> m_StartupIssues = new List<string>();
        // Completes once the initial tunnel bring-up has resolved (success or give-up), so the post-host
        // warning waits for the tunnel verdict before deciding whether to nag.
        private readonly TaskCompletionSource<bool> m_TunnelInitialDone =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
            m_PlayerSessions = sessions;
            m_WebPanelRegistry = registry;
            m_PlayerMenuRegistry = playerRegistry;

            // Panel content owned by this plugin: the player home/intro tab, its admin editor, and
            // this plugin's own translations + /menu command help.
            translations.AddBundle(WebPanelI18n.Zh, WebPanelI18n.ZhTable);
            var introStore = new IntroStore(WorkingDirectory);
            var commands = LifetimeScope.Resolve<IPlayerCommandRegistry>();
            m_PlayerCommandRegistry = commands;
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
            var favicon = LoadBinaryResource("unturned-favicon.jpg");
            var playerLanguages = new PlayerLanguageStore(WorkingDirectory);
            var adminLanguage = new AdminLanguageStore(WorkingDirectory);

            var server = new WebPanelHttpServer(
                registry, playerRegistry, translations, sessions, playerLanguages, adminLanguage,
                m_Logger, prefix, token, html, playerHtml, favicon, web.DevPlayer,
                web.RefreshIntervalSeconds);
            var displayHost = host == "+" ? "<server-ip>" : host;
            var localBase = $"http://{displayHost}:{web.Port}";

            m_BackgroundCts = new CancellationTokenSource();

            // Remember the tunnel command so unload can also clean up the cloudflared we spawn.
            m_TunnelCommand = web.Tunnel != null && web.Tunnel.Enabled ? web.Tunnel.Command : null;

            // A cloudflared we launched inherits this process's listening socket (Mono spawns children
            // with handle inheritance). If a previous run left one alive (e.g. a hard shutdown/crash),
            // it keeps the panel port bound and our bind below fails with "address already in use".
            // Kill any such leftover that we own BEFORE binding, so a restart self-heals.
            if (m_TunnelCommand != null)
            {
                KillOwnTunnelProcesses(m_TunnelCommand);
            }

            Exception? startError = null;
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    server.Start();
                    startError = null;
                    break;
                }
                catch (Exception ex)
                {
                    startError = ex;
                    // Port still busy on the first try → a leftover may not have fully released it yet.
                    // Re-kill and wait a moment, then retry once.
                    if (attempt == 1 && m_TunnelCommand != null)
                    {
                        m_Logger.LogWarning(
                            "WebPanel: HTTP listener bind on {Prefix} failed ({Err}); killing any leftover "
                            + "cloudflared and retrying once ...", prefix, ex.Message);
                        KillOwnTunnelProcesses(m_TunnelCommand);
                        await Task.Delay(1500).ConfigureAwait(false);
                    }
                }
            }

            if (startError != null)
            {
                server.Dispose();
                m_Logger.LogError(startError,
                    "WebPanel: failed to start HTTP listener on {Prefix}. Is the port already in use, or the "
                    + "address not assigned to this host?", prefix);
                RecordStartupIssue(
                    $"HTTP 监听启动失败({prefix}):端口可能被占用,或该地址未绑定到本机。Web 面板本次不可用。");
                m_TunnelInitialDone.TrySetResult(true);
                _ = WarnAfterServerHostedAsync(localBase, token, m_BackgroundCts.Token);
                return;
            }

            m_Server = server;

            // The local admin URL is known immediately; the tunnel (if any) comes up asynchronously
            // below and logs its public URL when ready — so startup is never blocked on a download.
            var adminUrl = $"{localBase}/{token}/";
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
                    + "joining the game. Disable web.devPlayer in production.", localBase + "/" + token + "/dev-player",
                    web.DevPlayer.SteamId);
            }

            // Bring the optional outbound tunnel up in the BACKGROUND so a slow/failing cloudflared
            // download never stalls server startup. On success it publishes the public URL to player
            // links and logs the public admin URL; failures are re-surfaced after the server is hosted.
            if (web.Tunnel != null && web.Tunnel.Enabled)
            {
                // Tell the session service a tunnel is coming up, so a /menu issued during the
                // startup window waits for the public URL instead of failing with "no public address".
                m_TunnelGeneration = sessions.BeginTunnel();
                // Run the whole tunnel bring-up on a thread-pool context, NOT inline on the UniTask
                // main-thread sync context. That context does not pump plain Task continuations, so an
                // await that completes synchronously here (e.g. cloudflared already cached, so EnsureAsync
                // returns without really awaiting) would strand every later await — the tunnel would
                // silently never come up. Task.Run starts it with a null sync context, so it can't happen.
                var bgCt = m_BackgroundCts.Token;
                var tunnelGeneration = m_TunnelGeneration;
                _ = Task.Run(() => BringUpTunnelAsync(web, token, sessions, tunnelGeneration, bgCt));
            }
            else
            {
                sessions.DisableTunnel();
                m_TunnelInitialDone.TrySetResult(true);
            }

            // Re-surface any startup problem prominently once the server has finished hosting.
            _ = WarnAfterServerHostedAsync(localBase, token, m_BackgroundCts.Token);
        }

        /// <summary>
        /// Brings the outbound tunnel up off the load path: starts it (downloading cloudflared if
        /// needed), and on success publishes the public admin URL and — when AutoRestart is on — keeps
        /// it alive. With auto-restart enabled, an initial failure is retried until success or unload.
        /// A terminal failure is recorded as a startup issue for the post-host warning.
        /// </summary>
        private async Task BringUpTunnelAsync(
            WebServerSettings web, string token, PlayerWebSessionManager sessions,
            long tunnelGeneration, CancellationToken ct)
        {
            string? url = null;
            var effective = ResolveEffectiveTunnel(web.Tunnel);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    url = await StartTunnelAsync(web, sessions, tunnelGeneration, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_Logger.LogError(ex, "WebPanel: tunnel bring-up threw unexpectedly.");
                }

                if (url != null || !effective.AutoRestart || ct.IsCancellationRequested)
                {
                    break;
                }

                m_Logger.LogWarning(
                    "WebPanel: initial tunnel start did not yield a public URL; retrying in 3 seconds.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            m_TunnelInitialDone.TrySetResult(true);

            if (url == null)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                // Unblock any /menu that is waiting for the tunnel — it won't come.
                sessions.SetTunnelUnavailable(tunnelGeneration);
                RecordStartupIssue(
                    "内置反代(tunnel)未能启动:cloudflared 下载/启动失败(见上方日志)。玩家 /menu 链接与公网管理面"
                    + "地址本次不可用;面板仍可经本地地址访问。可配置 web.tunnel.downloadMirrors / 系统代理后重试,"
                    + "或设 web.tunnel.enabled: false 关闭。");
                return;
            }

            m_Logger.LogInformation(
                "WebPanel: tunnel public admin panel at {AdminUrl} — keep this URL secret.", url + "/" + token + "/");

            if (effective.AutoRestart && !ct.IsCancellationRequested)
            {
                await MonitorTunnelAsync(
                    web, token, sessions, url, effective, tunnelGeneration, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Waits until the server has finished hosting (the connection code is shown) and the initial
        /// tunnel attempt has resolved, then — if anything went wrong during load — re-logs it as a
        /// prominent banner so the admin notices it below the noisy startup output.
        /// </summary>
        private async Task WarnAfterServerHostedAsync(string localBase, string token, CancellationToken ct)
        {
            try
            {
                // Poll for "server ready" (covers plugins loading either before or after hosting).
                for (var i = 0; i < 600 && !ct.IsCancellationRequested; i++)
                {
                    if (Level.isLoaded && !string.IsNullOrEmpty(Provider.serverID))
                    {
                        break;
                    }

                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }

                // Let the tunnel verdict settle (bounded), then a beat so we print below the host banner.
                await Task.WhenAny(m_TunnelInitialDone.Task, Task.Delay(TimeSpan.FromMinutes(5), ct)).ConfigureAwait(false);
                await Task.Delay(2000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            string[] issues;
            lock (m_IssuesLock)
            {
                if (m_StartupIssues.Count == 0)
                {
                    return;
                }

                issues = m_StartupIssues.ToArray();
            }

            var sb = new StringBuilder();
            sb.AppendLine("==================== WebPanel 启动异常提醒 ====================");
            for (var i = 0; i < issues.Length; i++)
            {
                sb.AppendLine($"  • {issues[i]}");
            }

            sb.AppendLine($"  本地管理面板地址: {localBase}/{token}/ (请保密)");
            sb.Append("============================================================");
            m_Logger.LogError(sb.ToString());
        }

        /// <summary>Records a load-time problem to be re-surfaced after the server is hosted.</summary>
        private void RecordStartupIssue(string message)
        {
            lock (m_IssuesLock)
            {
                m_StartupIssues.Add(message);
            }
        }

        protected override async UniTask OnUnloadAsync()
        {
            await UniTask.SwitchToMainThread();
            try
            {
                m_BackgroundCts?.Cancel();
            }
            catch
            {
                // ignored
            }

            m_TunnelInitialDone.TrySetResult(true);
            m_BackgroundCts?.Dispose();
            m_BackgroundCts = null;
            m_Tunnel?.Stop();
            m_Tunnel?.Dispose();
            m_Tunnel = null;
            if (m_TunnelGeneration != 0)
            {
                m_PlayerSessions?.EndTunnel(m_TunnelGeneration);
                m_TunnelGeneration = 0;
            }

            m_PlayerSessions = null;
            // Belt-and-suspenders: make sure no cloudflared we launched is left holding the panel port
            // (it inherited the listening socket), even if it wasn't tracked in m_Tunnel above.
            if (m_TunnelCommand != null)
            {
                KillOwnTunnelProcesses(m_TunnelCommand);
                m_TunnelCommand = null;
            }

            m_Server?.Dispose();
            m_Server = null;

            m_PlayerMenuRegistry?.UnregisterMenu(IntroPlayerMenu.MenuId);
            m_PlayerMenuRegistry = null;
            m_WebPanelRegistry?.UnregisterModule(WebPanelIntroModule.ModuleId);
            m_WebPanelRegistry = null;
            m_PlayerCommandRegistry?.Unregister("well404.webpanel");
            m_PlayerCommandRegistry = null;

            m_Logger.LogInformation(m_StringLocalizer["plugin_events:plugin_stop"]);
        }

        /// <summary>
        /// Starts the optional outbound tunnel and, on success, publishes its public base URL to the
        /// player-link generator. Returns the URL (no trailing slash) or null if disabled / failed.
        /// </summary>
        private async Task<string?> StartTunnelAsync(
            WebServerSettings web, PlayerWebSessionManager sessions,
            long tunnelGeneration, CancellationToken ct = default)
        {
            var tunnel = web.Tunnel;
            if (tunnel == null || !tunnel.Enabled)
            {
                return null;
            }

            tunnel = ResolveEffectiveTunnel(tunnel);

            // For a Cloudflare Quick Tunnel, make sure a cloudflared binary is actually runnable —
            // download a portable copy into the plugin's data dir when it is missing (never onto the
            // host system). For type: custom the admin owns the command, so we leave it untouched.
            if (string.Equals(tunnel.Type, "cloudflare", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = await CloudflaredDownloader
                    .EnsureAsync(tunnel.Command, tunnel.AutoDownload, tunnel.DownloadMirrors,
                        tunnel.DownloadAttempts, WorkingDirectory, m_Logger, ct)
                    .ConfigureAwait(false);

                if (resolved == null)
                {
                    // No usable cloudflared and auto-download gave up — EnsureAsync already logged why.
                    return null;
                }

                tunnel.Command = resolved;
            }

            var provider = new ProcessTunnelProvider(tunnel, m_Logger);
            try
            {
                var url = await provider.StartAsync(web.Port, ct).ConfigureAwait(false);
                if (url == null)
                {
                    var reason = provider.IsRunning ? "ready timeout elapsed" : "process exited before publishing a URL";
                    provider.Stop();
                    m_Logger.LogWarning(
                        "WebPanel: tunnel '{Command}' did not report a public URL ({Reason}; configured timeout "
                        + "{Timeout}s); player links fall back to web.publicBaseUrl while it retries.",
                        tunnel.Command, reason, tunnel.ReadyTimeoutSeconds);
                    return null;
                }

                // Unload may race with the URL line from cloudflared. Do not publish or retain a
                // provider after this plugin instance has already been cancelled.
                if (ct.IsCancellationRequested)
                {
                    provider.Stop();
                    return null;
                }

                m_Tunnel = provider;
                sessions.SetTunnelBaseUrl(tunnelGeneration, url);
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
            string initialUrl, TunnelSettings effective, long tunnelGeneration, CancellationToken ct)
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

                var newUrl = await StartTunnelAsync(
                    web, sessions, tunnelGeneration, ct).ConfigureAwait(false);
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
                AutoDownload = t.AutoDownload,
                DownloadMirrors = t.DownloadMirrors,
                DownloadAttempts = t.DownloadAttempts,
                // --http-host-header makes cloudflared send "Host: 127.0.0.1:{port}" to the local panel.
                // Without it, cloudflared forwards the public trycloudflare Host, which a bindAddress of
                // 127.0.0.1 (the default) does NOT match, so HttpListener answers "400 (Invalid host)".
                Args = "tunnel --url http://127.0.0.1:{port} --http-host-header 127.0.0.1:{port} --no-autoupdate",
                UrlPattern = "https://[a-z0-9-]+\\.trycloudflare\\.com",
                ApiUrl = string.Empty,
                ReadyTimeoutSeconds = t.ReadyTimeoutSeconds > 0 ? t.ReadyTimeoutSeconds : 30,
                AutoRestart = t.AutoRestart,
                HealthCheckSeconds = t.HealthCheckSeconds
            };
        }

        /// <summary>
        /// Terminates any running cloudflared process that WE launched (matched by executable path:
        /// the auto-downloaded portable binary, or an explicit path in <paramref name="command"/>).
        /// Such a process inherits this server's listening socket, so a leftover from a previous run
        /// keeps the panel port bound; killing it lets a restart re-bind. Best-effort; never throws.
        /// </summary>
        private void KillOwnTunnelProcesses(string? command)
        {
            try
            {
                var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                void AddCandidate(string? path)
                {
                    if (string.IsNullOrWhiteSpace(path)) return;
                    try { candidates.Add(Path.GetFullPath(path)); } catch { /* malformed path */ }
                }

                // The auto-downloaded portable copy (the common case).
                AddCandidate(Path.Combine(WorkingDirectory, "cloudflared", isWindows ? "cloudflared.exe" : "cloudflared"));
                // An explicit path the admin configured (only when it actually is a path, to avoid
                // matching/killing an unrelated system-wide cloudflared serving someone else's tunnel).
                if (!string.IsNullOrWhiteSpace(command) &&
                    (command!.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                     command.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                     Path.IsPathRooted(command)))
                {
                    AddCandidate(command);
                    if (isWindows && !command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        AddCandidate(command + ".exe");
                    }
                }

                Process[] procs;
                try { procs = Process.GetProcessesByName("cloudflared"); }
                catch { return; }

                var killed = 0;
                foreach (var p in procs)
                {
                    try
                    {
                        string? path = null;
                        try { path = p.MainModule?.FileName; } catch { /* access denied / exited */ }
                        if (path != null && candidates.Contains(Path.GetFullPath(path)))
                        {
                            p.Kill();
                            p.WaitForExit(3000);
                            killed++;
                        }
                    }
                    catch { /* already gone / not killable */ }
                    finally { p.Dispose(); }
                }

                if (killed > 0)
                {
                    m_Logger.LogWarning(
                        "WebPanel: terminated {Count} leftover cloudflared process(es) holding the panel port "
                        + "from a previous run.", killed);
                }
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "WebPanel: error while cleaning up leftover cloudflared processes.");
            }
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

        private static byte[] LoadBinaryResource(string suffix)
        {
            var assembly = typeof(WebPanelPlugin).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
            {
                throw new InvalidOperationException("Embedded WebPanel resource is missing: " + suffix);
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new InvalidOperationException("Embedded WebPanel resource cannot be opened: " + suffix);
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
