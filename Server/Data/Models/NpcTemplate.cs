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
        public string Type { get; set; } = "Monster";
        public string PrefabName { get; set; } = string.Empty;
        public int[] LevelRange { get; set; } = new int[] { 1, 1 };
        public float[] StatVarianceRange { get; set; } = new float[] { 1.0f, 1.0f };
        public int BaseStrength { get; set; }
        public int BaseIntelligence { get; set; }
        public int BaseConstitution { get; set; }
        public int BaseKnowledge { get; set; }
        public List<NpcInteraction> Interactions { get; set; } = new List<NpcInteraction>();
    }
}
