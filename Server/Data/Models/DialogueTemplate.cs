using System.Collections.Generic;
using Shared.Data;
using Shared.Enums;

namespace Server.Data.Models
{
    /// <summary>One choice the player can pick in a conversation (an entry of Dialogues.json, or one the server generates).</summary>
    public class DialogueOption
    {
        public string Label { get; set; } = string.Empty;

        /// <summary>The option is only offered while all of these hold.</summary>
        public List<Condition> Conditions { get; set; } = new();

        public InteractAction Action { get; set; } = InteractAction.None;

        /// <summary>Action argument: shop id (OpenShop) or quest id (StartQuest, TurnInQuest).</summary>
        public int Value { get; set; }

        /// <summary>Flag name for SetFlag / ClearFlag.</summary>
        public string? Name { get; set; }

        /// <summary>
        /// Where the conversation continues after the action: a node name, or "$greeting" for the NPC's greeting.
        /// Required for GotoNode; for other actions null ends the conversation.
        /// </summary>
        public string? Node { get; set; }
    }

    /// <summary>A screen of text with options: a greeting variant or a named node.</summary>
    public class DialogueScreen
    {
        public string Text { get; set; } = string.Empty;
        public List<DialogueOption> Options { get; set; } = new();
    }

    /// <summary>What an NPC says when first talked to; the highest-Priority greeting whose conditions hold is shown.</summary>
    public class DialogueGreeting : DialogueScreen
    {
        public int Priority { get; set; }
        public List<Condition> Conditions { get; set; } = new();
    }

    public class DialogueTemplate
    {
        public string Id { get; set; } = string.Empty;
        public List<DialogueGreeting> Greetings { get; set; } = new();
        public Dictionary<string, DialogueScreen> Nodes { get; set; } = new();

        /// <summary>True when any greeting or node has an option with this action.</summary>
        public bool OffersAction(InteractAction action)
        {
            foreach (var greeting in Greetings)
            {
                if (greeting.Options.Exists(o => o.Action == action)) return true;
            }
            foreach (var node in Nodes.Values)
            {
                if (node.Options.Exists(o => o.Action == action)) return true;
            }
            return false;
        }
    }
}
