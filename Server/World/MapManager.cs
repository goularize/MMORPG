using System;
using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Collections.Generic;
using Server.World.Entities;

namespace Server.World
{
    // DTOs for JSON Parsing
    public class Vector2Data
    {
        public float x { get; set; }
        public float y { get; set; }
    }

    public class PolygonData
    {
        public List<Vector2Data> Points { get; set; } = new();
    }

    public class MapExportData
    {
        public string MapName { get; set; } = string.Empty;
        public List<PolygonData> Colliders { get; set; } = new();
    }

    public class MapManager
    {
        public ConcurrentDictionary<int, MapInstance> ActiveMaps { get; } = new();

        public MapManager()
        {
            LoadMapsFromDisk();
            
            // Fallbacks in case no JSON files exist yet
            if (!ActiveMaps.ContainsKey(1)) ActiveMaps.TryAdd(1, new MapInstance(1));
            if (!ActiveMaps.ContainsKey(2)) ActiveMaps.TryAdd(2, new MapInstance(2));
        }

        private void LoadMapsFromDisk()
        {
            // The JSON files are in Server/Data/Maps
            string mapsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Data", "Maps");
            if (!Directory.Exists(mapsDir))
            {
                mapsDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Maps");
            }

            if (!Directory.Exists(mapsDir))
            {
                Console.WriteLine($"[MapManager] Maps directory not found at {mapsDir}. Using default maps.");
                return;
            }

            // Read our new JSON files
            var files = Directory.GetFiles(mapsDir, "*.json");

            foreach (var file in files)
            {
                try
                {
                    // Map "01_StartingVillage" to MapId 1
                    string fileName = Path.GetFileName(file);
                    string idPart = fileName.Split('_')[0]; // Gets "01"
                    if (int.TryParse(idPart, out int mapId))
                    {
                        string json = File.ReadAllText(file);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var mapData = JsonSerializer.Deserialize<MapExportData>(json, options);

                        if (mapData != null)
                        {
                            var mapInstance = new MapInstance(mapId);
                            
                            // Load Colliders from JSON into the Server's Physics Engine
                            foreach (var poly in mapData.Colliders)
                            {
                                var vertices = new List<Shared.Math.Vector3>();
                                foreach (var pt in poly.Points)
                                {
                                    // 2D tile maps map exactly to 3D world space (Z=0)
                                    vertices.Add(new Shared.Math.Vector3(pt.x, pt.y, 0));
                                }
                                mapInstance.Colliders.Add(new Physics.PolygonCollider(vertices));
                            }

                            ActiveMaps.TryAdd(mapId, mapInstance);
                            Console.WriteLine($"[MapManager] Loaded Map {mapId} ({mapData.MapName}) with {mapInstance.Colliders.Count} polygon colliders.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Failed to load map config {file}: {ex.Message}");
                }
            }
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
                Console.WriteLine($"[Error] Cannot add player to non-existent Map {mapId}");
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
