using System;
using System.Globalization;

namespace Server
{
    public static class ServerConfig
    {
        // Defaults live here (not scattered in handlers); .env values override them.
        public static int ServerPort { get; private set; } = 7777;
        public static int DefaultCharacterSlots { get; private set; } = 3;

        public static int BaseHealthRes { get; private set; } = 5;
        public static int BaseManaRes { get; private set; } = 3;
        public static double RegenTickIntervalSeconds { get; private set; } = 1.0;
        public static double RegenCooldownSeconds { get; private set; } = 5.0;

        // Write-behind persistence
        public static double WriteBehindFlushSeconds { get; private set; } = 2.0;
        public static double AutosaveSeconds { get; private set; } = 60.0;
        public static int PersistenceWorkers { get; private set; } = 4;
        public static int PersistenceMaxAttempts { get; private set; } = 8;
        public static int ChatLogQueueSize { get; private set; } = 10000;

        // Combat logging: a disconnect while tagged leaves the character in the world for the linger period
        public static double CombatTagSeconds { get; private set; } = 10.0;
        public static double CombatLogoutLingerSeconds { get; private set; } = 15.0;

        /// <summary>How long a client may take to load the map and send WorldReadyRequest before it is disconnected (0 disables).</summary>
        public static double WorldReadyTimeoutSeconds { get; private set; } = 60.0;

        // Network -> game thread command queue
        public static int MaxPendingCommandsPerClient { get; private set; } = 128;
        public static double CommandBudgetMs { get; private set; } = 10.0;

        // Network I/O limits
        public static int MaxInboundPacketBytes { get; private set; } = 4096;
        public static double PacketCompletionTimeoutSeconds { get; private set; } = 10.0;
        public static double PreAuthIdleTimeoutSeconds { get; private set; } = 30.0;
        public static double SendTimeoutSeconds { get; private set; } = 5.0;

        // Abuse protection
        public static int SignInMaxFailures { get; private set; } = 5;
        public static double SignInFailureWindowSeconds { get; private set; } = 60.0;
        public static double SignInLockoutSeconds { get; private set; } = 60.0;
        public static int ChatBurst { get; private set; } = 5;
        public static double ChatMessagesPerSecond { get; private set; } = 1.0;

        public static string GameVersion { get; private set; } = Shared.Constants.GameRules.GameVersion;

        // RPG Progression
        public static int ExpBase { get; private set; } = 100;
        public static double ExpGrowthRate { get; private set; } = 1.5;
        public static int StatPointsPerLevel { get; private set; } = 5;

        // Inventory Configuration
        public static int DefaultBackpackSlots { get; private set; } = 20;

        // World Loot Satchels
        public static float LootOwnershipSeconds { get; private set; } = 30.0f;
        public static float LootDecaySeconds { get; private set; } = 120.0f;

        /// <summary>
        /// Locates the <c>.env</c> file regardless of where the server was started from: the working directory, then
        /// its <c>Server/</c> subfolder (running from the repo root), then every parent of the executable's folder
        /// (running from <c>bin/Debug/net8.0</c>). Returns null when there is none, so the environment and defaults apply.
        /// </summary>
        public static string? FindEnvFile(string workingDirectory, string baseDirectory)
        {
            string[] direct =
            {
                System.IO.Path.Combine(workingDirectory, ".env"),
                System.IO.Path.Combine(workingDirectory, "Server", ".env"),
            };
            foreach (var candidate in direct)
            {
                if (System.IO.File.Exists(candidate)) return candidate;
            }

            for (var dir = new System.IO.DirectoryInfo(baseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, ".env");
                if (System.IO.File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>
        /// Reads every setting from the environment. Idempotent: a missing or invalid value always falls back to
        /// its default, so calling it again (e.g. in tests) never leaves a stale value behind.
        /// </summary>
        public static void Initialize()
        {
            var versionEnv = Environment.GetEnvironmentVariable("GAME_VERSION");
            GameVersion = !string.IsNullOrEmpty(versionEnv) ? versionEnv : Shared.Constants.GameRules.GameVersion;

            ServerPort = ReadInt("SERVER_PORT", 7777, min: 1, max: 65535);
            DefaultCharacterSlots = ReadInt("DEFAULT_CHARACTER_SLOT", 3, min: 1);

            BaseHealthRes = ReadInt("BASE_HEALTH_RES", 5, min: 0);
            BaseManaRes = ReadInt("BASE_MANA_RES", 3, min: 0);
            RegenTickIntervalSeconds = ReadDouble("REGEN_TICK_INTERVAL_SECONDS", 1.0, min: 0);
            RegenCooldownSeconds = ReadDouble("REGEN_COOLDOWN_SECONDS", 5.0, min: 0);

            WriteBehindFlushSeconds = ReadDouble("WRITE_BEHIND_FLUSH_SECONDS", 2.0, min: 0);
            AutosaveSeconds = ReadDouble("AUTOSAVE_SECONDS", 60.0, min: 1);
            PersistenceWorkers = ReadInt("PERSISTENCE_WORKERS", 4, min: 1, max: 32);
            PersistenceMaxAttempts = ReadInt("PERSISTENCE_MAX_ATTEMPTS", 8, min: 1);
            ChatLogQueueSize = ReadInt("CHAT_LOG_QUEUE_SIZE", 10000, min: 100);

            CombatTagSeconds = ReadDouble("COMBAT_TAG_SECONDS", 10.0, min: 0);
            CombatLogoutLingerSeconds = ReadDouble("COMBAT_LOGOUT_LINGER_SECONDS", 15.0, min: 0);
            WorldReadyTimeoutSeconds = ReadDouble("WORLD_READY_TIMEOUT_SECONDS", 60.0, min: 0);

            MaxPendingCommandsPerClient = ReadInt("MAX_PENDING_COMMANDS_PER_CLIENT", 128, min: 1);
            CommandBudgetMs = ReadDouble("COMMAND_BUDGET_MS", 10.0, min: 0.001);

            MaxInboundPacketBytes = ReadInt("MAX_INBOUND_PACKET_BYTES", 4096, min: Shared.Network.Packet.HeaderSize, max: ushort.MaxValue);
            PacketCompletionTimeoutSeconds = ReadDouble("PACKET_COMPLETION_TIMEOUT_SECONDS", 10.0, min: 0);
            PreAuthIdleTimeoutSeconds = ReadDouble("PRE_AUTH_IDLE_TIMEOUT_SECONDS", 30.0, min: 0);
            SendTimeoutSeconds = ReadDouble("SEND_TIMEOUT_SECONDS", 5.0, min: 0.1);

            SignInMaxFailures = ReadInt("SIGNIN_MAX_FAILURES", 5, min: 1);
            SignInFailureWindowSeconds = ReadDouble("SIGNIN_FAILURE_WINDOW_SECONDS", 60.0, min: 1);
            SignInLockoutSeconds = ReadDouble("SIGNIN_LOCKOUT_SECONDS", 60.0, min: 1);
            ChatBurst = ReadInt("CHAT_BURST", 5, min: 1);
            ChatMessagesPerSecond = ReadDouble("CHAT_MESSAGES_PER_SECOND", 1.0, min: 0.01);

            ExpBase = ReadInt("EXP_BASE", 100, min: 1);
            ExpGrowthRate = ReadDouble("EXP_GROWTH_RATE", 1.5, min: 0);
            StatPointsPerLevel = ReadInt("STAT_POINTS_PER_LEVEL", 5, min: 0);

            DefaultBackpackSlots = ReadInt("DEFAULT_BACKPACK_SLOTS", 20, min: 1);

            LootOwnershipSeconds = (float)ReadDouble("LOOT_OWNERSHIP_SECONDS", 30.0, min: 0);
            LootDecaySeconds = (float)ReadDouble("LOOT_DECAY_SECONDS", 120.0, min: 0);

            Console.WriteLine($"[Config] Running Version: {GameVersion} | Port: {ServerPort} | Regen: {BaseHealthRes} HP / {BaseManaRes} MP every {RegenTickIntervalSeconds}s (blocked {RegenCooldownSeconds}s after combat) | EXP Base: {ExpBase}, Growth: {ExpGrowthRate} | Slots: {DefaultBackpackSlots} | Loot: {LootOwnershipSeconds}s owner / {LootDecaySeconds}s decay");
        }

        // All numeric settings are parsed with the invariant culture: ".env" files always use '.' as the decimal
        // separator, but the current culture of the host (e.g. pt-BR) would read "0.5" as 5.
        private static int ReadInt(string name, int fallback, int min = int.MinValue, int max = int.MaxValue)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max)
            {
                return value;
            }

            Console.WriteLine($"[Config] Ignoring invalid {name}='{raw}' (expected an integer between {min} and {max}); using {fallback}.");
            return fallback;
        }

        private static double ReadDouble(string name, double fallback, double min = double.MinValue)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && double.IsFinite(value) && value >= min)
            {
                return value;
            }

            Console.WriteLine($"[Config] Ignoring invalid {name}='{raw}' (expected a number >= {min}); using {fallback}.");
            return fallback;
        }

        public static long GetExpForNextLevel(int currentLevel)
        {
            if (currentLevel < 1) currentLevel = 1;
            return (long)Math.Floor(ExpBase * Math.Pow(currentLevel, ExpGrowthRate));
        }
    }
}
