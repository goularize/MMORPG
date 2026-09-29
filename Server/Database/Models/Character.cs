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

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
