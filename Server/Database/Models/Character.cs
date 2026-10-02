using System;
using System.ComponentModel.DataAnnotations;

namespace Server.Database.Models
{
    public class Character
    {
        [Key]
        public int Id { get; set; }

        public int AccountId { get; set; }
        public Account? Account { get; set; }

        [Required]
        [MaxLength(30)]
        public string Name { get; set; } = string.Empty;

        // Visual / Aesthetic
        public int AppearanceId { get; set; }

        // Last known position
        public int MapId { get; set; } = 1;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        // Bind Location (Inn/Respawn)
        public int BindMapId { get; set; } = 1;
        public float BindX { get; set; }
        public float BindY { get; set; }
        public float BindZ { get; set; }

        // RPG Progression
        public int Level { get; set; } = 1;
        public long Exp { get; set; } = 0;
        public int StatPoints { get; set; } = 0;

        // Current Vitals
        public int Health { get; set; }
        public int Mana { get; set; }

        // Base Stats
        public int Strength { get; set; }
        public int Intelligence { get; set; }
        public int Constitution { get; set; }
        public int Knowledge { get; set; }

        // Currency & Inventory
        public long Gold { get; set; } = 0;
        public int InventorySlots { get; set; } = 20;

        // Navigation Collections
        public System.Collections.Generic.List<CharacterItem> InventoryItems { get; set; } = new();
        public System.Collections.Generic.List<CharacterLearnedRecipe> LearnedRecipes { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
