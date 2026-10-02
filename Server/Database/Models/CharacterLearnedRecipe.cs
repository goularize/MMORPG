using System;
using System.ComponentModel.DataAnnotations;

namespace Server.Database.Models
{
    public class CharacterLearnedRecipe
    {
        [Key]
        public int Id { get; set; }

        public int CharacterId { get; set; }
        public Character? Character { get; set; }

        public int RecipeId { get; set; }

        public DateTime LearnedAt { get; set; } = DateTime.UtcNow;
    }
}
