using System;

namespace Server
{
    public static class ServerConfig
    {
        public static int BaseHealthRes { get; private set; } = 5;
        public static int BaseManaRes { get; private set; } = 3;
        public static double RegenTickIntervalSeconds { get; private set; } = 1.0;

        public static string GameVersion { get; private set; } = "0.1.0-alpha";

        public static void Initialize()
        {
            var versionEnv = Environment.GetEnvironmentVariable("GAME_VERSION");
            if (!string.IsNullOrEmpty(versionEnv))
            {
                GameVersion = versionEnv;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("BASE_HEALTH_RES"), out int hpRes))
            {
                BaseHealthRes = hpRes;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("BASE_MANA_RES"), out int mpRes))
            {
                BaseManaRes = mpRes;
            }

            if (double.TryParse(Environment.GetEnvironmentVariable("REGEN_TICK_INTERVAL_SECONDS"), out double tickInterval))
            {
                RegenTickIntervalSeconds = tickInterval;
            }
            
            Console.WriteLine($"[Config] Running Version: {GameVersion} | Regen: {BaseHealthRes} HP / {BaseManaRes} MP every {RegenTickIntervalSeconds}s");
        }
    }
}
