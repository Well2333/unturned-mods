using System;
using Cronos;

namespace well404.AutoSave
{
    /// <summary>
    /// A parsed cron expression evaluated in a fixed time zone. Pure (no game/IO), so it is unit
    /// tested directly. Wraps <see cref="CronExpression"/> from Cronos; saves fire on the wall-clock
    /// boundaries the expression describes, independent of when the server started.
    /// </summary>
    public sealed class CronSchedule
    {
        private readonly CronExpression m_Expression;
        private readonly TimeZoneInfo m_TimeZone;

        public CronSchedule(string cron, TimeZoneInfo timeZone)
        {
            m_Expression = CronExpression.Parse(cron ?? throw new ArgumentNullException(nameof(cron)));
            m_TimeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
        }

        /// <summary>
        /// Validates a 5-field cron expression. Returns null on success or a short error message.
        /// </summary>
        public static string? Validate(string cron)
        {
            if (string.IsNullOrWhiteSpace(cron))
            {
                return "Enter a cron expression.";
            }

            try
            {
                CronExpression.Parse(cron);
                return null;
            }
            catch (CronFormatException ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// The next occurrence strictly after <paramref name="fromUtc"/> (which must be UTC), as UTC,
        /// or null if the expression never fires again (e.g. an impossible date).
        /// </summary>
        public DateTime? GetNextOccurrenceUtc(DateTime fromUtc)
        {
            if (fromUtc.Kind != DateTimeKind.Utc)
            {
                fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
            }

            return m_Expression.GetNextOccurrence(fromUtc, m_TimeZone, inclusive: false);
        }
    }
}
