using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Server.Data.Models;

namespace Server.Data
{
    public static class DataManager
    {
        public static Dictionary<int, NpcTemplate> Npcs { get; private set; } = new();
        public static List<SpawnerTemplate> Spawners { get; private set; } = new();

        public static void Initialize()
        {
            try
            {
                // Load NPCs
                string npcsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Npcs.json");
                if (File.Exists(npcsPath))
                {
                    string npcsJson = File.ReadAllText(npcsPath);
                    var npcList = JsonSerializer.Deserialize<List<NpcTemplate>>(npcsJson);
                    if (npcList != null)
                    {
                        foreach (var npc in npcList)
                        {
                            Npcs.TryAdd(npc.TemplateId, npc);
                        }
                    }
                    Console.WriteLine($"[DataManager] Loaded {Npcs.Count} NPC templates.");
                }
                else
                {
                    Console.WriteLine("[DataManager] Warning: Npcs.json not found.");
                }

                // Load Spawners
                string spawnersPath = Path.Combine(AppContext.BaseDirectory, "Data", "Spawners.json");
                if (File.Exists(spawnersPath))
                {
                    string spawnersJson = File.ReadAllText(spawnersPath);
                    var spawnerList = JsonSerializer.Deserialize<List<SpawnerTemplate>>(spawnersJson);
                    if (spawnerList != null)
                    {
                        Spawners.AddRange(spawnerList);
                    }
                    Console.WriteLine($"[DataManager] Loaded {Spawners.Count} Spawners.");
                }
                else
                {
                    Console.WriteLine("[DataManager] Warning: Spawners.json not found.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DataManager] Critical Error loading static data: {ex.Message}");
            }
        }
    }
}
