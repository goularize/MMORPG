namespace Shared.Enums
{
    /// <summary>The kinds of requirement a Condition can express (see Shared.Data.Condition).</summary>
    public enum ConditionType : byte
    {
        Unknown = 0,

        /// <summary>The character has killed at least Min NPCs of template Id.</summary>
        KillCount = 1,

        /// <summary>The character's quest Id is in State (Available when it is not in the quest log).</summary>
        QuestState = 2,

        /// <summary>The character has the story flag Name.</summary>
        Flag = 3,

        /// <summary>The character's level is at least Min and, when Max is above 0, at most Max.</summary>
        Level = 4,

        /// <summary>The character owns at least Min (default 1) of item template Id, in the backpack or equipped.</summary>
        HasItem = 5
    }
}
