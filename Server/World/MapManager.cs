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

    public class ColliderExportData
    {
        public string Type { get; set; } = string.Empty;
        public Vector2Data Position { get; set; } = new Vector2Data();
        public Vector2Data Size { get; set; } = new Vector2Data();
        public float Radius { get; set; }
        public List<Vector2Data> Vertices { get; set; } = new();
    }

    public class ResourceSpawnerExportData
    {
        public string Type { get; set; } = string.Empty;
        public Vector2Data Position { get; set; } = new Vector2Data();
        public float RespawnTimeSeconds { get; set; }
    }

    public class MapExportData
    {
        public string MapName { get; set; } = string.Empty;
        public int MaxPlayers { get; set; }
        public List<ColliderExportData> Colliders { get; set; } = new();
        public List<ResourceSpawnerExportData> ResourceSpawners { get; set; } = new();
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
            // If running via dotnet run, the directory structure might be slightly different.
            // Let's resolve safely based on current working directory.
            if (!Directory.Exists(mapsDir))
            {
                mapsDir = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Maps");
            }

            if (!Directory.Exists(mapsDir))
            {
                Console.WriteLine($"[MapManager] Maps directory not found at {mapsDir}. Using default maps.");
                return;
            }

            var files = Directory.GetFiles(mapsDir, "Map_*_Config.json");
            int resourceIdCounter = 100000; // Offset IDs to avoid colliding with Players/NPCs

            foreach (var file in files)
            {
                try
                {
                    // Extract ID from Map_X_Config.json
                    string fileName = Path.GetFileName(file);
                    string idPart = fileName.Replace("Map_", "").Replace("_Config.json", "");
                    if (int.TryParse(idPart, out int mapId))
                    {
                        string json = File.ReadAllText(file);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var mapData = JsonSerializer.Deserialize<MapExportData>(json, options);

                        if (mapData != null)
                        {
                            var mapInstance = new MapInstance(mapId);
                            
                            // Load Resources
                            foreach (var spawner in mapData.ResourceSpawners)
                            {
                                int resId = resourceIdCounter++;
                                var position = new Shared.Math.Vector3(spawner.Position.x, spawner.Position.y, 0);
                                var resource = new Resource(resId, spawner.Type, position, spawner.RespawnTimeSeconds);
                                
                                resource.MapId = mapId;
                                mapInstance.Resources.TryAdd(resId, resource);
                            }

                            // Load Colliders
                            foreach (var col in mapData.Colliders)
                            {
                                if (col.Type == "Polygon")
                                {
                                    var vertices = new List<Shared.Math.Vector3>();
                                    foreach (var v in col.Vertices)
                                    {
                                        vertices.Add(new Shared.Math.Vector3(v.x, v.y, 0));
                                    }
                                    mapInstance.Colliders.Add(new Physics.PolygonCollider(vertices));
                                }
                                else if (col.Type == "Box")
                                {
                                    var center = new Shared.Math.Vector3(col.Position.x, col.Position.y, 0);
                                    var size = new Shared.Math.Vector3(col.Size.x, col.Size.y, 0);
                                    mapInstance.Colliders.Add(new Physics.BoxCollider(center, size));
                                }
                                else if (col.Type == "Circle")
                                {
                                    var center = new Shared.Math.Vector3(col.Position.x, col.Position.y, 0);
                                    mapInstance.Colliders.Add(new Physics.CircleCollider(center, col.Radius));
                                }
                            }

                            ActiveMaps.TryAdd(mapId, mapInstance);
                            Console.WriteLine($"[MapManager] Loaded Map {mapId} ({mapData.MapName}) with {mapData.ResourceSpawners.Count} resources and {mapData.Colliders.Count} colliders.");
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
