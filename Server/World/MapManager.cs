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
        private static int _nextNpcEntityId = 100000;

        public MapManager()
        {
            LoadMapsFromDisk();
            
            // Fallbacks in case no JSON files exist yet
            if (!ActiveMaps.ContainsKey(1)) ActiveMaps.TryAdd(1, new MapInstance(1));
            if (!ActiveMaps.ContainsKey(2)) ActiveMaps.TryAdd(2, new MapInstance(2));

            SpawnInitialSpawners();
        }

        public void SpawnInitialSpawners()
        {
            SpawnSpawners(Data.DataManager.Spawners, Data.DataManager.Npcs);
        }

        // Spawns every spawner's NPCs into the map named by its MapId. Returns the number of NPCs spawned.
        public int SpawnSpawners(IEnumerable<Data.Models.SpawnerTemplate> spawners, IReadOnlyDictionary<int, Data.Models.NpcTemplate> npcTemplates)
        {
            int spawned = 0;
            var rng = new Random();
            foreach (var spawner in spawners)
            {
                if (!npcTemplates.TryGetValue(spawner.TemplateId, out var template))
                {
                    Console.WriteLine($"[MapManager] Spawner skipped: unknown NPC template {spawner.TemplateId} (Map {spawner.MapId}).");
                    continue;
                }

                if (!ActiveMaps.TryGetValue(spawner.MapId, out var map))
                {
                    Console.WriteLine($"[MapManager] Spawner skipped: map {spawner.MapId} does not exist (NPC template {spawner.TemplateId}).");
                    continue;
                }

                int count = Math.Max(1, spawner.Amount);
                for (int i = 0; i < count; i++)
                {
                    int entityId = System.Threading.Interlocked.Increment(ref _nextNpcEntityId);
                    var npc = new NPC(entityId, template.Name)
                    {
                        TemplateId = template.TemplateId,
                        BehaviorType = template.BehaviorType,
                        AggroRadius = template.AggroRadius,
                        WanderRadius = template.WanderRadius,
                        LeashRadius = template.LeashRadius,
                        RespawnTimeSeconds = template.RespawnTimeSeconds,
                        WalkSpeed = template.WalkSpeed,
                        RunSpeed = template.RunSpeed,
                        AttackRange = template.AttackRange,
                        PackAssistRadius = template.PackAssistRadius,
                        Strength = template.BaseStrength,
                        Intelligence = template.BaseIntelligence,
                        Constitution = template.BaseConstitution,
                        Knowledge = template.BaseKnowledge,
                        ExpYield = (long)(template.BaseExp * Math.Max(1, template.LevelRange[0]))
                    };

                    Shared.Math.Vector3 spawnPos;
                    if (string.Equals(spawner.Type, "Area", StringComparison.OrdinalIgnoreCase) && spawner.Radius > 0)
                    {
                        float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                        float dist = (float)(rng.NextDouble() * spawner.Radius);
                        spawnPos = new Shared.Math.Vector3(
                            spawner.X + (float)Math.Cos(angle) * dist,
                            spawner.Y + (float)Math.Sin(angle) * dist,
                            spawner.Z
                        );
                    }
                    else
                    {
                        spawnPos = new Shared.Math.Vector3(spawner.X, spawner.Y, spawner.Z);
                    }

                    npc.SpawnPosition = spawnPos;
                    npc.Position = spawnPos;
                    npc.CalculateDerivedStats();
                    npc.Health = npc.MaxHealth;
                    npc.Mana = npc.MaxMana;

                    map.AddNPC(npc);
                    spawned++;
                }
            }

            return spawned;
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
