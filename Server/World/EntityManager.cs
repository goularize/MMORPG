using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Server.World.Entities;

namespace Server.World
{
    public class EntityManager
    {
        // Thread-safe dictionary to hold all active players in the world
        public ConcurrentDictionary<int, Player> Players { get; } = new();
        
        // Thread-safe dictionary for NPCs
        public ConcurrentDictionary<int, NPC> NPCs { get; } = new();

        public void AddPlayer(Player player)
        {
            if (Players.TryAdd(player.Id, player))
            {
                Console.WriteLine($"Player {player.Name} (ID: {player.Id}) joined the world.");
            }
        }

        public void RemovePlayer(int playerId)
        {
            if (Players.TryRemove(playerId, out Player player))
            {
                Console.WriteLine($"Player {player.Name} left the world.");
            }
        }

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
        }
    }
}
