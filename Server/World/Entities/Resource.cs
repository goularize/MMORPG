using System;
using Shared.Math;

namespace Server.World.Entities
{
    public class Resource : Entity
    {
        public string ResourceType { get; set; }
        public float RespawnTimeSeconds { get; set; }
        
        public bool IsDepleted => Health <= 0;
        private DateTime _depletedTime;

        public Resource(int id, string type, Vector3 position, float respawnTime)
        {
            Id = id;
            Name = type;
            ResourceType = type;
            Position = position;
            RespawnTimeSeconds = respawnTime;
            
            // Base Stats
            MaxHealth = 100;
            Health = 100;
            Defense = 10;
        }

        public override void Update()
        {
            // Do not call base.Update() because resources don't passively regen health/mana normally,
            // they just respawn fully when their timer hits.
            
            if (IsDepleted)
            {
                if ((DateTime.UtcNow - _depletedTime).TotalSeconds >= RespawnTimeSeconds)
                {
                    Respawn();
                }
            }
        }

        public void TakeDamage(int amount)
        {
            if (IsDepleted) return;

            Health -= amount;
            if (Health <= 0)
            {
                Health = 0;
                _depletedTime = DateTime.UtcNow;
                Console.WriteLine($"[Resource] {Name} ({Id}) was depleted.");
            }
            else
            {
                Console.WriteLine($"[Resource] {Name} ({Id}) took {amount} damage, {Health} HP remaining.");
            }
        }

        private void Respawn()
        {
            Health = MaxHealth;
            _vitalsChanged = true;
            Console.WriteLine($"[Resource] {Name} ({Id}) respawned!");
        }
    }
}
