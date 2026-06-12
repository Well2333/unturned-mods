using System;
using System.Globalization;

namespace well404.Essentials.Gift
{
    /// <summary>
    /// A tiny standard 5-field crontab evaluator (minute hour day-of-month month day-of-week),
    /// hand-rolled so the plugin ships no extra NuGet dependency. Each field supports
    /// <c>*</c>, <c>*/step</c>, ranges <c>a-b</c>, steps <c>a-b/step</c>, and comma lists.
    /// Day-of-week is 0–6 (Sunday = 0; 7 also accepted for Sunday).
    /// <para>
    /// When both day-of-month and day-of-week are restricted (neither is <c>*</c>), a time
    /// matches if <b>either</b> matches — the standard Vixie-cron rule.
    /// </para>
    /// Evaluation is pure (the caller passes "now"), which keeps it unit-testable.
    /// </summary>
    public sealed class CronSchedule
    {
        private readonly bool[] m_Minutes;   // 0-59
        private readonly bool[] m_Hours;     // 0-23
        private readonly bool[] m_Days;      // 1-31
        private readonly bool[] m_Months;    // 1-12
        private readonly bool[] m_DaysOfWeek; // 0-6
        private readonly bool m_DomRestricted;
        private readonly bool m_DowRestricted;

        private CronSchedule(
            bool[] minutes, bool[] hours, bool[] days, bool[] months, bool[] daysOfWeek,
            bool domRestricted, bool dowRestricted)
        {
            m_Minutes = minutes;
            m_Hours = hours;
            m_Days = days;
            m_Months = months;
            m_DaysOfWeek = daysOfWeek;
            m_DomRestricted = domRestricted;
            m_DowRestricted = dowRestricted;
        }

        /// <summary>Parses a 5-field cron expression. Returns null if it is empty or malformed.</summary>
        public static CronSchedule? TryParse(string? expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return null;
            }

            var fields = expression!.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 5)
            {
                return null;
            }

            var minutes = ParseField(fields[0], 0, 59, out var minOk);
            var hours = ParseField(fields[1], 0, 23, out var hourOk);
            var days = ParseField(fields[2], 1, 31, out var dayOk);
            var months = ParseField(fields[3], 1, 12, out var monthOk);
            var dow = ParseDayOfWeekField(fields[4], out var dowOk);
            if (!minOk || !hourOk || !dayOk || !monthOk || !dowOk)
            {
                return null;
            }

            return new CronSchedule(
                minutes, hours, days, months, dow,
                domRestricted: fields[2].Trim() != "*",
                dowRestricted: fields[4].Trim() != "*");
        }

        /// <summary>True if <paramref name="time"/> (to the minute) matches the schedule.</summary>
        public bool Matches(DateTime time)
        {
            if (!m_Minutes[time.Minute] || !m_Hours[time.Hour] || !m_Months[time.Month])
            {
                return false;
            }

            var domMatch = m_Days[time.Day];
            var dowMatch = m_DaysOfWeek[(int)time.DayOfWeek];

            // Vixie rule: if both DOM and DOW are restricted, either matching is enough;
            // otherwise both (the unrestricted one is always true) must match.
            if (m_DomRestricted && m_DowRestricted)
            {
                return domMatch || dowMatch;
            }

            return domMatch && dowMatch;
        }

        /// <summary>
        /// The latest scheduled time at or before <paramref name="from"/> (truncated to the
        /// minute), searching back up to ~13 months. Null if none is found in that window.
        /// </summary>
        public DateTime? GetPreviousOccurrence(DateTime from)
        {
            var cursor = Truncate(from);
            var limit = cursor.AddMonths(-13);
            while (cursor >= limit)
            {
                if (Matches(cursor))
                {
                    return cursor;
                }

                cursor = cursor.AddMinutes(-1);
            }

            return null;
        }

        /// <summary>
        /// The next scheduled time strictly after <paramref name="from"/> (truncated to the
        /// minute), searching forward up to ~13 months. Null if none is found in that window.
        /// </summary>
        public DateTime? GetNextOccurrence(DateTime from)
        {
            var cursor = Truncate(from).AddMinutes(1);
            var limit = cursor.AddMonths(13);
            while (cursor <= limit)
            {
                if (Matches(cursor))
                {
                    return cursor;
                }

                cursor = cursor.AddMinutes(1);
            }

            return null;
        }

        private static DateTime Truncate(DateTime value)
            => new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

        private static bool[] ParseField(string field, int min, int max, out bool ok)
        {
            var set = new bool[max + 1];
            ok = true;
            foreach (var partRaw in field.Split(','))
            {
                var part = partRaw.Trim();
                if (part.Length == 0)
                {
                    ok = false;
                    return set;
                }

                int step = 1;
                var body = part;
                var slash = part.IndexOf('/');
                if (slash >= 0)
                {
                    body = part.Substring(0, slash);
                    if (!int.TryParse(part.Substring(slash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out step) || step <= 0)
                    {
                        ok = false;
                        return set;
                    }
                }

                int rangeStart, rangeEnd;
                if (body == "*")
                {
                    rangeStart = min;
                    rangeEnd = max;
                }
                else if (body.IndexOf('-') >= 0)
                {
                    var bounds = body.Split('-');
                    if (bounds.Length != 2
                        || !int.TryParse(bounds[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeStart)
                        || !int.TryParse(bounds[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeEnd))
                    {
                        ok = false;
                        return set;
                    }
                }
                else
                {
                    if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out rangeStart))
                    {
                        ok = false;
                        return set;
                    }

                    rangeEnd = rangeStart;
                }

                if (rangeStart < min || rangeEnd > max || rangeStart > rangeEnd)
                {
                    ok = false;
                    return set;
                }

                for (var value = rangeStart; value <= rangeEnd; value += step)
                {
                    set[value] = true;
                }
            }

            return set;
        }

        /// <summary>Day-of-week field: like a normal field but folds 7 into 0 (both = Sunday).</summary>
        private static bool[] ParseDayOfWeekField(string field, out bool ok)
        {
            // Parse against 0-7, then fold 7 -> 0.
            var raw = ParseField(field, 0, 7, out ok);
            var set = new bool[7];
            if (!ok)
            {
                return set;
            }

            for (var i = 0; i <= 7; i++)
            {
                if (raw[i])
                {
                    set[i % 7] = true;
                }
            }

            return set;
        }
    }
}
