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

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property: One Account can have many Characters
        public System.Collections.Generic.List<Character> Characters { get; set; } = new();
    }
}
