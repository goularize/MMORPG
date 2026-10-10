namespace Shared.Enums
{
    /// <summary>
    /// The marker drawn over an NPC's head for ONE player (EntityMarker packet). The server computes it from the
    /// player's quests, so two players looking at the same NPC can see different markers. The numeric values are sent
    /// to the client, so they must not change.
    /// </summary>
    public enum EntityMarker : byte
    {
        None = 0,

        /// <summary>The NPC offers a quest the player can accept now ("!").</summary>
        QuestAvailable = 1,

        /// <summary>The player can turn a quest in to this NPC ("?").</summary>
        QuestReady = 2
    }
}
