using System;

namespace Server
{
    public static class ServerConfig
    {
        public static int BaseHealthRes { get; private set; } = 5;
        public static int BaseManaRes { get; private set; } = 3;
        public static double RegenTickIntervalSeconds { get; private set; } = 1.0;

        public static string GameVersion { get; private set; } = "0.1.0-alpha";

        // RPG Progression
        public static int ExpBase { get; private set; } = 100;
        public static double ExpGrowthRate { get; private set; } = 1.5;
        public static int StatPointsPerLevel { get; private set; } = 5;

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

            if (int.TryParse(Environment.GetEnvironmentVariable("EXP_BASE"), out int expBase))
            {
                ExpBase = expBase;
            }

            if (double.TryParse(Environment.GetEnvironmentVariable("EXP_GROWTH_RATE"), System.Globalization.CultureInfo.InvariantCulture, out double expGrowthRate))
            {
                ExpGrowthRate = expGrowthRate;
            }

            if (int.TryParse(Environment.GetEnvironmentVariable("STAT_POINTS_PER_LEVEL"), out int statPoints))
            {
                StatPointsPerLevel = statPoints;
            }
            
            Console.WriteLine($"[Config] Running Version: {GameVersion} | Regen: {BaseHealthRes} HP / {BaseManaRes} MP every {RegenTickIntervalSeconds}s | EXP Base: {ExpBase}, Growth: {ExpGrowthRate}, StatPoints/Lvl: {StatPointsPerLevel}");
        }

        public static long GetExpForNextLevel(int currentLevel)
        {
            if (currentLevel < 1) currentLevel = 1;
            return (long)Math.Floor(ExpBase * Math.Pow(currentLevel, ExpGrowthRate));
        }
    }
}
