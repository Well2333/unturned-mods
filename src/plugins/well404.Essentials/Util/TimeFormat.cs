using System;

namespace well404.Essentials.Util
{
    /// <summary>Small helpers for rendering durations in player-facing messages.</summary>
    public static class TimeFormat
    {
        /// <summary>Rounds a seconds value up and renders it compactly (e.g. <c>1h 5m</c>, <c>42s</c>).</summary>
        public static string Humanize(double seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }

            var span = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            if (span.TotalDays >= 1)
            {
                return span.Hours > 0
                    ? $"{(int)span.TotalDays}d {span.Hours}h"
                    : $"{(int)span.TotalDays}d";
            }

            if (span.TotalHours >= 1)
            {
                return span.Minutes > 0
                    ? $"{(int)span.TotalHours}h {span.Minutes}m"
                    : $"{(int)span.TotalHours}h";
            }

            if (span.TotalMinutes >= 1)
            {
                return span.Seconds > 0
                    ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
                    : $"{(int)span.TotalMinutes}m";
            }

            return $"{span.Seconds}s";
        }
    }
}
