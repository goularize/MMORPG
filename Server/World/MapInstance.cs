using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Server.World.Entities;

namespace Server.World
{
    public class MapInstance
    {
        public int MapId { get; }

        // Thread-safe dictionary to hold all active players in the world
        public ConcurrentDictionary<int, Player> Players { get; } = new();
        
        // Thread-safe dictionary for NPCs
        public ConcurrentDictionary<int, NPC> NPCs { get; } = new();

        // Thread-safe dictionary for Resources (Trees, Ores, etc.)
        public ConcurrentDictionary<int, Resource> Resources { get; } = new();

        // Thread-safe dictionary for Loot Satchels (Ground loot bags)
        public ConcurrentDictionary<int, LootSatchel> LootSatchels { get; } = new();

        // Server-authoritative collision providers loaded from Unity maps
        public List<Physics.ICollisionProvider> Colliders { get; } = new();

        public MapInstance(int mapId)
        {
            MapId = mapId;
        }

        public bool IsWalkable(Shared.Math.Vector3 position)
        {
            foreach (var collider in Colliders)
            {
                if (collider.ContainsPoint(position))
                {
                    return false;
                }
            }
            return true;
        }

        public Entity? GetEntity(int id)
        {
            if (Players.TryGetValue(id, out var player)) return player;
            if (NPCs.TryGetValue(id, out var npc)) return npc;
            if (Resources.TryGetValue(id, out var res)) return res;
            if (LootSatchels.TryGetValue(id, out var satchel)) return satchel;
            return null;
        }

        /// <returns>False when a player with this ID is already on the map (it is left untouched).</returns>
        public bool AddPlayer(Player player)
        {
            if (!Players.TryAdd(player.Id, player))
            {
                Console.WriteLine($"[Error] Player {player.Name} (ID: {player.Id}) is already on Map {MapId}; not added again.");
                return false;
            }

            player.MapId = MapId;
            Console.WriteLine($"Player {player.Name} (ID: {player.Id}) joined Map {MapId}.");
            return true;
        }

        public void AddNPC(NPC npc)
        {
            npc.MapId = MapId;
            npc.Map = this;
            if (NPCs.TryAdd(npc.Id, npc))
            {
                Console.WriteLine($"NPC {npc.Name} (ID: {npc.Id}, Behavior: {npc.BehaviorType}) spawned on Map {MapId}.");
            }
        }

        public void SpawnLootSatchel(LootSatchel satchel)
        {
            satchel.MapId = MapId;
            if (LootSatchels.TryAdd(satchel.Id, satchel))
            {
                Console.WriteLine($"[Loot] Spawned satchel {satchel.Id} at {satchel.Position} (Owner: {satchel.OwnerPlayerId}, Gold: {satchel.Gold}, Items: {satchel.Items.Count}).");
            }
        }

        public void DespawnLootSatchel(int satchelId)
        {
            if (LootSatchels.TryRemove(satchelId, out var satchel))
            {
                Console.WriteLine($"[Loot] Despawned satchel {satchelId} on Map {MapId}.");
                using Shared.Network.Packet despawnPacket = new Shared.Network.Packet(Shared.Network.OpCode.EntityDespawn);
                despawnPacket.Write(satchelId);
                Broadcast(despawnPacket, satchel.Position);

                foreach (var p in Players.Values)
                {
                    p.KnownEntities.Remove(satchelId);
                }
            }
        }

        /// <param name="saveState">
        /// False when the caller queues the character's new state itself (respawn queues the bind-point state),
        /// so the death-time state is not queued on top of it.
        /// </param>
        public void RemovePlayer(int playerId, bool saveState = true)
        {
            if (Players.TryRemove(playerId, out Player? player))
            {
                // Ask the game loop to forget what this client was sent. Observers that still list the player
                // are handled by the regular AoI pass (despawn, or just a position update after a same-map respawn).
                player.AoiResetRequested = true;

                // Final state (position, map, vitals, ...) goes to the write-behind queue; it is a durable event
                if (saveState) player.QueueSave(urgent: true);

                Console.WriteLine($"Player {player.Name} left Map {MapId}{(saveState ? " (Position saved)" : string.Empty)}.");
            }
        }

        // The distance within which a player can "see" other entities
        private const float AOI_RADIUS = Shared.Constants.GameRules.AoiRadius;

        /// <summary>
        /// Called once per tick by the GameLogic loop.
        /// Iterates over all entities to process their physics, movement, or AI.
        /// </summary>
        public void Update()
        {
            // Process Player logic
            foreach (var player in Players.Values)
            {
                player.Update();
            }

            // Process NPC logic
            foreach (var npc in NPCs.Values)
            {
                npc.Update();
            }

            // Process Resource logic (Respawn timers)
            foreach (var res in Resources.Values)
            {
                res.Update();
            }

            // Process Loot Satchels (Decay and Empty despawn)
            List<int>? expiredSatchels = null;
            foreach (var satchel in LootSatchels.Values)
            {
                if (satchel.IsExpired || satchel.IsEmpty)
                {
                    expiredSatchels ??= new List<int>();
                    expiredSatchels.Add(satchel.Id);
                }
            }
            if (expiredSatchels != null)
            {
                foreach (var id in expiredSatchels)
                {
                    DespawnLootSatchel(id);
                }
            }

            // Replicate health/death changes to AoI observers
            foreach (var player in Players.Values) SyncEntityVitals(player);
            foreach (var npc in NPCs.Values) SyncEntityVitals(npc);
            foreach (var res in Resources.Values) SyncEntityVitals(res);

            // Broadcast state updates to players based on their Area of Interest
            ProcessAreaOfInterest();
        }

        public void Broadcast(Shared.Network.Packet packet, Shared.Math.Vector3 origin)
        {
            foreach (var player in Players.Values)
            {
                if (Shared.Math.Vector3.Distance(player.Position, origin) <= AOI_RADIUS)
                {
                    player.Connection.Send(packet);
                }
            }
        }

        private static Shared.Network.Packet BuildEntityVitals(Entity entity)
        {
            var packet = new Shared.Network.Packet(Shared.Network.OpCode.EntityVitals);
            packet.Write(entity.Id);
            packet.Write(entity.Health);
            packet.Write(entity.MaxHealth);
            return packet;
        }

        private static Shared.Network.Packet BuildEntityDeath(Entity entity)
        {
            var packet = new Shared.Network.Packet(Shared.Network.OpCode.EntityDeath);
            packet.Write(entity.Id);
            return packet;
        }

        /// <summary>
        /// Tells a single observer the current health (and death) state of an entity that just entered its AoI.
        /// </summary>
        private static void SendEntityState(Player observer, Entity entity)
        {
            using var vitals = BuildEntityVitals(entity);
            observer.Connection.Send(vitals);

            if (entity.Health <= 0)
            {
                using var death = BuildEntityDeath(entity);
                observer.Connection.Send(death);
            }
        }

        /// <summary>
        /// Broadcasts EntityVitals to the entity's AoI whenever its health or max health changed since the
        /// last broadcast (combat, regen, respawn, level-up, gear), plus EntityDeath on the alive-to-dead edge.
        /// Runs once per tick, so every code path that changes health is covered without per-site hooks.
        /// </summary>
        private void SyncEntityVitals(Entity entity)
        {
            if (entity.ReplicatedHealth < 0)
            {
                // First sight: observers get the state through SendEntityState on spawn.
                entity.ReplicatedHealth = entity.Health;
                entity.ReplicatedMaxHealth = entity.MaxHealth;
                return;
            }

            if (entity.Health == entity.ReplicatedHealth && entity.MaxHealth == entity.ReplicatedMaxHealth) return;

            bool died = entity.Health <= 0 && entity.ReplicatedHealth > 0;
            entity.ReplicatedHealth = entity.Health;
            entity.ReplicatedMaxHealth = entity.MaxHealth;

            using (var vitals = BuildEntityVitals(entity))
            {
                Broadcast(vitals, entity.Position);
            }

            if (died)
            {
                using var death = BuildEntityDeath(entity);
                Broadcast(death, entity.Position);
            }
        }

        private void ProcessAreaOfInterest()
        {
            // For a first version, we just loop all players and check distance to all entities.
            // In a production MMO, this would use Spatial Partitioning (Grid/QuadTree).
            foreach (var player in Players.Values)
            {
                if (player.AoiResetRequested)
                {
                    player.AoiResetRequested = false;
                    player.KnownEntities.Clear();
                }

                List<Entity> nearbyEntities = new List<Entity>();

                // Check distance to all other players
                foreach (var otherPlayer in Players.Values)
                {
                    if (player.Id == otherPlayer.Id) continue; // Skip self

                    if (Shared.Math.Vector3.Distance(player.Position, otherPlayer.Position) <= AOI_RADIUS)
                    {
                        nearbyEntities.Add(otherPlayer);
                    }
                }

                // Check distance to all NPCs
                foreach (var npc in NPCs.Values)
                {
                    if (npc.CurrentState == Shared.Enums.AIState.Dead && npc.IsCorpseDecayed) continue;

                    if (Shared.Math.Vector3.Distance(player.Position, npc.Position) <= AOI_RADIUS)
                    {
                        nearbyEntities.Add(npc);
                    }
                }

                // Check distance to all Resources
                foreach (var res in Resources.Values)
                {
                    if (Shared.Math.Vector3.Distance(player.Position, res.Position) <= AOI_RADIUS)
                    {
                        // Even if depleted, we still might want players to know it's there (as a stump, or hidden if you prefer).
                        // Let's always add it, and if it's depleted, the client handles the visual state based on its Health.
                        nearbyEntities.Add(res);
                    }
                }

                // Check distance to all Loot Satchels
                foreach (var satchel in LootSatchels.Values)
                {
                    if (satchel.IsEmpty || satchel.IsExpired) continue;

                    if (Shared.Math.Vector3.Distance(player.Position, satchel.Position) <= AOI_RADIUS)
                    {
                        nearbyEntities.Add(satchel);
                    }
                }

                // Now we have everything near the player.
                // 1. Send Spawn for newly discovered entities
                // 2. Send Despawn for entities that left
                // 3. Send Position Update for entities currently in range
                
                HashSet<int> currentNearbyIds = new HashSet<int>();

                foreach (var entity in nearbyEntities)
                {
                    currentNearbyIds.Add(entity.Id);

                    if (!player.KnownEntities.Contains(entity.Id))
                    {
                        // New entity entered AoI!
                        player.KnownEntities.Add(entity.Id);
                        
                        using Shared.Network.Packet spawnPacket = new Shared.Network.Packet(Shared.Network.OpCode.EntitySpawn);
                        spawnPacket.Write(entity.Id);
                        spawnPacket.Write((byte)entity.Type);
                        spawnPacket.Write(entity.PrefabName);
                        spawnPacket.Write(entity.Name);
                        spawnPacket.Write(entity.Position);
                        player.Connection.Send(spawnPacket);

                        // Late joiners must learn the entity's current health/death state
                        SendEntityState(player, entity);
                    }
                    else
                    {
                        // Entity is already known, just update position
                        using Shared.Network.Packet movePacket = new Shared.Network.Packet(Shared.Network.OpCode.EntityPositionUpdate);
                        movePacket.Write(entity.Id);
                        movePacket.Write(entity.Position);
                        player.Connection.Send(movePacket);
                    }
                }

                // Check who left the AoI
                List<int> toRemove = new List<int>();
                foreach (var knownId in player.KnownEntities)
                {
                    if (!currentNearbyIds.Contains(knownId))
                    {
                        // They left!
                        toRemove.Add(knownId);
                        
                        using Shared.Network.Packet despawnPacket = new Shared.Network.Packet(Shared.Network.OpCode.EntityDespawn);
                        despawnPacket.Write(knownId);
                        player.Connection.Send(despawnPacket);
                    }
                }

                // Remove out-of-range entities from known list
                foreach (var id in toRemove)
                {
                    player.KnownEntities.Remove(id);
                }
            }
        }
    }
}


