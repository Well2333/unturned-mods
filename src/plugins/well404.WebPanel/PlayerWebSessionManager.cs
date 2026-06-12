using System;
using System.Collections.Concurrent;
using System.Linq;
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

        public PlayerWebSessionManager(IPluginAccessor<WebPanelPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
        }

        /// <summary>
        /// Sets (or clears, with null) the live tunnel base URL. When present it takes precedence
        /// over <c>web.publicBaseUrl</c>, so player links use the current public address even when
        /// it is assigned dynamically at startup. Set by the plugin once the tunnel is up.
        /// </summary>
        public void SetTunnelBaseUrl(string? baseUrl)
            => m_TunnelBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl!.Trim().TrimEnd('/');

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
            if (baseUrl == null)
            {
                return null;
            }

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
