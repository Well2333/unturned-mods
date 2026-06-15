using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace well404.WebPanel
{
    /// <summary>
    /// Makes a runnable <c>cloudflared</c> available for the Cloudflare Quick Tunnel without ever
    /// touching the host system. Resolution order:
    /// <list type="number">
    /// <item>the admin-configured command, if it exists on disk or is found on <c>PATH</c>;</item>
    /// <item>a portable copy downloaded on an earlier run into the plugin's data directory;</item>
    /// <item>(when <c>web.tunnel.autoDownload</c> is on) a fresh download of the latest official
    /// release into that same directory, trying each configured mirror in turn with retries.</item>
    /// </list>
    /// The portable binary lives only under the plugin's working directory — it is NEVER installed
    /// system-wide or added to <c>PATH</c>, so removing the plugin's data folder removes it too.
    /// Downloads honour the system / environment HTTP(S) proxy.
    /// </summary>
    internal static class CloudflaredDownloader
    {
        /// <summary>Sub-folder of the plugin working directory that holds the portable binary.</summary>
        private const string SubDirectory = "cloudflared";

        /// <summary>Canonical "latest release asset" URL; <c>{asset}</c> is the platform file name.</summary>
        private const string CanonicalLatest =
            "https://github.com/cloudflare/cloudflared/releases/latest/download/{asset}";

        /// <summary>
        /// Built-in default download sources, tried top to bottom (first that works wins). GitHub
        /// release proxies first (reachable where github.com is blocked/slow), direct GitHub last.
        /// Each entry may be a full template containing <c>{asset}</c>, or a bare prefix that gets the
        /// canonical GitHub release URL appended. Overridable via <c>web.tunnel.downloadMirrors</c>.
        /// NOTE: jsDelivr (cdn.jsdelivr.net) only mirrors a repo's committed files, NOT release
        /// binaries, so it cannot serve cloudflared and is deliberately not a default.
        /// </summary>
        private static readonly string[] DefaultMirrors =
        {
            "https://ghproxy.net/",
            "https://gh-proxy.com/",
            "https://mirror.ghproxy.com/",
            "https://ghproxy.com/",
            "", // direct github.com
        };

        /// <summary>
        /// Returns the cloudflared command/path the tunnel should run, or <c>null</c> if no usable
        /// binary could be found or downloaded. Never throws. When <paramref name="autoDownload"/> is
        /// off and the configured command is not found, the command is returned unchanged so the
        /// caller still surfaces the existing "is it installed?" diagnostic.
        /// </summary>
        public static async Task<string?> EnsureAsync(
            string command, bool autoDownload, IReadOnlyList<string>? mirrors, int attemptsPerSource,
            string workingDirectory, ILogger logger, CancellationToken cancellationToken)
        {
            command = string.IsNullOrWhiteSpace(command) ? "cloudflared" : command.Trim();

            // (1) The configured command is already runnable (explicit path, or on PATH) — use it.
            if (IsExecutableAvailable(command))
            {
                return command;
            }

            // (2) A portable copy from a previous run? Reuse it (no re-download).
            var portable = Path.Combine(workingDirectory, SubDirectory, BinaryFileName());
            if (IsUsableFile(portable))
            {
                logger.LogInformation("WebPanel: using the portable cloudflared at {Path}.", portable);
                return portable;
            }

            if (!autoDownload)
            {
                // Auto-download disabled: let the caller try `command` and log the friendly error.
                return command;
            }

            var asset = ResolveAssetName();
            if (asset == null)
            {
                logger.LogWarning(
                    "WebPanel: cloudflared not found and auto-download does not support this platform "
                    + "({OS} / {Arch}). Install cloudflared manually and set web.tunnel.command, or use "
                    + "tunnel type: custom.", RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture);
                return null;
            }

            var urls = BuildCandidateUrls(asset, mirrors);
            var attempts = attemptsPerSource > 0 ? attemptsPerSource : 2;

            logger.LogInformation(
                "WebPanel: cloudflared not found — downloading a portable copy into {Dir} (NOT installed "
                + "system-wide). Trying {Sources} source(s), {Attempts} attempt(s) each.",
                Path.GetDirectoryName(portable), urls.Count, attempts);

            for (var s = 0; s < urls.Count; s++)
            {
                var url = urls[s];
                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }

                    try
                    {
                        logger.LogInformation(
                            "WebPanel: cloudflared download — source {S}/{N} attempt {A}/{Attempts}: {Url}",
                            s + 1, urls.Count, attempt, attempts, url);
                        await DownloadAsync(url, portable, cancellationToken).ConfigureAwait(false);
                        MakeExecutable(portable, logger);
                        logger.LogInformation("WebPanel: portable cloudflared ready at {Path} (from {Url}).", portable, url);
                        return portable;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            "WebPanel: cloudflared download failed (source {S}/{N} attempt {A}/{Attempts}, {Url}): {Err}",
                            s + 1, urls.Count, attempt, attempts, url, ex.Message);
                    }
                }
            }

            logger.LogWarning(
                "WebPanel: could not download cloudflared from any of {N} source(s). Set a reachable mirror in "
                + "web.tunnel.downloadMirrors, configure a system/HTTPS_PROXY proxy, install cloudflared manually "
                + "and set web.tunnel.command, or disable the tunnel (web.tunnel.enabled: false).", urls.Count);
            return null;
        }

        /// <summary>Expands the mirror list (or built-in defaults) into concrete asset URLs.</summary>
        private static List<string> BuildCandidateUrls(string asset, IReadOnlyList<string>? mirrors)
        {
            var sources = new List<string>();
            if (mirrors != null)
            {
                foreach (var m in mirrors)
                {
                    if (!string.IsNullOrWhiteSpace(m))
                    {
                        sources.Add(m.Trim());
                    }
                }
            }

            if (sources.Count == 0)
            {
                sources.AddRange(DefaultMirrors);
            }

            var urls = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in sources)
            {
                string url;
                if (src.Length == 0)
                {
                    url = CanonicalLatest.Replace("{asset}", asset);
                }
                else if (src.IndexOf("{asset}", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    url = src.Replace("{asset}", asset);
                }
                else
                {
                    // Bare prefix style, e.g. "https://ghproxy.com/" — prepend to the canonical URL.
                    url = src + CanonicalLatest.Replace("{asset}", asset);
                }

                if (seen.Add(url))
                {
                    urls.Add(url);
                }
            }

            return urls;
        }

        /// <summary>True if <paramref name="command"/> can be launched: an existing file when it is a
        /// path, otherwise a hit while scanning <c>PATH</c> (with Windows executable extensions).</summary>
        private static bool IsExecutableAvailable(string command)
        {
            if (command.IndexOf(Path.DirectorySeparatorChar) >= 0
                || command.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                return IsUsableFile(command) || (IsWindows() && IsUsableFile(command + ".exe"));
            }

            var pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar))
            {
                return false;
            }

            var extensions = IsWindows() ? new[] { string.Empty, ".exe", ".cmd", ".bat" } : new[] { string.Empty };
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                foreach (var ext in extensions)
                {
                    try
                    {
                        if (IsUsableFile(Path.Combine(dir.Trim(), command + ext)))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // A malformed PATH entry (illegal chars, etc.) — skip it.
                    }
                }
            }

            return false;
        }

        /// <summary>A file that exists and is non-empty (guards against a half-written download).</summary>
        private static bool IsUsableFile(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists && info.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string BinaryFileName() => IsWindows() ? "cloudflared.exe" : "cloudflared";

        /// <summary>Maps the running OS/arch to its official cloudflared asset file name, or null if
        /// unsupported (macOS ships a <c>.tgz</c> that needs extraction and is not auto-downloaded).</summary>
        private static string? ResolveAssetName()
        {
            if (IsWindows())
            {
                switch (RuntimeInformation.OSArchitecture)
                {
                    case Architecture.X86:
                        return "cloudflared-windows-386.exe";
                    // cloudflared ships no native windows-arm64 build; amd64 runs under emulation.
                    case Architecture.X64:
                    case Architecture.Arm64:
                        return "cloudflared-windows-amd64.exe";
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                switch (RuntimeInformation.OSArchitecture)
                {
                    case Architecture.X64:
                        return "cloudflared-linux-amd64";
                    case Architecture.X86:
                        return "cloudflared-linux-386";
                    case Architecture.Arm64:
                        return "cloudflared-linux-arm64";
                    case Architecture.Arm:
                        return "cloudflared-linux-arm";
                }
            }

            return null;
        }

        /// <summary>Streams <paramref name="url"/> to <paramref name="destination"/> via a temp file,
        /// then atomically moves it into place so a reused copy is never partially written. Honours the
        /// system / environment HTTP(S) proxy.</summary>
        private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
        {
            var dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = destination + ".download";

            var handler = new HttpClientHandler();
            var proxy = ResolveProxyFromEnvironment();
            if (proxy != null)
            {
                handler.UseProxy = true;
                handler.Proxy = proxy;
            }
            // else: handler.UseProxy defaults to true and picks up the system proxy (Windows/WinHTTP).

            using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("well404.WebPanel");
                using var response = await http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(file, 81920, cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temp).Length == 0)
            {
                try { File.Delete(temp); } catch { /* best effort */ }
                throw new IOException("downloaded file was empty");
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(temp, destination);
        }

        /// <summary>Reads a proxy from the usual environment variables, or null to use the system proxy.</summary>
        private static IWebProxy? ResolveProxyFromEnvironment()
        {
            foreach (var name in new[] { "HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy" })
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
                {
                    return new WebProxy(uri);
                }
            }

            return null;
        }

        /// <summary>On Unix, marks the downloaded binary executable (<c>chmod +x</c>). No-op on Windows.</summary>
        private static void MakeExecutable(string path, ILogger logger)
        {
            if (IsWindows())
            {
                return;
            }

            try
            {
                var psi = new ProcessStartInfo("/bin/chmod", "+x \"" + path + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(psi);
                process?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "WebPanel: could not mark {Path} executable (chmod +x). If the tunnel fails to start, run "
                    + "'chmod +x' on it manually.", path);
            }
        }

        private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}
