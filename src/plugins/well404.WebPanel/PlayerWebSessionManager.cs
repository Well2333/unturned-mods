using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Ioc;
using OpenMod.API.Plugins;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel
{
    public enum PlayerSessionKind
    {
        Player,
        Developer
    }

    /// <summary>A validated player web session bound to one plugin-load generation.</summary>
    public sealed class PlayerSession
    {
        public PlayerSession(string steamId, string displayName, DateTime expiresUtc)
            : this(steamId, displayName, expiresUtc, PlayerSessionKind.Player, 0)
        {
        }

        public PlayerSession(
            string steamId, string displayName, DateTime expiresUtc,
            PlayerSessionKind kind, long generation)
        {
            SteamId = steamId;
            DisplayName = displayName;
            ExpiresUtc = expiresUtc;
            Kind = kind;
            Generation = generation;
        }

        public string SteamId { get; }
        public string DisplayName { get; }
        public DateTime ExpiresUtc { get; }
        public PlayerSessionKind Kind { get; }
        public long Generation { get; }
    }

    /// <summary>
    /// Issues and validates short-lived, per-player tokens for the panel's player surface, and
    /// builds the in-game link a player opens in their browser. A global singleton implementing
    /// <see cref="IPlayerWebSessionService"/>; reads the panel's config lazily via
    /// <see cref="IPluginAccessor{TPlugin}"/> (it must not capture plugin-scoped services in its
    /// constructor — see the global-service rule).
    /// </summary>
    [ServiceImplementation(Lifetime = ServiceLifetime.Singleton)]
    public sealed class PlayerWebSessionManager : IPlayerWebSessionService
    {
        private readonly IPluginAccessor<WebPanelPlugin> m_PluginAccessor;
        private readonly Func<string, bool>? m_IsOnlineOverride;
        private readonly Func<WebServerSettings?>? m_SettingsOverride;
        private readonly ConcurrentDictionary<string, PlayerSession> m_Sessions =
            new ConcurrentDictionary<string, PlayerSession>(StringComparer.Ordinal);

        private volatile string? m_TunnelBaseUrl;
        private readonly object m_TunnelStateLock = new object();
        private long m_TunnelGeneration;
        private long m_SessionGeneration;

        // Completes when the initial tunnel bring-up resolves (URL obtained, or gave up). Lets
        // CreateLinkAsync wait briefly so a /menu right after startup doesn't fail on the window
        // where the tunnel is still coming up. Null when no tunnel is being brought up.
        private volatile TaskCompletionSource<bool>? m_TunnelReady;

        public PlayerWebSessionManager(IPluginAccessor<WebPanelPlugin> pluginAccessor)
            : this(pluginAccessor, null, null)
        {
        }

        internal PlayerWebSessionManager(
            IPluginAccessor<WebPanelPlugin> pluginAccessor,
            Func<string, bool>? isOnlineOverride,
            Func<WebServerSettings?>? settingsOverride)
        {
            m_PluginAccessor = pluginAccessor;
            m_IsOnlineOverride = isOnlineOverride;
            m_SettingsOverride = settingsOverride;
        }

        /// <summary>
        /// Invalidates every existing token and starts a fresh plugin-load session generation.
        /// Call on both load and unload so singleton state can never survive a reload.
        /// </summary>
        public void RevokeAllSessions()
        {
            Interlocked.Increment(ref m_SessionGeneration);
            m_Sessions.Clear();
        }

        /// <summary>Marks that a tunnel is being brought up, so CreateLinkAsync waits for its first
        /// result instead of immediately reporting "no public address". Called by the plugin at load.</summary>
        public long BeginTunnel()
        {
            lock (m_TunnelStateLock)
            {
                m_TunnelGeneration++;
                m_TunnelBaseUrl = null;
                m_TunnelReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return m_TunnelGeneration;
            }
        }

        /// <summary>
        /// Sets (or clears, with null) the live tunnel base URL. When present it takes precedence
        /// over <c>web.publicBaseUrl</c>, so player links use the current public address even when
        /// it is assigned dynamically at startup. Set by the plugin once the tunnel is up.
        /// </summary>
        public void SetTunnelBaseUrl(long generation, string? baseUrl)
        {
            lock (m_TunnelStateLock)
            {
                if (generation != m_TunnelGeneration)
                {
                    return;
                }

                m_TunnelBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl!.Trim().TrimEnd('/');
                if (m_TunnelBaseUrl != null)
                {
                    m_TunnelReady?.TrySetResult(true);
                }
            }
        }

        /// <summary>Marks the tunnel bring-up as concluded without a URL (download/start failed), so
        /// CreateLinkAsync stops waiting and falls back to web.publicBaseUrl (or reports unavailable).</summary>
        public void SetTunnelUnavailable(long generation)
        {
            lock (m_TunnelStateLock)
            {
                if (generation == m_TunnelGeneration)
                {
                    m_TunnelReady?.TrySetResult(false);
                }
            }
        }

        /// <summary>Clears tunnel state only when it still belongs to the unloading plugin instance.
        /// The generation check prevents a late old-instance callback from erasing a new reload's URL.</summary>
        public void EndTunnel(long generation)
        {
            lock (m_TunnelStateLock)
            {
                if (generation != m_TunnelGeneration)
                {
                    return;
                }

                m_TunnelGeneration++;
                m_TunnelBaseUrl = null;
                m_TunnelReady?.TrySetResult(false);
                m_TunnelReady = null;
            }
        }

        /// <summary>Clears a URL retained by the global singleton when the reloaded config disables
        /// the built-in tunnel.</summary>
        public void DisableTunnel()
        {
            lock (m_TunnelStateLock)
            {
                m_TunnelGeneration++;
                m_TunnelBaseUrl = null;
                m_TunnelReady?.TrySetResult(false);
                m_TunnelReady = null;
            }
        }

        private WebServerSettings? ReadSettings()
        {
            if (m_SettingsOverride != null)
            {
                return m_SettingsOverride();
            }

            try
            {
                var plugin = m_PluginAccessor?.Instance;
                if (plugin == null || !plugin.IsComponentAlive)
                {
                    return null;
                }

                var configuration = plugin.LifetimeScope.Resolve<IConfiguration>();
                return (configuration.Get<WebPanelSettings>() ?? new WebPanelSettings()).Web;
            }
            catch
            {
                return null;
            }
        }

        public string? CreateLink(string steamId, string displayName, string? menuId = null)
        {
            if (string.IsNullOrEmpty(steamId))
            {
                return null;
            }

            var settings = ReadSettings();
            if (settings == null)
            {
                return null;
            }

            // A live tunnel URL wins over static config; otherwise derive from settings.
            var baseUrl = m_TunnelBaseUrl ?? ResolveBaseUrl(settings);
            return baseUrl == null ? null : BuildLink(baseUrl, steamId, displayName, settings, menuId);
        }

        public async Task<string?> CreateLinkAsync(string steamId, string displayName, string? menuId = null)
        {
            if (string.IsNullOrEmpty(steamId))
            {
                return null;
            }

            var settings = ReadSettings();
            if (settings == null)
            {
                return null;
            }

            var baseUrl = m_TunnelBaseUrl ?? ResolveBaseUrl(settings);
            if (baseUrl == null)
            {
                // No address yet — if a tunnel is still coming up (just after startup), wait briefly
                // for its first result so /menu works without the player having to retry. ConfigureAwait
                // (false) keeps the continuation off the Unity/UniTask main-thread context.
                var ready = m_TunnelReady;
                if (ready != null && !ready.Task.IsCompleted)
                {
                    var configuredTimeout = settings.Tunnel?.ReadyTimeoutSeconds ?? 30;
                    var waitSeconds = Math.Max(configuredTimeout, 30) + 5;
                    await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(waitSeconds)))
                        .ConfigureAwait(false);
                }

                baseUrl = m_TunnelBaseUrl ?? ResolveBaseUrl(settings);
                if (baseUrl == null)
                {
                    return null;
                }
            }

            return BuildLink(baseUrl, steamId, displayName, settings, menuId);
        }

        /// <summary>Mints a session token and assembles the full player-surface URL.</summary>
        private string BuildLink(string baseUrl, string steamId, string displayName, WebServerSettings settings, string? menuId)
        {
            // Keep a minimum 15-minute window, but validation also requires the player to remain
            // online and never extends the absolute expiry.
            var minutes = Math.Max(settings.PlayerSessionMinutes, 15);
            var token = NewToken();
            m_Sessions[token] = new PlayerSession(steamId, displayName ?? string.Empty,
                DateTime.UtcNow.AddMinutes(minutes), PlayerSessionKind.Player,
                Interlocked.Read(ref m_SessionGeneration));

            var url = baseUrl + "/p?t=" + token;
            if (!string.IsNullOrEmpty(menuId))
            {
                url += "#" + Uri.EscapeDataString(menuId!);
            }

            return url;
        }

        /// <summary>
        /// Mints a long-lived session for a fixed Steam ID and returns its token (not a URL). Used
        /// only by the admin-gated developer preview (<c>/&lt;token&gt;/dev-player</c>): the long floor
        /// keeps it valid even though the impersonated player is offline, so the player surface can be
        /// previewed from a browser. Returns null when no Steam ID is given.
        /// </summary>
        public string? CreateDevSession(string steamId, string displayName)
        {
            var settings = ReadSettings();
            var configured = settings?.DevPlayer;
            if (configured == null || !configured.Enabled || string.IsNullOrWhiteSpace(steamId) ||
                !string.Equals(configured.SteamId?.Trim(), steamId.Trim(), StringComparison.Ordinal))
            {
                return null;
            }

            var token = NewToken();
            m_Sessions[token] = new PlayerSession(steamId.Trim(),
                string.IsNullOrWhiteSpace(displayName) ? "Dev Player" : displayName.Trim(),
                DateTime.UtcNow.AddDays(1), PlayerSessionKind.Developer,
                Interlocked.Read(ref m_SessionGeneration));
            return token;
        }

        /// <summary>
        /// Returns a live session for a token. Player tokens are revoked as soon as the player is
        /// offline; developer tokens additionally require the current dev-player switch and Steam ID
        /// to still match. Every token is bound to the current plugin-load generation.
        /// </summary>
        public PlayerSession? Validate(string? token)
        {
            if (string.IsNullOrEmpty(token) || !m_Sessions.TryGetValue(token!, out var session))
            {
                return null;
            }

            if (session.Generation != Interlocked.Read(ref m_SessionGeneration))
            {
                m_Sessions.TryRemove(token!, out _);
                return null;
            }

            if (session.Kind == PlayerSessionKind.Developer)
            {
                var configured = ReadSettings()?.DevPlayer;
                if (DateTime.UtcNow <= session.ExpiresUtc && configured != null && configured.Enabled &&
                    string.Equals(configured.SteamId?.Trim(), session.SteamId, StringComparison.Ordinal))
                {
                    return session;
                }

                m_Sessions.TryRemove(token!, out _);
                return null;
            }

            if (DateTime.UtcNow <= session.ExpiresUtc && IsOnline(session.SteamId))
            {
                return session;
            }

            m_Sessions.TryRemove(token!, out _);
            return null;
        }

        /// <summary>True if a player with this Steam ID is currently connected.</summary>
        private bool IsOnline(string steamId)
        {
            if (m_IsOnlineOverride != null)
            {
                return m_IsOnlineOverride(steamId);
            }

            return IsOnlineViaPlugin(steamId);
        }

        private bool IsOnlineViaPlugin(string steamId)
        {
            try
            {
                var plugin = m_PluginAccessor?.Instance;
                if (plugin == null || !plugin.IsComponentAlive)
                {
                    return false;
                }

                var directory = plugin.LifetimeScope.Resolve<IUnturnedUserDirectory>();
                return directory.GetOnlineUsers()
                    .Any(u => string.Equals(u.Id, steamId, StringComparison.Ordinal));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Derives the public base URL (no trailing slash), or null if none is usable.</summary>
        private static string? ResolveBaseUrl(WebServerSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
            {
                return settings.PublicBaseUrl.Trim().TrimEnd('/');
            }

            var bind = string.IsNullOrWhiteSpace(settings.BindAddress) ? "127.0.0.1" : settings.BindAddress.Trim();
            if (bind == "127.0.0.1" || bind == "::1" || bind == "0.0.0.0"
                || string.Equals(bind, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                // Not a routable address players could reach — require an explicit publicBaseUrl.
                return null;
            }

            return $"http://{bind}:{settings.Port}";
        }

        private static string NewToken()
            => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }
}
