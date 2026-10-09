namespace Shared.Enums
{
    /// <summary>
    /// Where a character stands with a quest. <see cref="Available"/> is never stored: it is what a character that
    /// has no quest log entry is considered to be in (prerequisites are checked by the quest engine).
    /// </summary>
    public enum QuestState : byte
    {
        Available = 0,
        Active = 1,
        ReadyToTurnIn = 2,
        Rewarded = 3
    }
}
