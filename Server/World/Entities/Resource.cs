using System;
using System.Collections.Generic;
using Server.Data.Models;
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

        /// <summary>What this node yields when depleted. Without one (tests, ad-hoc nodes) it drops nothing.</summary>
        public ResourceNodeTemplate? Template { get; }

        public Resource(int id, ResourceNodeTemplate template, Vector3 position)
            : this(id, template.Name, position, template.RespawnTimeSeconds)
        {
            Template = template;
            MaxHealth = template.MaxHealth;
            Health = template.MaxHealth;
            Defense = template.Defense;
        }

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
            if (Template == null) return;

            var droppedItems = new List<CharacterItem>();
            foreach (var entry in Template.Drops)
            {
                if (_rng.NextDouble() > entry.DropChance) continue;

                int quantity = _rng.Next(entry.MinQuantity, entry.MaxQuantity + 1);
                var item = ItemFactory.CreateItem(entry.ItemTemplateId, 0, quantity, null, _rng);
                if (item != null) droppedItems.Add(item);
            }

            if (droppedItems.Count == 0) return;

            map.SpawnLootSatchel(new LootSatchel(Position, harvester.Id, 0, droppedItems));
        }

        private void Respawn()
        {
            Health = MaxHealth;
            _vitalsChanged = true;
            Console.WriteLine($"[Resource] {Name} ({Id}) respawned!");
        }
    }
}
