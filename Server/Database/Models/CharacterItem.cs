using System;
using System.ComponentModel.DataAnnotations;
using Shared.Enums;

namespace Server.Database.Models
{
    public class CharacterItem
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public int CharacterId { get; set; }
        public Character? Character { get; set; }

        public int TemplateId { get; set; }

        // Inventory location (-1 if equipped or bag slot)
        public int BagIndex { get; set; } = 0;   // 0 = Main Backpack, 1..4 = Extra Bags
        public int SlotIndex { get; set; } = 0;  // 0..19
        public int Quantity { get; set; } = 1;

        // Paperdoll Equipment State
        public bool IsEquipped { get; set; } = false;
        public EquipmentSlot? EquippedSlot { get; set; }

        // Refinement (+0, +1, +2, etc.) & Rarity
        public int UpgradeLevel { get; set; } = 0;
        public ItemRarity Rarity { get; set; } = ItemRarity.Common;

        // Rolled Primary Combat Stats & Attributes
        public int RolledPhysicalAttack { get; set; }
        public int RolledMagicAttack { get; set; }
        public int RolledPhysicalDefense { get; set; }
        public int RolledMagicDefense { get; set; }
        public int RolledStrength { get; set; }
        public int RolledIntelligence { get; set; }
        public int RolledConstitution { get; set; }
        public int RolledKnowledge { get; set; }

        // Rolled Secondary Combat Stats
        public float RolledCritChance { get; set; }
        public float RolledCritMultiplier { get; set; }
        public float RolledDodgeChance { get; set; }
        public float RolledMovementSpeed { get; set; }
        public float RolledAttackSpeedBonus { get; set; }
        public int RolledHealthRegen { get; set; }
        public int RolledManaRegen { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
