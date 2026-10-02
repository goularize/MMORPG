using System.Collections.Generic;
using Shared.Enums;
using Shared.Models;

namespace Server.Data.Models
{
    public class ItemTemplate
    {
        public int TemplateId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ItemType Type { get; set; } = ItemType.Unknown;
        public ItemRarity Rarity { get; set; } = ItemRarity.Common;
        public EquipmentSlot Slot { get; set; } = EquipmentSlot.None;
        public int RequiredLevel { get; set; } = 1;
        public int MaxStack { get; set; } = 1;
        public int BasePrice { get; set; } = 0;
        public bool IsTwoHanded { get; set; } = false;

        public string Icon { get; set; } = string.Empty;
        public string Prefab { get; set; } = string.Empty;

        // Primary Stat Ranges (Min, Max)
        public StatRange PhysicalAttack { get; set; } = new(0, 0);
        public StatRange MagicAttack { get; set; } = new(0, 0);
        public StatRange PhysicalDefense { get; set; } = new(0, 0);
        public StatRange MagicDefense { get; set; } = new(0, 0);
        public StatRange Strength { get; set; } = new(0, 0);
        public StatRange Intelligence { get; set; } = new(0, 0);
        public StatRange Constitution { get; set; } = new(0, 0);
        public StatRange Knowledge { get; set; } = new(0, 0);

        // Secondary Stat Ranges (values represented in percentage points e.g. [1, 5] for 1% - 5%)
        public StatRange CritChance { get; set; } = new(0, 0);
        public StatRange CritMultiplier { get; set; } = new(0, 0); // e.g. [10, 30] for +10% to +30%
        public StatRange DodgeChance { get; set; } = new(0, 0);
        public StatRange MovementSpeed { get; set; } = new(0, 0); // e.g. [2, 8] for +0.2 to +0.8

        // Consumable Effect Properties
        public int RestoreHealth { get; set; } = 0;
        public int RestoreMana { get; set; } = 0;
        public int BuffDurationSeconds { get; set; } = 0;
    }
}
