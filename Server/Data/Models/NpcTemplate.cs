using System.Collections.Generic;

namespace Server.Data.Models
{
    public class NpcInteraction
    {
        public string ConditionType { get; set; } = "None";
        public int ConditionValue { get; set; } = 0;
        public string Action { get; set; } = "None";
        public int ActionValue { get; set; } = 0;
    }

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
        public List<NpcInteraction> Interactions { get; set; } = new List<NpcInteraction>();

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
