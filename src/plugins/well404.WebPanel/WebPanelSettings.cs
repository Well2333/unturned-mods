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

        public int Port { get; set; } = 8080;

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
        /// at <c>/p</c>), e.g. <c>http://your-server-ip:8080</c>. Must be reachable from players'
        /// browsers. Empty = derive from <see cref="BindAddress"/> + <see cref="Port"/> (only
        /// works when the bind address is a routable address, not 127.0.0.1 / 0.0.0.0). When no
        /// usable base URL is available, the player-link feature is simply disabled.
        /// </summary>
        public string PublicBaseUrl { get; set; } = string.Empty;

        /// <summary>How long a player web-session link stays valid, in minutes.</summary>
        public int PlayerSessionMinutes { get; set; } = 5;
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
    }
}
