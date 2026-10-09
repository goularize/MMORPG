namespace Shared.Enums
{
    /// <summary>Why the server ended a conversation (DialogueClose).</summary>
    public enum DialogueCloseReason : byte
    {
        Finished = 0,

        /// <summary>The player walked out of interact range.</summary>
        TooFar = 1,

        /// <summary>The NPC died or disappeared.</summary>
        TargetGone = 2,

        /// <summary>The player died.</summary>
        PlayerDied = 3,

        /// <summary>The request did not match what was offered (stale session, unknown option) or the option no longer applies.</summary>
        Invalid = 4,

        /// <summary>A quest turn-in was refused because the rewards do not fit in the backpack.</summary>
        InventoryFull = 5,

        /// <summary>A quest could not be accepted or turned in (requirements not met, quest log full, ...).</summary>
        RequirementsNotMet = 6
    }
}
