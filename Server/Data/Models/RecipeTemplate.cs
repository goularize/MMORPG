using System.Collections.Generic;

namespace Server.Data.Models
{
    public class RecipeIngredient
    {
        public int ItemTemplateId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class RecipeTemplate
    {
        public int RecipeId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ResultItemTemplateId { get; set; }
        public int ResultQuantity { get; set; } = 1;
        public int RequiredGold { get; set; } = 0;
        public List<RecipeIngredient> Ingredients { get; set; } = new();
    }
}
