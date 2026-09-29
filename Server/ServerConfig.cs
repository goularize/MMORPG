using System;

namespace Server
{
    public static class ServerConfig
    {
        public static int BaseHealthRes { get; private set; } = 5;
        public static int BaseManaRes { get; private set; } = 3;
        public static double RegenTickIntervalSeconds { get; private set; } = 1.0;

        public static void Initialize()
        {
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
            
            Console.WriteLine($"[Config] Regen loaded: {BaseHealthRes} HP / {BaseManaRes} MP every {RegenTickIntervalSeconds}s");
        }
    }
}
