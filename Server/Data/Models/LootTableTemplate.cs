using System.Collections.Generic;

namespace Server.Data.Models
{
    public class LootEntry
    {
        public int ItemTemplateId { get; set; }
        public float DropChance { get; set; } = 1.0f; // 0.0 to 1.0
        public int MinQuantity { get; set; } = 1;
        public int MaxQuantity { get; set; } = 1;
    }

    public class LootTableTemplate
    {
        public int NpcTemplateId { get; set; }
        public int MinGold { get; set; } = 0;
        public int MaxGold { get; set; } = 0;
        public float GoldChance { get; set; } = 1.0f;
        public List<LootEntry> Entries { get; set; } = new();
    }
}
