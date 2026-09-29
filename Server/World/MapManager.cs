using System.Collections.Concurrent;
using Server.World.Entities;

namespace Server.World
{
    public class MapManager
    {
        public ConcurrentDictionary<int, MapInstance> ActiveMaps { get; } = new();

        public MapManager()
        {
            // Initialize basic default map(s)
            // In a full implementation, this would load from JSON configurations
            ActiveMaps.TryAdd(1, new MapInstance(1));
            ActiveMaps.TryAdd(2, new MapInstance(2)); // Just as an example for multiple maps
        }

        public MapInstance? GetMap(int mapId)
        {
            ActiveMaps.TryGetValue(mapId, out var map);
            return map;
        }

        public Player? GetPlayer(int playerId)
        {
            foreach (var map in ActiveMaps.Values)
            {
                if (map.Players.TryGetValue(playerId, out var player))
                {
                    return player;
                }
            }
            return null;
        }

        public void AddPlayer(Player player)
        {
            var mapId = player.MapId > 0 ? player.MapId : 1;
            if (ActiveMaps.TryGetValue(mapId, out var map))
            {
                map.AddPlayer(player);
            }
            else
            {
                System.Console.WriteLine($"[Error] Cannot add player to non-existent Map {mapId}");
            }
        }

        public void RemovePlayer(int playerId)
        {
            foreach (var map in ActiveMaps.Values)
            {
                if (map.Players.ContainsKey(playerId))
                {
                    map.RemovePlayer(playerId);
                    break;
                }
            }
        }

        public void Update()
        {
            foreach (var map in ActiveMaps.Values)
            {
                map.Update();
            }
        }
    }
}
