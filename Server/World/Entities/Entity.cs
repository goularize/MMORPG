using Shared.Math;

namespace Server.World.Entities
{
    public abstract class Entity
    {
        // Core Identity
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Vector3 Position { get; set; }

        // Vitals
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int Mana { get; set; }
        public int MaxMana { get; set; }

        // Base Stats
        public int Strength { get; set; }       // Physical Attack
        public int Intelligence { get; set; }   // Magical Attack
        public int Constitution { get; set; }   // Physical Defense / Health
        public int Knowledge { get; set; }      // Magical Defense / Mana

        // Methods
        public virtual void Update()
        {
            // Base update logic (e.g. passive health regen)
        }
    }
}
