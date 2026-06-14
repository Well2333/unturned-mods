using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace well404.WebPanel
{
    /// <summary>
    /// Extension point for "expose the panel through an outbound tunnel / reverse proxy" backends.
    /// An implementation brings up a tunnel to the local panel port and reports the public base URL
    /// players' browsers can reach (e.g. <c>https://abc.trycloudflare.com</c>); it is stopped when
    /// the plugin unloads. The shipped <see cref="ProcessTunnelProvider"/> is config-driven and
    /// covers cloudflared / ngrok / most CLI tunnels; add another implementation to integrate a
    /// different proxy (a hosted FRP, an SDK-based tunnel, …) without touching the rest of the panel.
    /// </summary>
    public interface ITunnelProvider : IDisposable
    {
        /// <summary>
        /// Brings the tunnel up against <paramref name="localPort"/> and returns the public base URL
        /// (no trailing slash), or null if none was obtained before the configured timeout.
        /// </summary>
        Task<string?> StartAsync(int localPort, CancellationToken cancellationToken);

        /// <summary>Whether the tunnel backend is still running (e.g. its child process is alive).</summary>
        bool IsRunning { get; }

        /// <summary>Tears the tunnel down (kills the child process, etc.). Safe to call repeatedly.</summary>
        void Stop();
    }

    /// <summary>
    /// A generic tunnel provider that launches an <b>admin-installed</b> CLI tool (it never bundles
    /// or downloads a binary) and discovers the public URL one of two ways:
    /// <list type="bullet">
    /// <item>by matching <see cref="TunnelSettings.UrlPattern"/> against the tool's stdout/stderr; or</item>
    /// <item>if <see cref="TunnelSettings.ApiUrl"/> is set, by polling that local JSON endpoint
    /// (e.g. ngrok's <c>http://127.0.0.1:4040/api/tunnels</c>) and matching the pattern there.</item>
    /// </list>
    /// </summary>
    public sealed class ProcessTunnelProvider : ITunnelProvider
    {
        private readonly TunnelSettings m_Settings;
        private readonly ILogger m_Logger;
        private readonly Regex m_UrlRegex;

        private Process? m_Process;
        private readonly TaskCompletionSource<string?> m_Url =
            new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProcessTunnelProvider(TunnelSettings settings, ILogger logger)
        {
            m_Settings = settings;
            m_Logger = logger;
            var pattern = string.IsNullOrWhiteSpace(settings.UrlPattern)
                ? "https://[^\\s\"']+"
                : settings.UrlPattern;
            m_UrlRegex = new Regex(pattern, RegexOptions.IgnoreCase);
        }

        /// <summary>True while the child process has been started and has not exited.</summary>
        public bool IsRunning
        {
            get
            {
                var process = m_Process;
                if (process == null)
                {
                    return false;
                }

                try
                {
                    return !process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<string?> StartAsync(int localPort, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(m_Settings.Command))
            {
                m_Logger.LogWarning("WebPanel: tunnel enabled but web.tunnel.command is empty; skipping.");
                return null;
            }

            var args = (m_Settings.Args ?? string.Empty).Replace("{port}", localPort.ToString());
            var psi = new ProcessStartInfo
            {
                FileName = m_Settings.Command,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            m_Process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            m_Process.OutputDataReceived += (sender, e) => InspectLine(e.Data);
            m_Process.ErrorDataReceived += (sender, e) => InspectLine(e.Data);
            m_Process.Exited += (sender, e) => m_Url.TrySetResult(null);

            m_Process.Start();
            m_Process.BeginOutputReadLine();
            m_Process.BeginErrorReadLine();

            // If a local API is exposed (ngrok), poll it in parallel — its URL is more reliable
            // than scraping logs.
            if (!string.IsNullOrWhiteSpace(m_Settings.ApiUrl))
            {
                _ = PollApiAsync(cancellationToken);
            }

            var timeout = m_Settings.ReadyTimeoutSeconds > 0 ? m_Settings.ReadyTimeoutSeconds : 30;
            var completed = await Task.WhenAny(m_Url.Task, Task.Delay(TimeSpan.FromSeconds(timeout), cancellationToken))
                .ConfigureAwait(false);

            if (completed == m_Url.Task)
            {
                return m_Url.Task.Result;
            }

            return null;
        }

        private void InspectLine(string? line)
        {
            if (string.IsNullOrEmpty(line) || m_Url.Task.IsCompleted)
            {
                return;
            }

            var match = m_UrlRegex.Match(line);
            if (match.Success)
            {
                m_Url.TrySetResult(match.Value.TrimEnd('/'));
            }
        }

        private async Task PollApiAsync(CancellationToken cancellationToken)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var deadline = m_Settings.ReadyTimeoutSeconds > 0 ? m_Settings.ReadyTimeoutSeconds : 30;
            for (var elapsed = 0; elapsed < deadline && !m_Url.Task.IsCompleted; elapsed++)
            {
                try
                {
                    var body = await http.GetStringAsync(m_Settings.ApiUrl).ConfigureAwait(false);
                    var match = m_UrlRegex.Match(body);
                    if (match.Success)
                    {
                        m_Url.TrySetResult(match.Value.TrimEnd('/'));
                        return;
                    }
                }
                catch
                {
                    // The API may not be up yet; keep polling until the deadline.
                }

                try
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public void Stop()
        {
            m_Url.TrySetResult(null);
            var process = m_Process;
            m_Process = null;
            if (process == null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Already gone or not killable; nothing more to do.
            }
            finally
            {
                process.Dispose();
            }
        }

        public void Dispose() => Stop();
    }
}
