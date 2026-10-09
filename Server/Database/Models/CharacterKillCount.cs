using Microsoft.EntityFrameworkCore;

namespace Server.Database.Models
{
    /// <summary>How many NPCs of a template a character has killed.</summary>
    [PrimaryKey(nameof(CharacterId), nameof(NpcTemplateId))]
    public class CharacterKillCount
    {
        public int CharacterId { get; set; }
        public Character? Character { get; set; }

        public int NpcTemplateId { get; set; }
        public int Count { get; set; }
    }
}
