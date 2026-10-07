namespace Client.World
{
    /// <summary>
    /// Per-map gameplay rules the client needs for presentation. Every map is a safe map until the server defines
    /// PvP maps (see #169); this is the single place to change when it does, and the rule should then move to
    /// Shared so the server enforces the same thing.
    /// </summary>
    public static class MapRules
    {
        /// <summary>On a PvP map other players are solid; on a safe map you can walk through them.</summary>
        public static bool IsPvp(int mapId) => false;
    }
}
