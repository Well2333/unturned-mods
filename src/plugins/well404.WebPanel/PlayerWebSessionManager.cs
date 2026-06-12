using System;
using System.Collections.Concurrent;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Ioc;
using OpenMod.API.Plugins;
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

        public PlayerWebSessionManager(IPluginAccessor<WebPanelPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
        }

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

            var baseUrl = ResolveBaseUrl(settings);
            if (baseUrl == null)
            {
                return null;
            }

            var minutes = settings.PlayerSessionMinutes > 0 ? settings.PlayerSessionMinutes : 5;
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

        /// <summary>Returns the session for a token, or null if unknown/expired (expired ones are dropped).</summary>
        public PlayerSession? Validate(string? token)
        {
            if (string.IsNullOrEmpty(token) || !m_Sessions.TryGetValue(token!, out var session))
            {
                return null;
            }

            if (session.ExpiresUtc <= DateTime.UtcNow)
            {
                m_Sessions.TryRemove(token!, out _);
                return null;
            }

            return session;
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
