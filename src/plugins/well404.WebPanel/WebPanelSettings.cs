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
        /// Access token required in the <c>X-Web-Token</c> header (or <c>?token=</c>) for
        /// every <c>/api</c> call. <b>Mandatory</b> when <see cref="BindAddress"/> is not
        /// loopback: if it is empty there, the server downgrades to <c>127.0.0.1</c> unless
        /// <see cref="AllowInsecurePublic"/> is set. Empty + loopback = no auth (local only).
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Advanced/hidden escape hatch: allow serving on a non-loopback address with no
        /// token. Insecure — anyone who can reach the port gets full admin access.
        /// </summary>
        public bool AllowInsecurePublic { get; set; } = false;
    }
}
