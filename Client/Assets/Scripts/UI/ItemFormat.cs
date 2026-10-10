using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Client.Network.Handlers;
using Shared.Enums;

namespace Client.UI
{
    /// <summary>How items are shown: rarity colours, the tooltip text and the icon lookup.</summary>
    public static class ItemFormat
    {
        /// <summary>Item icons are looked up by the template's Icon name in Resources/ItemIcons.</summary>
        public const string IconFolder = "ItemIcons/";

        private static readonly Dictionary<string, Sprite> _icons = new();

        public static Color RarityColor(ItemRarity rarity) => rarity switch
        {
            ItemRarity.Uncommon => new Color(0.30f, 0.80f, 0.30f),
            ItemRarity.Rare => new Color(0.30f, 0.55f, 1.00f),
            ItemRarity.Epic => new Color(0.70f, 0.35f, 0.95f),
            ItemRarity.Legendary => new Color(1.00f, 0.60f, 0.10f),
            _ => new Color(0.80f, 0.80f, 0.80f)
        };

        /// <summary>The item's icon, or null when the project has no sprite with that name (the slot then shows initials).</summary>
        public static Sprite Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (!_icons.TryGetValue(name, out var sprite))
            {
                sprite = Resources.Load<Sprite>(IconFolder + name);
                _icons[name] = sprite;
            }
            return sprite;
        }

        /// <summary>Two letters standing in for a missing icon, e.g. "Iron Broadsword" -> "IB".</summary>
        public static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";

            var words = name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 1
                ? words[0].Substring(0, System.Math.Min(2, words[0].Length))
                : string.Concat(words[0][0], words[1][0]);
        }

        public static string DisplayName(ItemView item) =>
            item.UpgradeLevel > 0 ? $"{item.Name} +{item.UpgradeLevel}" : item.Name;

        public static string SlotName(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.MainHand => "Main hand",
            EquipmentSlot.OffHand => "Off hand",
            _ => slot.ToString()
        };

        /// <param name="characterLevel">Level lines turn red when the character is below the requirement.</param>
        public static string Tooltip(ItemView item, int characterLevel)
        {
            var text = new StringBuilder();
            string color = ColorUtility.ToHtmlStringRGB(RarityColor(item.Rarity));
            text.Append($"<b><color=#{color}>{DisplayName(item)}</color></b>\n");
            text.Append($"<color=#{color}>{item.Rarity}</color>");

            if (item.Type == ItemType.Equipment)
            {
                string hands = item.Slot == EquipmentSlot.MainHand ? (item.IsTwoHanded ? ", two-handed" : ", one-handed") : string.Empty;
                text.Append($" {SlotName(item.Slot).ToLowerInvariant()}{hands}");
            }
            else if (item.Type != ItemType.Unknown)
            {
                text.Append($" {item.Type.ToString().ToLowerInvariant()}");
            }
            text.Append('\n');

            if (item.RequiredLevel > 1)
            {
                string levelColor = characterLevel >= item.RequiredLevel ? "FFFFFF" : "FF5555";
                text.Append($"<color=#{levelColor}>Requires level {item.RequiredLevel}</color>\n");
            }

            AppendStat(text, "Physical attack", item.PhysicalAttack);
            AppendStat(text, "Magic attack", item.MagicAttack);
            AppendStat(text, "Physical defense", item.PhysicalDefense);
            AppendStat(text, "Magic defense", item.MagicDefense);
            AppendStat(text, "Strength", item.Strength);
            AppendStat(text, "Intelligence", item.Intelligence);
            AppendStat(text, "Constitution", item.Constitution);
            AppendStat(text, "Knowledge", item.Knowledge);
            AppendStat(text, "Critical chance", item.CritChance, "%");
            AppendStat(text, "Critical damage", item.CritMultiplier, "%");
            AppendStat(text, "Dodge", item.DodgeChance, "%");
            AppendStat(text, "Movement speed", item.MovementSpeed);
            AppendStat(text, "Attack speed", item.AttackSpeedBonus, "%");
            AppendStat(text, "Health regeneration", item.HealthRegen);
            AppendStat(text, "Mana regeneration", item.ManaRegen);

            if (!string.IsNullOrWhiteSpace(item.Description)) text.Append($"\n<i>{item.Description}</i>\n");
            if (item.BasePrice > 0) text.Append($"\nWorth {item.BasePrice} gold");

            return text.ToString().TrimEnd('\n');
        }

        private static void AppendStat(StringBuilder text, string label, int value)
        {
            if (value != 0) text.Append($"<color=#9FE39F>{(value > 0 ? "+" : string.Empty)}{value} {label}</color>\n");
        }

        private static void AppendStat(StringBuilder text, string label, float value, string suffix = "")
        {
            if (Mathf.Abs(value) > 0.0001f)
                text.Append($"<color=#9FE39F>{(value > 0 ? "+" : string.Empty)}{value:0.##}{suffix} {label}</color>\n");
        }
    }
}
