namespace Shared.Constants
{
    /// <summary>
    /// Gameplay and protocol values that BOTH the server and the Unity client must agree on.
    /// Per-entity values (live movement/attack speed, ranges that depend on a weapon) belong on
    /// the entity; the "Base*" and "Default*" values here are only their starting points.
    /// Server-only tuning (anti-cheat tolerances, EXP curve) stays in ServerConfig.
    /// </summary>
    public static class GameRules
    {
        /// <summary>Protocol/build version exchanged at sign-in. The server may override it via GAME_VERSION.</summary>
        public const string GameVersion = "0.2.3-alpha";

        // --- Simulation ---
        /// <summary>Fixed simulation rate of the authoritative server loop (ticks per second).</summary>
        public const int ServerTickRate = 30;

        // --- Area of Interest / social ranges (world units) ---
        public const float AoiRadius = 50.0f;
        public const float ChatLocalRadius = 50.0f;
        public const float InteractRange = 3.0f;

        // --- Combat ---
        /// <summary>Default reach when no weapon defines its own.</summary>
        public const float DefaultMeleeRange = 2.5f;

        // --- Player base values (gear and stats modify the live per-entity values) ---
        public const float BasePlayerMoveSpeed = 4.0f;
        public const float MinPlayerMoveSpeed = 2.0f;
        /// <summary>Seconds between swings before attack speed bonuses.</summary>
        public const float BasePlayerAttackInterval = 1.5f;
        public const float MinPlayerAttackInterval = 0.5f;
    }
}
