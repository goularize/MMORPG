namespace Shared.Enums
{
    public enum QuestObjectiveType : byte
    {
        Unknown = 0,

        /// <summary>Kill Count NPCs of template Id after accepting the quest.</summary>
        Kill = 1,

        /// <summary>Hold Count of item template Id. The items are handed in when the quest is turned in.</summary>
        Collect = 2,

        /// <summary>Talk to the NPC of template Id (Count times).</summary>
        Talk = 3
    }
}
