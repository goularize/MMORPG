using System.Collections.Generic;
using Shared.Data;
using Shared.Enums;

namespace Server.Data.Models
{
    public class QuestObjective
    {
        public QuestObjectiveType Type { get; set; }

        /// <summary>NPC template id (Kill, Talk) or item template id (Collect).</summary>
        public int Id { get; set; }

        public int Count { get; set; } = 1;

        /// <summary>Optional text for the quest screen; a default is built from the target when empty.</summary>
        public string? Description { get; set; }
    }

    public class QuestRewardItem
    {
        public int ItemTemplateId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class QuestRewards
    {
        public long Exp { get; set; }
        public long Gold { get; set; }
        public List<QuestRewardItem> Items { get; set; } = new();
    }

    public class QuestText
    {
        /// <summary>Shown when the quest is offered.</summary>
        public string Offer { get; set; } = string.Empty;

        /// <summary>Shown while the quest is in progress.</summary>
        public string Progress { get; set; } = string.Empty;

        /// <summary>Shown when the objectives are done and the player turns the quest in.</summary>
        public string Complete { get; set; } = string.Empty;
    }

    public class QuestTemplate
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MinLevel { get; set; } = 1;

        /// <summary>NPC template that offers the quest.</summary>
        public int GiverNpcTemplateId { get; set; }

        /// <summary>NPC template the quest is turned in to.</summary>
        public int TurnInNpcTemplateId { get; set; }

        public List<Condition> Prerequisites { get; set; } = new();
        public List<QuestObjective> Objectives { get; set; } = new();
        public QuestRewards Rewards { get; set; } = new();
        public QuestText Text { get; set; } = new();
    }
}
