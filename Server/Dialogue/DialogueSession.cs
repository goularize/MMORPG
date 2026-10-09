using System.Collections.Generic;
using Server.Data.Models;

namespace Server.Dialogue
{
    /// <summary>
    /// A conversation a player has open: which NPC, which screen, and exactly which options were offered. The client
    /// can only answer with one of the offered option ids, so it can never pick something it was not shown.
    /// Owned by <see cref="Server.World.Entities.Player"/> and only touched by the game thread.
    /// </summary>
    public sealed class DialogueSession
    {
        public int NpcId { get; }
        public int NpcTemplateId { get; }
        public DialogueTemplate Dialogue { get; }

        /// <summary>The node being shown: a node name, "$greeting", or a generated "quest:..." node.</summary>
        public string Node { get; set; } = string.Empty;

        public Dictionary<int, DialogueOption> Offered { get; } = new();

        public DialogueSession(int npcId, int npcTemplateId, DialogueTemplate dialogue)
        {
            NpcId = npcId;
            NpcTemplateId = npcTemplateId;
            Dialogue = dialogue;
        }
    }
}
