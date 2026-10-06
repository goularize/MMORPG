using System.Collections.Generic;

namespace Server.Data.Models
{
    public class StarterItem
    {
        public int TemplateId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    /// <summary>What a brand-new character starts with. Data-driven so balance changes need no code change.</summary>
    public class CharacterCreationTemplate
    {
        public int StartMapId { get; set; } = 1;
        public float StartX { get; set; }
        public float StartY { get; set; }
        public float StartZ { get; set; }

        public int Health { get; set; } = 100;
        public int Mana { get; set; } = 50;
        public int Strength { get; set; } = 10;
        public int Intelligence { get; set; } = 10;
        public int Constitution { get; set; } = 10;
        public int Knowledge { get; set; } = 10;
        public long Gold { get; set; } = 100;

        /// <summary>Placed in the main backpack, one slot each, in this order.</summary>
        public List<StarterItem> StarterItems { get; set; } = new();
    }
}
