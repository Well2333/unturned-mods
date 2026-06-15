using System;
using System.Diagnostics;
using System.IO;
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
    /// release into that same directory.</item>
    /// </list>
    /// The portable binary lives only under the plugin's working directory — it is NEVER installed
    /// system-wide or added to <c>PATH</c>, so removing the plugin's data folder removes it too.
    /// On any failure the original command is returned unchanged, so the caller still surfaces the
    /// existing "is it installed?" diagnostic and the panel keeps working locally.
    /// </summary>
    internal static class CloudflaredDownloader
    {
        /// <summary>Sub-folder of the plugin working directory that holds the portable binary.</summary>
        private const string SubDirectory = "cloudflared";

        /// <summary>Official release channel; <c>latest/download</c> always 302s to the newest asset.</summary>
        private const string DownloadBase = "https://github.com/cloudflare/cloudflared/releases/latest/download/";

        /// <summary>
        /// Returns the cloudflared command/path the tunnel should run. Downloads a portable copy when
        /// the configured command is missing and <paramref name="autoDownload"/> is enabled. Never
        /// throws and never returns null — falls back to <paramref name="command"/> on any problem.
        /// </summary>
        public static async Task<string> EnsureAsync(
            string command, bool autoDownload, string workingDirectory, ILogger logger, CancellationToken cancellationToken)
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

            var url = ResolveDownloadUrl();
            if (url == null)
            {
                logger.LogWarning(
                    "WebPanel: cloudflared not found and auto-download does not support this platform "
                    + "({OS} / {Arch}). Install cloudflared manually and set web.tunnel.command, or use "
                    + "tunnel type: custom.", RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture);
                return command;
            }

            try
            {
                logger.LogInformation(
                    "WebPanel: cloudflared not found — downloading a portable copy into {Dir} (NOT installed "
                    + "system-wide) from {Url}.", Path.GetDirectoryName(portable), url);
                await DownloadAsync(url, portable, cancellationToken).ConfigureAwait(false);
                MakeExecutable(portable, logger);
                logger.LogInformation("WebPanel: portable cloudflared ready at {Path}.", portable);
                return portable;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "WebPanel: failed to download a portable cloudflared from {Url}. Install it manually and set "
                    + "web.tunnel.command, or disable the tunnel (web.tunnel.enabled: false).", url);
                return command;
            }
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

        /// <summary>Maps the running OS/arch to its official cloudflared asset, or null if unsupported
        /// (macOS ships a <c>.tgz</c> that needs extraction and is intentionally not auto-downloaded).</summary>
        private static string? ResolveDownloadUrl()
        {
            string? asset = null;

            if (IsWindows())
            {
                switch (RuntimeInformation.OSArchitecture)
                {
                    case Architecture.X86:
                        asset = "cloudflared-windows-386.exe";
                        break;
                    // cloudflared ships no native windows-arm64 build; amd64 runs under emulation.
                    case Architecture.X64:
                    case Architecture.Arm64:
                        asset = "cloudflared-windows-amd64.exe";
                        break;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                switch (RuntimeInformation.OSArchitecture)
                {
                    case Architecture.X64:
                        asset = "cloudflared-linux-amd64";
                        break;
                    case Architecture.X86:
                        asset = "cloudflared-linux-386";
                        break;
                    case Architecture.Arm64:
                        asset = "cloudflared-linux-arm64";
                        break;
                    case Architecture.Arm:
                        asset = "cloudflared-linux-arm";
                        break;
                }
            }

            return asset == null ? null : DownloadBase + asset;
        }

        /// <summary>Streams <paramref name="url"/> to <paramref name="destination"/> via a temp file,
        /// then atomically moves it into place so a reused copy is never partially written.</summary>
        private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
        {
            var dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var temp = destination + ".download";

            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
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

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(temp, destination);
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
