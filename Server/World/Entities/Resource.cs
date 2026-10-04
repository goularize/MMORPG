using System;
using System.Collections.Generic;
using Server.Database.Models;
using Shared.Math;

namespace Server.World.Entities
{
    public class Resource : Entity
    {
        public override Shared.Enums.EntityType Type => Shared.Enums.EntityType.Resource;

        private static readonly Random _rng = new();

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

        public void TakeDamage(int amount, MapInstance? map = null, Player? harvester = null)
        {
            if (IsDepleted) return;

            Health -= amount;
            if (Health <= 0)
            {
                Health = 0;
                _depletedTime = DateTime.UtcNow;
                Console.WriteLine($"[Resource] {Name} ({Id}) was depleted.");

                if (map != null && harvester != null)
                {
                    DropHarvestSatchel(map, harvester);
                }
            }
            else
            {
                Console.WriteLine($"[Resource] {Name} ({Id}) took {amount} damage, {Health} HP remaining.");
            }
        }

        private void DropHarvestSatchel(MapInstance map, Player harvester)
        {
            int templateId = 3001; // Default: Iron Ore
            int minQty = 2;
            int maxQty = 5;

            if (ResourceType.Contains("Tree", StringComparison.OrdinalIgnoreCase) || 
                ResourceType.Contains("Wood", StringComparison.OrdinalIgnoreCase))
            {
                templateId = 3003; // Oak Wood
                minQty = 2;
                maxQty = 4;
            }

            int yieldCount = _rng.Next(minQty, maxQty + 1);
            var item = ItemFactory.CreateItem(templateId, 0, yieldCount, null, _rng);
            var droppedItems = new List<CharacterItem>();
            if (item != null)
            {
                droppedItems.Add(item);
            }

            var satchel = new LootSatchel(Position, harvester.Id, 0, droppedItems);
            map.SpawnLootSatchel(satchel);
        }

        private void Respawn()
        {
            Health = MaxHealth;
            _vitalsChanged = true;
            Console.WriteLine($"[Resource] {Name} ({Id}) respawned!");
        }
    }
}
