using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace well404.WebPanel
{
    /// <summary>
    /// Resolves an administrator-installed cloudflared executable. Automatic binary downloads are
    /// deliberately unavailable until the plugin ships a pinned official release manifest with a
    /// per-platform SHA-256 allowlist. This prevents mutable release URLs, third-party mirrors, and
    /// unverified cached files from becoming an executable supply chain.
    /// </summary>
    internal static class CloudflaredDownloader
    {
        public static Task<string?> EnsureAsync(
            string command, bool autoDownload, IReadOnlyList<string>? mirrors, int attemptsPerSource,
            string workingDirectory, ILogger logger, CancellationToken cancellationToken)
        {
            command = string.IsNullOrWhiteSpace(command) ? "cloudflared" : command.Trim();
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult<string?>(null);
            }

            if (IsExecutableAvailable(command))
            {
                return Task.FromResult<string?>(command);
            }

            if (autoDownload)
            {
                logger.LogWarning(
                    "WebPanel: cloudflared automatic download is disabled because this build has no pinned "
                    + "official version and SHA-256 allowlist. Install cloudflared manually and set "
                    + "web.tunnel.command to its executable path. downloadMirrors/downloadAttempts are ignored.");
                return Task.FromResult<string?>(null);
            }

            // Preserve the existing manual-install diagnostic in ProcessTunnelProvider when the
            // feature is disabled and the configured command cannot currently be found.
            return Task.FromResult<string?>(command);
        }

        private static bool IsExecutableAvailable(string command)
        {
            if (command.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                command.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                return IsUsableFile(command) || (IsWindows() && IsUsableFile(command + ".exe"));
            }

            var pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathValue))
            {
                return false;
            }

            var extensions = IsWindows() ? new[] { string.Empty, ".exe", ".cmd", ".bat" } : new[] { string.Empty };
            foreach (var directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                foreach (var extension in extensions)
                {
                    try
                    {
                        if (IsUsableFile(Path.Combine(directory.Trim(), command + extension)))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Ignore malformed or inaccessible PATH entries.
                    }
                }
            }

            return false;
        }

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

        private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}
