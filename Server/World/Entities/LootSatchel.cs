using System;
using System.Collections.Generic;
using Server.Database.Models;
using Shared.Math;

namespace Server.World.Entities
{
    public class LootSatchel : Entity
    {
        private static int _nextSatchelEntityId = 500000;

        public int OwnerPlayerId { get; set; }
        public DateTime CreatedTime { get; set; }
        public float OwnershipDurationSeconds { get; set; }
        public float DecayDurationSeconds { get; set; }

        public long Gold { get; set; }
        public List<CharacterItem> Items { get; } = new();

        public readonly object Lock = new();

        public bool IsEmpty
        {
            get
            {
                lock (Lock)
                {
                    return Gold <= 0 && Items.Count == 0;
                }
            }
        }

        public bool IsPublic => (DateTime.UtcNow - CreatedTime).TotalSeconds >= OwnershipDurationSeconds;

        public bool IsExpired => (DateTime.UtcNow - CreatedTime).TotalSeconds >= DecayDurationSeconds;

        public float RemainingDecaySeconds => Math.Max(0f, (float)(DecayDurationSeconds - (DateTime.UtcNow - CreatedTime).TotalSeconds));

        public float RemainingOwnershipSeconds => Math.Max(0f, (float)(OwnershipDurationSeconds - (DateTime.UtcNow - CreatedTime).TotalSeconds));

        public LootSatchel(Vector3 position, int ownerPlayerId, long gold = 0, IEnumerable<CharacterItem>? items = null)
        {
            Id = System.Threading.Interlocked.Increment(ref _nextSatchelEntityId);
            Name = "LootSatchel";
            Position = position;
            OwnerPlayerId = ownerPlayerId;
            CreatedTime = DateTime.UtcNow;
            OwnershipDurationSeconds = ServerConfig.LootOwnershipSeconds;
            DecayDurationSeconds = ServerConfig.LootDecaySeconds;
            Gold = gold;
            if (items != null)
            {
                Items.AddRange(items);
            }
        }

        public LootSatchel(int id, Vector3 position, int ownerPlayerId, long gold = 0, IEnumerable<CharacterItem>? items = null)
        {
            Id = id;
            Name = "LootSatchel";
            Position = position;
            OwnerPlayerId = ownerPlayerId;
            CreatedTime = DateTime.UtcNow;
            OwnershipDurationSeconds = ServerConfig.LootOwnershipSeconds;
            DecayDurationSeconds = ServerConfig.LootDecaySeconds;
            Gold = gold;
            if (items != null)
            {
                Items.AddRange(items);
            }
        }

        public bool CanLoot(Player player)
        {
            if (player == null) return false;
            if (player.Id == OwnerPlayerId) return true;
            return IsPublic;
        }

        public override void Update()
        {
            // Satchel lifecycle (decay / despawn) is processed in MapInstance.Update()
        }
    }
}
