using System;
using System.Collections.Concurrent;
using System.Linq;
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
    /// <summary>A validated player web session.</summary>
    public sealed class PlayerSession
    {
        public PlayerSession(string steamId, string displayName, DateTime expiresUtc)
        {
            SteamId = steamId;
            DisplayName = displayName;
            ExpiresUtc = expiresUtc;
        }

        public string SteamId { get; }
        public string DisplayName { get; }
        public DateTime ExpiresUtc { get; }
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
        private readonly ConcurrentDictionary<string, PlayerSession> m_Sessions =
            new ConcurrentDictionary<string, PlayerSession>(StringComparer.Ordinal);

        private volatile string? m_TunnelBaseUrl;

        // Completes when the initial tunnel bring-up resolves (URL obtained, or gave up). Lets
        // CreateLinkAsync wait briefly so a /menu right after startup doesn't fail on the window
        // where the tunnel is still coming up. Null when no tunnel is being brought up.
        private volatile TaskCompletionSource<bool>? m_TunnelReady;

        /// <summary>How long CreateLinkAsync waits for the tunnel to come up before giving up.</summary>
        private const int TunnelWaitSeconds = 25;

        public PlayerWebSessionManager(IPluginAccessor<WebPanelPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
        }

        /// <summary>Marks that a tunnel is being brought up, so CreateLinkAsync waits for its first
        /// result instead of immediately reporting "no public address". Called by the plugin at load.</summary>
        public void BeginTunnel()
            => m_TunnelReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Sets (or clears, with null) the live tunnel base URL. When present it takes precedence
        /// over <c>web.publicBaseUrl</c>, so player links use the current public address even when
        /// it is assigned dynamically at startup. Set by the plugin once the tunnel is up.
        /// </summary>
        public void SetTunnelBaseUrl(string? baseUrl)
        {
            m_TunnelBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl!.Trim().TrimEnd('/');
            if (m_TunnelBaseUrl != null)
            {
                m_TunnelReady?.TrySetResult(true);
            }
        }

        /// <summary>Marks the tunnel bring-up as concluded without a URL (download/start failed), so
        /// CreateLinkAsync stops waiting and falls back to web.publicBaseUrl (or reports unavailable).</summary>
        public void SetTunnelUnavailable() => m_TunnelReady?.TrySetResult(false);

        private WebServerSettings? ReadSettings()
        {
            var plugin = m_PluginAccessor.Instance;
            if (plugin == null || !plugin.IsComponentAlive)
            {
                return null;
            }

            var configuration = plugin.LifetimeScope.Resolve<IConfiguration>();
            return (configuration.Get<WebPanelSettings>() ?? new WebPanelSettings()).Web;
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
                    await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(TunnelWaitSeconds)))
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
            // Links are valid for at least 15 minutes (the floor); past that they stay valid only
            // while the player is online (see Validate). So the "expiry" stored here is the floor.
            var minutes = Math.Max(settings.PlayerSessionMinutes, 15);
            var token = NewToken();
            m_Sessions[token] = new PlayerSession(steamId, displayName ?? string.Empty,
                DateTime.UtcNow.AddMinutes(minutes));

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
            if (string.IsNullOrWhiteSpace(steamId))
            {
                return null;
            }

            var token = NewToken();
            m_Sessions[token] = new PlayerSession(steamId.Trim(),
                string.IsNullOrWhiteSpace(displayName) ? "Dev Player" : displayName.Trim(),
                DateTime.UtcNow.AddDays(1));
            return token;
        }

        /// <summary>
        /// Returns the session for a token, or null if unknown/expired. A session is valid for at
        /// least its floor window (≥15 min from creation); after that it stays valid only while the
        /// player is still online, and is dropped the moment they go offline.
        /// </summary>
        public PlayerSession? Validate(string? token)
        {
            if (string.IsNullOrEmpty(token) || !m_Sessions.TryGetValue(token!, out var session))
            {
                return null;
            }

            // Within the floor window: always valid (even briefly offline mid-login).
            if (DateTime.UtcNow <= session.ExpiresUtc)
            {
                return session;
            }

            // Past the floor: valid as long as the player is online; expire on disconnect.
            if (IsOnline(session.SteamId))
            {
                return session;
            }

            m_Sessions.TryRemove(token!, out _);
            return null;
        }

        /// <summary>True if a player with this Steam ID is currently connected.</summary>
        private bool IsOnline(string steamId)
        {
            var plugin = m_PluginAccessor.Instance;
            if (plugin == null || !plugin.IsComponentAlive)
            {
                return false;
            }

            try
            {
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
