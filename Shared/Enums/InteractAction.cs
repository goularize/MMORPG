namespace Shared.Enums
{
    /// <summary>
    /// What a dialogue option does when the player picks it (the Action of an option in Dialogues.json). The numeric
    /// values are sent to the client (EntityInteractResponse, DialogueOpen), so they must not change.
    /// </summary>
    public enum InteractAction : byte
    {
        None = 0,
        BindPoint = 1,
        OpenShop = 2,

        /// <summary>Ends the conversation.</summary>
        Close = 3,

        /// <summary>Shows another node of the dialogue (the option's Node, or "$greeting" for the NPC's greeting).</summary>
        GotoNode = 4,

        /// <summary>Sets the story flag named by the option's Name.</summary>
        SetFlag = 5,

        /// <summary>Clears the story flag named by the option's Name.</summary>
        ClearFlag = 6,

        /// <summary>Accepts the quest whose id is the option's Value.</summary>
        StartQuest = 7,

        /// <summary>Turns in the quest whose id is the option's Value.</summary>
        TurnInQuest = 8
    }
}
