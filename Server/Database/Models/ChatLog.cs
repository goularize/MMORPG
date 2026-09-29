using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Shared.Network;

namespace Server.Database.Models
{
    public class ChatLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        public ChatChannel Channel { get; set; }

        [Required]
        [MaxLength(32)]
        public string SenderName { get; set; } = string.Empty;

        [MaxLength(32)]
        public string? TargetName { get; set; } // Nullable, only used for Whispers

        [Required]
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;
    }
}
