using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace well404.AutoSave
{
    /// <summary>
    /// Drives a callback on the wall-clock boundaries of a <see cref="CronSchedule"/>. Long waits are
    /// chunked (so a far-future occurrence and clock changes are handled), and a tick that throws is
    /// logged without stopping the loop.
    /// </summary>
    public sealed class SchedulerLoop
    {
        private static readonly TimeSpan MaxSingleWait = TimeSpan.FromMinutes(60);

        private readonly ILogger m_Logger;

        public SchedulerLoop(ILogger logger)
        {
            m_Logger = logger;
        }

        public async Task RunAsync(CronSchedule schedule, Func<Task> onTick, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var next = schedule.GetNextOccurrenceUtc(DateTime.UtcNow);
                    if (next == null)
                    {
                        m_Logger.LogWarning("Auto Save: the cron expression has no future occurrences; scheduler stopping.");
                        return;
                    }

                    m_Logger.LogInformation(
                        "Auto Save: next save at {Time}.",
                        next.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));

                    try
                    {
                        await WaitUntilAsync(next.Value, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        await onTick().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        m_Logger.LogError(ex, "Auto Save: a scheduled save/backup tick failed.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Auto Save: the scheduler loop crashed and stopped.");
            }
        }

        private static async Task WaitUntilAsync(DateTime targetUtc, CancellationToken cancellationToken)
        {
            while (true)
            {
                var remaining = targetUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                // ConfigureAwait(false) is essential: the loop is started from the main thread, so
                // without it the continuation would try to resume on the Unity/UniTask main-thread
                // context (which does not pump plain Task continuations) and hang forever.
                await Task.Delay(remaining > MaxSingleWait ? MaxSingleWait : remaining, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
