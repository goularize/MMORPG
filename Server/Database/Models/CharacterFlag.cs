using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Server.Database.Models
{
    /// <summary>A named story/progress flag a character has set (see PlayerProgress).</summary>
    [PrimaryKey(nameof(CharacterId), nameof(Flag))]
    public class CharacterFlag
    {
        public int CharacterId { get; set; }
        public Character? Character { get; set; }

        [MaxLength(Shared.Constants.ProgressRules.FlagMaxLength)]
        public string Flag { get; set; } = string.Empty;

        public DateTime SetAt { get; set; } = DateTime.UtcNow;
    }
}
