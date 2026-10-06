using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Server.World
{
    /// <summary>
    /// Rate-limits repeated log lines. A fault inside the 30 TPS loop would otherwise print 30 times a second
    /// and bury everything else. Each key logs at most once per interval.
    /// </summary>
    public static class LogThrottle
    {
        private static readonly ConcurrentDictionary<string, long> _last = new();

        public static bool ShouldLog(string key, double intervalSeconds = 5.0)
        {
            long now = Stopwatch.GetTimestamp();
            long interval = (long)(intervalSeconds * Stopwatch.Frequency);

            while (true)
            {
                if (!_last.TryGetValue(key, out long last))
                {
                    if (_last.TryAdd(key, now)) return true;
                    continue;
                }

                if (now - last < interval) return false;
                if (_last.TryUpdate(key, now, last)) return true;
            }
        }

        /// <summary>Writes the line to the console unless the same key already logged within the interval.</summary>
        public static void Warn(string key, string message, double intervalSeconds = 5.0)
        {
            if (ShouldLog(key, intervalSeconds)) Console.WriteLine(message);
        }
    }
}
