using System;
using System.IO;
using SDG.Unturned;

namespace well404.AutoSave
{
    /// <summary>
    /// Resolved filesystem locations for the running server. <see cref="Capture"/> reads the Unturned
    /// statics (so it MUST run on the main thread); the resulting immutable holder can then be used
    /// from background threads (e.g. the backup worker) without touching any game API.
    /// </summary>
    public sealed class SavePaths
    {
        public SavePaths(string installRoot, string serverId, string savedataRoot)
        {
            InstallRoot = installRoot ?? throw new ArgumentNullException(nameof(installRoot));
            ServerId = serverId ?? throw new ArgumentNullException(nameof(serverId));
            SavedataRoot = savedataRoot ?? throw new ArgumentNullException(nameof(savedataRoot));
        }

        /// <summary>The Unturned install root (<c>ReadWrite.PATH</c>).</summary>
        public string InstallRoot { get; }

        /// <summary>The current server id (<c>Provider.serverID</c>).</summary>
        public string ServerId { get; }

        /// <summary>Absolute path of this server's savedata folder (<c>&lt;install&gt;/Servers/&lt;id&gt;</c>).</summary>
        public string SavedataRoot { get; }

        /// <summary>Reads the game statics. Call once the server id is known.</summary>
        public static SavePaths Capture()
        {
            var install = ReadWrite.PATH;
            var serverId = Provider.serverID;

            // ServerSavedata.directory is the SERVERS CONTAINER ("/Servers"), not the per-server
            // folder, so the running server's savedata is "<container>/<serverId>". Guard against an
            // Unturned build whose directory already ends with the id (then don't append it twice).
            var container = (ServerSavedata.directory ?? "/Servers").Replace('\\', '/').TrimEnd('/');
            var combined = !string.IsNullOrEmpty(serverId)
                && !container.EndsWith("/" + serverId, StringComparison.OrdinalIgnoreCase)
                    ? container + "/" + serverId
                    : container;
            var savedataRoot = Path.GetFullPath(install + combined);
            return new SavePaths(install, serverId, savedataRoot);
        }

        /// <summary>
        /// Resolves the backup output directory: empty config = <c>&lt;install&gt;/Backups/&lt;id&gt;</c>;
        /// an absolute path is used as-is; a relative path is taken relative to the install root.
        /// </summary>
        public string ResolveBackupDirectory(string? configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(Path.Combine(InstallRoot, "Backups", ServerId));
            }

            var trimmed = configured.Trim();
            return Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(InstallRoot, trimmed));
        }
    }
}
