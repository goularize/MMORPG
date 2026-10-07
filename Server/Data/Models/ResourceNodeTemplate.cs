using System.Collections.Generic;

namespace Server.Data.Models
{
    /// <summary>A harvestable node type (tree, ore vein...) and what it yields when it is depleted.</summary>
    public class ResourceNodeTemplate
    {
        public int TemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int MaxHealth { get; set; } = 100;
        public int Defense { get; set; } = 10;
        public float RespawnTimeSeconds { get; set; } = 60.0f;
        public List<LootEntry> Drops { get; set; } = new();
    }
}
