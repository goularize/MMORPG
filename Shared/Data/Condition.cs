#nullable enable
using Shared.Enums;

namespace Shared.Data
{
    /// <summary>
    /// One requirement on a character's progress. Used by dialogue greetings and options and by quest prerequisites;
    /// the server evaluates it, the client never decides. Which fields matter depends on <see cref="Type"/>:
    /// KillCount (Id, Min), QuestState (Id, State), Flag (Name), Level (Min, Max), HasItem (Id, Min).
    /// </summary>
    public class Condition
    {
        public ConditionType Type { get; set; }

        /// <summary>NPC template id (KillCount), quest id (QuestState) or item template id (HasItem).</summary>
        public int Id { get; set; }

        /// <summary>Flag name (Flag).</summary>
        public string? Name { get; set; }

        /// <summary>Lower bound: kills, level or item quantity (default 1).</summary>
        public int Min { get; set; } = 1;

        /// <summary>Upper bound for Level; 0 means no upper bound.</summary>
        public int Max { get; set; }

        /// <summary>Required quest state (QuestState).</summary>
        public QuestState State { get; set; } = QuestState.Rewarded;

        /// <summary>Inverts the result of this condition.</summary>
        public bool Negate { get; set; }
    }
}
