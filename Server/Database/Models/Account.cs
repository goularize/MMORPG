using System;
using System.ComponentModel.DataAnnotations;

namespace Server.Database.Models
{
    public class Account
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)] // Store the hashed password, NEVER plaintext
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>Stored normalized (trimmed, lower-case). Null for accounts created before email was collected.</summary>
        [MaxLength(254)]
        public string? Email { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public int CharacterSlots { get; set; } = 3;

        // Navigation Property: One Account can have many Characters
        public System.Collections.Generic.List<Character> Characters { get; set; } = new();
    }
}
