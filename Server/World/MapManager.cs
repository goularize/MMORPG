using System;
using System.IO;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Thrown when map data is missing, malformed or referenced by data but absent; lists every problem.</summary>
    public class MapLoadException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public MapLoadException(IReadOnlyList<string> errors)
            : base($"Map data is invalid ({errors.Count} error(s)):{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", errors)}")
        {
            Errors = errors;
        }
    }

    public class MapManager
    {
        public ConcurrentDictionary<int, MapInstance> ActiveMaps { get; } = new();

        /// <param name="mapsDirectory">Folder with the map JSON files; defaults to the "Data/Maps" folder next to the executable.</param>
        /// <exception cref="MapLoadException">The maps are missing or invalid, or data references a map that does not exist.</exception>
        public MapManager(string? mapsDirectory = null)
        {
            LoadMapsFromDisk(mapsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Data", "Maps"));

            var missing = FindMissingMapReferences(Data.DataManager.Spawners, Data.DataManager.CharacterCreation.StartMapId, Data.DataManager.ResourceSpawns);
            if (missing.Count > 0) throw new MapLoadException(missing);

            SpawnInitialSpawners();
            SpawnResourceNodes(Data.DataManager.ResourceSpawns, Data.DataManager.ResourceNodes);
        }

        /// <summary>Spawners and the character start point must name a loaded map; a typo should stop startup, not skip silently.</summary>
        public List<string> FindMissingMapReferences(IEnumerable<Data.Models.SpawnerTemplate> spawners, int startMapId, IEnumerable<Data.Models.ResourceSpawnTemplate>? resourceSpawns = null)
        {
            var errors = new List<string>();
            if (!ActiveMaps.ContainsKey(startMapId))
                errors.Add($"CharacterCreation.json: StartMapId {startMapId} does not match any map file.");

            foreach (var group in spawners.Where(sp => !ActiveMaps.ContainsKey(sp.MapId)).GroupBy(sp => sp.MapId))
                errors.Add($"Spawners.json: {group.Count()} spawner(s) reference map {group.Key}, which does not match any map file.");

            if (resourceSpawns != null)
            {
                foreach (var group in resourceSpawns.Where(sp => !ActiveMaps.ContainsKey(sp.MapId)).GroupBy(sp => sp.MapId))
                    errors.Add($"ResourceSpawns.json: {group.Count()} spawn(s) reference map {group.Key}, which does not match any map file.");
            }

            return errors;
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
                    int entityId = EntityIdAllocator.Next(Shared.Constants.EntityIdKind.Npc);
                    var npc = new NPC(entityId, template.Name);
                    npc.ApplyTemplate(template, rng);

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

                    map.AddNPC(npc);
                    spawned++;
                }
            }

            return spawned;
        }

        /// <summary>Places every resource node on the map named by its MapId. Returns the number of nodes spawned.</summary>
        public int SpawnResourceNodes(IEnumerable<Data.Models.ResourceSpawnTemplate> spawns, IReadOnlyDictionary<int, Data.Models.ResourceNodeTemplate> nodeTemplates)
        {
            int spawned = 0;
            foreach (var spawn in spawns)
            {
                if (!nodeTemplates.TryGetValue(spawn.TemplateId, out var template))
                {
                    Console.WriteLine($"[MapManager] Resource spawn skipped: unknown resource node {spawn.TemplateId} (Map {spawn.MapId}).");
                    continue;
                }

                if (!ActiveMaps.TryGetValue(spawn.MapId, out var map))
                {
                    Console.WriteLine($"[MapManager] Resource spawn skipped: map {spawn.MapId} does not exist (resource node {spawn.TemplateId}).");
                    continue;
                }

                int entityId = EntityIdAllocator.Next(Shared.Constants.EntityIdKind.Resource);
                var node = new Resource(entityId, template, new Shared.Math.Vector3(spawn.X, spawn.Y, spawn.Z)) { MapId = map.MapId };
                if (map.Resources.TryAdd(node.Id, node)) spawned++;
            }

            return spawned;
        }

        private void LoadMapsFromDisk(string mapsDir)
        {
            var errors = new List<string>();
            var seenIds = new HashSet<int>();

            if (!Directory.Exists(mapsDir))
            {
                throw new MapLoadException(new[] { $"Maps directory not found at {mapsDir}." });
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var files = Directory.GetFiles(mapsDir, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0) errors.Add($"No map files (*.json) found in {mapsDir}.");

            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);

                // Map "01_StartingVillage" to MapId 1
                if (!int.TryParse(fileName.Split('_')[0], out int mapId) || mapId < 1)
                {
                    errors.Add($"{fileName}: the file name must start with the map id (e.g. \"01_StartingVillage.json\").");
                    continue;
                }

                // A duplicate is reported, but its content is still checked so all problems show up in one run
                bool duplicate = !seenIds.Add(mapId);
                if (duplicate) errors.Add($"{fileName}: map id {mapId} is already used by another file.");

                MapExportData? mapData;
                try
                {
                    mapData = JsonSerializer.Deserialize<MapExportData>(File.ReadAllText(file), options);
                }
                catch (JsonException ex)
                {
                    errors.Add($"{fileName}: malformed JSON ({ex.Message}).");
                    continue;
                }

                if (mapData == null)
                {
                    errors.Add($"{fileName}: file is empty or 'null'.");
                    continue;
                }

                var mapInstance = new MapInstance(mapId);
                bool valid = true;
                for (int i = 0; i < mapData.Colliders.Count; i++)
                {
                    var points = mapData.Colliders[i].Points;
                    if (points.Count < 3)
                    {
                        errors.Add($"{fileName}: collider #{i} has {points.Count} point(s); a polygon needs at least 3.");
                        valid = false;
                        continue;
                    }

                    // 2D tile maps map exactly to 3D world space (Z=0)
                    var vertices = points.Select(pt => new Shared.Math.Vector3(pt.x, pt.y, 0)).ToList();
                    mapInstance.Colliders.Add(new Physics.PolygonCollider(vertices));
                }

                if (!valid || duplicate) continue;

                ActiveMaps.TryAdd(mapId, mapInstance);
                Console.WriteLine($"[MapManager] Loaded Map {mapId} ({mapData.MapName}) with {mapInstance.Colliders.Count} polygon colliders.");
            }

            if (errors.Count > 0) throw new MapLoadException(errors);
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

        /// <returns>True when the player is now on a map; false if the map does not exist or the player is already online.</returns>
        public bool AddPlayer(Player player)
        {
            var mapId = player.MapId > 0 ? player.MapId : 1;
            if (!ActiveMaps.TryGetValue(mapId, out var map))
            {
                Console.WriteLine($"[Error] Cannot add player to non-existent Map {mapId}");
                return false;
            }

            return map.AddPlayer(player);
        }

        /// <summary>True when leaving now (disconnect or logout) keeps the character in the world as a combat-log penalty.</summary>
        public static bool WouldLinger(Player player)
            => player.Health > 0 && ServerConfig.CombatLogoutLingerSeconds > 0 && player.IsCombatTagged;

        /// <summary>
        /// Takes the client's player off its connection: removes it from the world, or, when the client dropped
        /// mid-combat, leaves it lingering (see <see cref="Player.BeginLinger"/>). Only if that exact connection owns the player in the world. A stale
        /// connection must never remove the player a newer session of the same character has since put there.
        /// </summary>
        public bool RemovePlayerOwnedBy(Server.Network.IClientConnection client)
        {
            if (!client.PlayerId.HasValue) return false;

            var player = GetPlayer(client.PlayerId.Value);
            if (player == null || !ReferenceEquals(player.Connection, client)) return false;

            // Dropping out of a fight is not an escape: the character stays (defenseless) until the linger ends
            if (WouldLinger(player))
            {
                player.BeginLinger();
                Console.WriteLine($"[CombatLog] {player.Name} disconnected in combat; staying in the world for {ServerConfig.CombatLogoutLingerSeconds:0.#}s.");
                return true;
            }

            RemovePlayer(player.Id);
            return true;
        }

        public void RemovePlayer(int playerId, bool saveState = true)
        {
            foreach (var map in ActiveMaps.Values)
            {
                if (map.Players.ContainsKey(playerId))
                {
                    map.RemovePlayer(playerId, saveState);
                    break;
                }
            }
        }

        /// <summary>Queues a durable save of every online player (used when the server shuts down).</summary>
        public void SaveAllPlayers()
        {
            foreach (var map in ActiveMaps.Values)
            {
                foreach (var player in map.Players.Values) player.QueueSave(urgent: true);
            }
        }

        public void Update()
        {
            foreach (var map in ActiveMaps.Values)
            {
                // A failing map must not stop the others from being simulated this tick
                try
                {
                    map.Update();
                }
                catch (Exception ex)
                {
                    LogThrottle.Warn($"map.update.{map.MapId}", $"[Error] Map {map.MapId} update failed: {ex}");
                }
            }
        }
    }
}
