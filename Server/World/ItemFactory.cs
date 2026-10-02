using System;
using System.Collections.Generic;
using Server.Data;
using Server.Data.Models;
using Server.Database.Models;
using Shared.Enums;

namespace Server.World
{
    public static class ItemFactory
    {
        private static readonly Random _defaultRng = new();

        public static CharacterItem? CreateItem(int templateId, int characterId, int quantity = 1, ItemRarity? forcedRarity = null, Random? rng = null)
        {
            if (!DataManager.Items.TryGetValue(templateId, out var template))
            {
                Console.WriteLine($"[ItemFactory] Item template {templateId} not found.");
                return null;
            }

            rng ??= _defaultRng;

            var item = new CharacterItem
            {
                Id = Guid.NewGuid(),
                CharacterId = characterId,
                TemplateId = templateId,
                Quantity = template.Type == ItemType.Equipment ? 1 : Math.Max(1, quantity),
                UpgradeLevel = 0,
                IsEquipped = false,
                EquippedSlot = null
            };

            // Non-equipment items do not roll combat stats or affixes
            if (template.Type != ItemType.Equipment)
            {
                item.Rarity = template.Rarity;
                return item;
            }

            // Determine Rarity
            ItemRarity rarity = forcedRarity ?? RollRarity(template.Rarity, rng);
            item.Rarity = rarity;

            // Rarity Multiplier for Base Stats
            float statMultiplier = rarity switch
            {
                ItemRarity.Uncommon => 1.10f,
                ItemRarity.Rare => 1.25f,
                ItemRarity.Epic => 1.40f,
                ItemRarity.Legendary => 1.60f,
                _ => 1.0f
            };

            // Roll Primary Combat Stats & Attributes
            item.RolledPhysicalAttack = (int)Math.Round(template.PhysicalAttack.Roll(rng) * statMultiplier);
            item.RolledMagicAttack = (int)Math.Round(template.MagicAttack.Roll(rng) * statMultiplier);
            item.RolledPhysicalDefense = (int)Math.Round(template.PhysicalDefense.Roll(rng) * statMultiplier);
            item.RolledMagicDefense = (int)Math.Round(template.MagicDefense.Roll(rng) * statMultiplier);
            item.RolledStrength = (int)Math.Round(template.Strength.Roll(rng) * statMultiplier);
            item.RolledIntelligence = (int)Math.Round(template.Intelligence.Roll(rng) * statMultiplier);
            item.RolledConstitution = (int)Math.Round(template.Constitution.Roll(rng) * statMultiplier);
            item.RolledKnowledge = (int)Math.Round(template.Knowledge.Roll(rng) * statMultiplier);

            // Roll Secondary Affixes based on Rarity
            int affixCount = rarity switch
            {
                ItemRarity.Uncommon => 1,
                ItemRarity.Rare => 2,
                ItemRarity.Epic => 3,
                ItemRarity.Legendary => 4,
                _ => 0
            };

            RollSecondaryAffixes(item, template, affixCount, rng);

            return item;
        }

        private static ItemRarity RollRarity(ItemRarity minRarity, Random rng)
        {
            double roll = rng.NextDouble();
            ItemRarity rolledRarity = roll switch
            {
                < 0.01 => ItemRarity.Legendary,
                < 0.05 => ItemRarity.Epic,
                < 0.15 => ItemRarity.Rare,
                < 0.35 => ItemRarity.Uncommon,
                _ => ItemRarity.Common
            };

            // If the item template has an inherent minimum rarity (e.g. naturally Rare), respect that
            return rolledRarity > minRarity ? rolledRarity : minRarity;
        }

        private static void RollSecondaryAffixes(CharacterItem item, ItemTemplate template, int count, Random rng)
        {
            if (count <= 0) return;

            // Pool of possible affix types
            var availableAffixes = new List<string>
            {
                "CritChance",
                "CritMultiplier",
                "DodgeChance",
                "MovementSpeed",
                "AttackSpeed",
                "HealthRegen",
                "ManaRegen"
            };

            // Prioritize template secondary ranges if specified
            if (template.CritChance.HasValue && !item.RolledCritChance.Equals(0f))
            {
                item.RolledCritChance = template.CritChance.Roll(rng) * 0.01f;
                availableAffixes.Remove("CritChance");
                count--;
            }

            if (template.MovementSpeed.HasValue && template.Slot == EquipmentSlot.Feet)
            {
                item.RolledMovementSpeed = template.MovementSpeed.Roll(rng) * 0.1f;
                availableAffixes.Remove("MovementSpeed");
                count--;
            }

            // Shuffle and pick remaining affixes
            for (int i = 0; i < count && availableAffixes.Count > 0; i++)
            {
                int index = rng.Next(availableAffixes.Count);
                string affix = availableAffixes[index];
                availableAffixes.RemoveAt(index);

                switch (affix)
                {
                    case "CritChance":
                        item.RolledCritChance += rng.Next(1, 6) * 0.01f; // +1% to +5%
                        break;
                    case "CritMultiplier":
                        item.RolledCritMultiplier += rng.Next(10, 31) * 0.01f; // +10% to +30%
                        break;
                    case "DodgeChance":
                        item.RolledDodgeChance += rng.Next(1, 5) * 0.01f; // +1% to +4%
                        break;
                    case "MovementSpeed":
                        item.RolledMovementSpeed += rng.Next(2, 7) * 0.1f; // +0.2 to +0.6
                        break;
                    case "AttackSpeed":
                        item.RolledAttackSpeedBonus += rng.Next(5, 16) * 0.01f; // +5% to +15%
                        break;
                    case "HealthRegen":
                        item.RolledHealthRegen += rng.Next(2, 6);
                        break;
                    case "ManaRegen":
                        item.RolledManaRegen += rng.Next(2, 6);
                        break;
                }
            }
        }
    }
}
