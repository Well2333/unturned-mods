using System.Collections.Generic;

namespace well404.WebPanel
{
    /// <summary>Strongly-typed view of the WebPanel <c>config.yaml</c>.</summary>
    public class WebPanelSettings
    {
        public WebServerSettings Web { get; set; } = new WebServerSettings();
    }

    public class WebServerSettings
    {
        /// <summary>
        /// Interface to listen on: <c>127.0.0.1</c> (loopback only, default),
        /// <c>0.0.0.0</c> (all interfaces) or a specific NIC address (e.g. <c>192.168.1.10</c>).
        /// </summary>
        public string BindAddress { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 27020;

        /// <summary>
        /// Secret admin token. The admin panel is always served under this token as a path
        /// prefix (<c>http://host:port/&lt;token&gt;/</c>); the token IS the auth, so a wrong/absent
        /// one is a plain 404. Leave empty to have the plugin mint a random token each run (logged
        /// at startup) — set a stable value here to keep the admin URL constant across restarts.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>Optional outbound tunnel / reverse proxy that exposes the panel publicly.</summary>
        public TunnelSettings Tunnel { get; set; } = new TunnelSettings();

        /// <summary>
        /// Public base URL given to players in the in-game shop/gift links (the player surface
        /// at <c>/p</c>), e.g. <c>http://your-server-ip:27020</c>. Must be reachable from players'
        /// browsers. Empty = derive from <see cref="BindAddress"/> + <see cref="Port"/> (only
        /// works when the bind address is a routable address, not 127.0.0.1 / 0.0.0.0). When no
        /// usable base URL is available, the player-link feature is simply disabled.
        /// </summary>
        public string PublicBaseUrl { get; set; } = string.Empty;

        /// <summary>How long a player web-session link stays valid, in minutes.</summary>
        public int PlayerSessionMinutes { get; set; } = 5;

        /// <summary>
        /// Browser data refresh interval in seconds. The clients pause while hidden or editing;
        /// zero disables automatic refresh. Default is five seconds.
        /// </summary>
        public int RefreshIntervalSeconds { get; set; } = 5;

        /// <summary>Developer-only impersonation of a fixed player (see <see cref="DevPlayerSettings"/>).</summary>
        public DevPlayerSettings DevPlayer { get; set; } = new DevPlayerSettings();
    }

    /// <summary>
    /// A developer convenience for previewing the player surface (<c>/p</c>) without joining the
    /// game. When enabled, opening <c>/&lt;adminToken&gt;/dev-player</c> mints a long-lived session
    /// for the configured Steam ID and drops you straight into that player's panel — so menus can be
    /// inspected/debugged from a browser. It is gated behind the secret admin token AND this switch,
    /// and defaults off. Actions needing the player online (buy/sell, teleport) still report
    /// "must be online"; only the rendered views are previewable offline.
    /// </summary>
    public class DevPlayerSettings
    {
        /// <summary>Master switch. Off by default — this is impersonation, keep it off in production.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>The Steam ID to impersonate (the player whose panel you want to preview).</summary>
        public string SteamId { get; set; } = string.Empty;

        /// <summary>Display name shown in the previewed panel.</summary>
        public string DisplayName { get; set; } = "Dev Player";
    }

    /// <summary>
    /// Drives <see cref="ProcessTunnelProvider"/>: an admin-installed CLI tunnel that exposes the
    /// local panel port to the internet so neither the game host's real IP nor any inbound port
    /// needs to be opened. The plugin never bundles or downloads the binary — point it at one you
    /// installed (cloudflared, ngrok, …). Generic by design; see <see cref="ITunnelProvider"/>.
    /// </summary>
    public class TunnelSettings
    {
        /// <summary>Master switch. When false, no tunnel is launched (the default).</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Which backend to use: <c>cloudflare</c> (a Cloudflare Quick Tunnel via <c>cloudflared</c>
        /// — the args/url-pattern are preset, only <see cref="Command"/> is used as the binary path)
        /// or <c>custom</c> (you fully control <see cref="Command"/>/<see cref="Args"/>/
        /// <see cref="UrlPattern"/>/<see cref="ApiUrl"/>, e.g. for ngrok).
        /// </summary>
        public string Type { get; set; } = "cloudflare";

        /// <summary>The tunnel executable to run, e.g. <c>cloudflared</c> or <c>ngrok</c> (or a full path).</summary>
        public string Command { get; set; } = "cloudflared";

        /// <summary>
        /// Automatic download is disabled by default and unavailable in this build because no pinned
        /// official version/SHA-256 allowlist is bundled. Install cloudflared manually and set
        /// <see cref="Command"/> to its path. This compatibility switch is retained for old configs.
        /// </summary>
        public bool AutoDownload { get; set; } = false;

        /// <summary>
        /// Reserved for configuration compatibility. Ignored while verified auto-download is unavailable.
        /// </summary>
        public List<string> DownloadMirrors { get; set; } = new List<string>();

        /// <summary>Reserved for configuration compatibility; currently ignored.</summary>
        public int DownloadAttempts { get; set; } = 2;

        /// <summary>
        /// Arguments for <see cref="Command"/>. The literal <c>{port}</c> is replaced with the panel
        /// port. Default targets a Cloudflare Quick Tunnel; for ngrok use <c>http {port}</c>.
        /// </summary>
        public string Args { get; set; } = "tunnel --url http://127.0.0.1:{port}";

        /// <summary>
        /// Regex used to extract the public URL from the tool's output (or from <see cref="ApiUrl"/>).
        /// Defaults to a Cloudflare quick-tunnel pattern; empty falls back to the first https URL.
        /// </summary>
        public string UrlPattern { get; set; } = "https://[a-z0-9-]+\\.trycloudflare\\.com";

        /// <summary>
        /// Optional local JSON endpoint to poll for the URL instead of scraping logs. For ngrok set
        /// this to <c>http://127.0.0.1:4040/api/tunnels</c> (and an ngrok-flavoured UrlPattern).
        /// </summary>
        public string ApiUrl { get; set; } = string.Empty;

        /// <summary>How long to wait for the tunnel to report a public URL before giving up.</summary>
        public int ReadyTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Keep the tunnel alive: if the tunnel process exits, or its public URL stops responding,
        /// restart it and publish the fresh URL (a quick tunnel gets a NEW address each restart, so
        /// players must re-open <c>/menu</c> for fresh links). Default true. Without this a
        /// Cloudflare quick tunnel that drops after a few hours leaves the panel unreachable until a
        /// manual server restart.
        /// </summary>
        public bool AutoRestart { get; set; } = true;

        /// <summary>
        /// How often (seconds) to health-check the tunnel's public URL. A process exit is always
        /// detected immediately; this interval governs the HTTP reachability probe. 0 disables the
        /// probe (process-exit detection still works). Default 60. The probe self-disables if it can
        /// never reach the URL even once (e.g. no outbound HTTPS), so it never causes a restart loop.
        /// </summary>
        public int HealthCheckSeconds { get; set; } = 60;
    }
}
