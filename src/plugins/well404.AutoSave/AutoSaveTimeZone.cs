using System;

namespace well404.AutoSave
{
    /// <summary>Resolves the configured time-zone id (empty = local) to a <see cref="TimeZoneInfo"/>.</summary>
    public static class AutoSaveTimeZone
    {
        /// <summary>
        /// Resolves <paramref name="id"/> (empty/blank = the server's local zone). The id must match
        /// the host OS's naming (IANA on Linux, Windows ids on Windows). Returns false on an unknown id.
        /// </summary>
        public static bool TryResolve(string? id, out TimeZoneInfo zone)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                zone = TimeZoneInfo.Local;
                return true;
            }

            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
                return true;
            }
            catch (Exception)
            {
                zone = TimeZoneInfo.Local;
                return false;
            }
        }

        public static TimeZoneInfo Resolve(string? id) => TryResolve(id, out var zone) ? zone : TimeZoneInfo.Local;
    }
}
