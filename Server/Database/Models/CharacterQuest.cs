using System;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace Server.Database.Models
{
    /// <summary>A quest log entry. A quest that is not in the log is <see cref="QuestState.Available"/>.</summary>
    [PrimaryKey(nameof(CharacterId), nameof(QuestId))]
    public class CharacterQuest
    {
        public int CharacterId { get; set; }
        public Character? Character { get; set; }

        public int QuestId { get; set; }
        public QuestState State { get; set; }

        /// <summary>Objective counters as a JSON int array, in the order of the quest's objectives.</summary>
        public string ProgressJson { get; set; } = "[]";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
