using Shared.Math;

namespace Server.World.Entities
{
    public abstract class Entity
    {
        // Core Identity
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Vector3 Position { get; set; }

        // RPG Progression
        public int Level { get; set; } = 1;
        public int MapId { get; set; } = 1;

        // Vitals
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int Mana { get; set; }
        public int MaxMana { get; set; }

        // Core Attributes
        public int Strength { get; set; }       // Physical Attack
        public int Intelligence { get; set; }   // Magical Attack
        public int Constitution { get; set; }   // Physical Defense / Health
        public int Knowledge { get; set; }      // Magical Defense / Mana

        // Derived Stats
        public int Attack { get; set; }
        public int MagicAttack { get; set; }
        public int Defense { get; set; }
        public int MagicDefense { get; set; }

        // Regen Tracking
        // Combat & Cooldowns
        public DateTime LastAttackTime { get; set; } = DateTime.MinValue;
        public float BaseAttackSpeed { get; set; } = 1.5f;
        public float CritChance { get; set; } = 0.05f;
        public float CritMultiplier { get; set; } = 2.0f;
        public float DodgeChance { get; set; } = 0.05f;

        protected DateTime _lastRegenTime = DateTime.UtcNow;
        protected bool _vitalsChanged = false;

        public virtual void CalculateDerivedStats()
        {
            MaxHealth = System.Math.Max(1, 50 + (Constitution * 10) + (Level * 15));
            MaxMana = System.Math.Max(1, 20 + (Knowledge * 5) + (Level * 8));

            Attack = System.Math.Max(1, (int)(10 + (Strength * 2) + (Level * 1.5)));
            MagicAttack = System.Math.Max(1, (int)(10 + (Intelligence * 2) + (Level * 1.5)));
            Defense = System.Math.Max(0, (int)((Constitution * 1.5) + (Level * 0.5)));
            MagicDefense = System.Math.Max(0, (int)((Knowledge * 1.5) + (Level * 0.5)));
        }

        // Methods
        public virtual void Update()
        {
            // Base update logic (passive health/mana regen)
            if (Health > 0 && (Health < MaxHealth || Mana < MaxMana)) // Only regen if alive and not capped
            {
                if ((DateTime.UtcNow - _lastRegenTime).TotalSeconds >= ServerConfig.RegenTickIntervalSeconds)
                {
                    _lastRegenTime = DateTime.UtcNow;

                    if (Health < MaxHealth)
                    {
                        Health += ServerConfig.BaseHealthRes;
                        if (Health > MaxHealth) Health = MaxHealth;
                        _vitalsChanged = true;
                    }

                    if (Mana < MaxMana)
                    {
                        Mana += ServerConfig.BaseManaRes;
                        if (Mana > MaxMana) Mana = MaxMana;
                        _vitalsChanged = true;
                    }
                }
            }
        }
    }
}
