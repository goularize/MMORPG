using System.Collections.Generic;

namespace Server.Data.Models
{
    public class NpcTemplate
    {
        public int TemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Enemy";
        public string Behavior { get; set; } = "Passive";
        public float AggroRadius { get; set; } = 0.0f;
        public float WanderRadius { get; set; } = 5.0f;
        public float LeashRadius { get; set; } = 20.0f;
        public float RespawnTimeSeconds { get; set; } = 15.0f;
        public float WalkSpeed { get; set; } = 2.0f;
        public float RunSpeed { get; set; } = 3.5f;
        public float AttackRange { get; set; } = 2.0f;
        public float PackAssistRadius { get; set; } = 12.0f;
        public string PrefabName { get; set; } = string.Empty;
        public int[] LevelRange { get; set; } = new int[] { 1, 1 };
        public float[] StatVarianceRange { get; set; } = new float[] { 1.0f, 1.0f };
        public int BaseStrength { get; set; }
        public int BaseIntelligence { get; set; }
        public int BaseConstitution { get; set; }
        public int BaseKnowledge { get; set; }
        public int BaseExp { get; set; } = 25;

        /// <summary>Id of the Dialogues.json entry shown when a player talks to this NPC; empty when it offers no conversation.</summary>
        public string DialogueId { get; set; } = string.Empty;

        /// <summary>True when the NPC's dialogue has an option with this action (e.g. an innkeeper offers BindPoint).</summary>
        public bool OffersAction(Shared.Enums.InteractAction action) =>
            !string.IsNullOrEmpty(DialogueId) && DataManager.Dialogues.TryGetValue(DialogueId, out var dialogue) && dialogue.OffersAction(action);

        public bool IsFriendly => string.Equals(Type, "Friendly", System.StringComparison.OrdinalIgnoreCase);

        public Shared.Enums.MobBehaviorType BehaviorType
        {
            get
            {
                // A Friendly NPC is never attackable, whatever its AI behavior says.
                if (IsFriendly) return Shared.Enums.MobBehaviorType.Friendly;
                if (System.Enum.TryParse<Shared.Enums.MobBehaviorType>(Behavior, true, out var parsed))
                {
                    return parsed;
                }
                return Shared.Enums.MobBehaviorType.Passive;
            }
        }
    }
}
