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
            return null;
        }

        public void AddPlayer(Player player)
        {
            if (Players.TryAdd(player.Id, player))
            {
                player.MapId = MapId;
                Console.WriteLine($"Player {player.Name} (ID: {player.Id}) joined Map {MapId}.");
            }
        }

        public void RemovePlayer(int playerId)
        {
            if (Players.TryRemove(playerId, out Player? player))
            {
                // Save character's final position to the DB asynchronously
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using var db = Server.Database.AppDbContext.Factory();
                        var dbChar = db.Characters.Find(player.Id);
                        if (dbChar != null)
                        {
                            dbChar.X = player.Position.X;
                            dbChar.Y = player.Position.Y;
                            dbChar.Z = player.Position.Z;
                            db.SaveChanges();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Error] Failed to save player {player.Name} position on disconnect: {ex.Message}");
                    }
                });

                Console.WriteLine($"Player {player.Name} left Map {MapId} (Position saved).");
            }
        }

        // The distance within which a player can "see" other entities
        private const float AOI_RADIUS = 50.0f;

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

        private void ProcessAreaOfInterest()
        {
            // For a first version, we just loop all players and check distance to all entities.
            // In a production MMO, this would use Spatial Partitioning (Grid/QuadTree).
            foreach (var player in Players.Values)
            {
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
                        spawnPacket.Write(entity.Name);
                        spawnPacket.Write(entity.Position);
                        player.Connection.Send(spawnPacket);
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


