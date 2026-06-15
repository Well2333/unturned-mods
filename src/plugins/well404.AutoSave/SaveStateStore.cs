using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace well404.AutoSave
{
    /// <summary>
    /// Persists the running save counter (how many scheduled saves have happened) in a tiny text file
    /// in the plugin's working directory, so the "back up every Nth save" cadence survives restarts.
    /// </summary>
    public sealed class SaveStateStore
    {
        private readonly string m_Path;
        private readonly ILogger m_Logger;
        private readonly object m_Lock = new object();

        public SaveStateStore(string workingDirectory, ILogger logger)
        {
            m_Path = Path.Combine(workingDirectory, "save-count.txt");
            m_Logger = logger;
        }

        public long Read()
        {
            lock (m_Lock)
            {
                try
                {
                    if (File.Exists(m_Path)
                        && long.TryParse(File.ReadAllText(m_Path).Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var value)
                        && value >= 0)
                    {
                        return value;
                    }
                }
                catch (Exception ex)
                {
                    m_Logger.LogWarning(ex, "Auto Save: could not read the save counter; starting from 0.");
                }

                return 0;
            }
        }

        public void Write(long value)
        {
            lock (m_Lock)
            {
                try
                {
                    File.WriteAllText(m_Path, value.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception ex)
                {
                    m_Logger.LogWarning(ex, "Auto Save: could not persist the save counter.");
                }
            }
        }
    }
}
