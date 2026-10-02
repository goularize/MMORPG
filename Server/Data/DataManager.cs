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
        public static Dictionary<int, ItemTemplate> Items { get; private set; } = new();
        public static Dictionary<int, RecipeTemplate> Recipes { get; private set; } = new();
        public static Dictionary<int, LootTableTemplate> LootTables { get; private set; } = new();

        public static void Initialize()
        {
            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                };

                // Load Items
                string itemsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Items.json");
                if (File.Exists(itemsPath))
                {
                    string itemsJson = File.ReadAllText(itemsPath);
                    var itemList = JsonSerializer.Deserialize<List<ItemTemplate>>(itemsJson, jsonOptions);
                    if (itemList != null)
                    {
                        foreach (var item in itemList)
                        {
                            Items.TryAdd(item.TemplateId, item);
                        }
                    }
                    Console.WriteLine($"[DataManager] Loaded {Items.Count} Item templates.");
                }
                else
                {
                    Console.WriteLine("[DataManager] Warning: Items.json not found.");
                }

                // Load Recipes
                string recipesPath = Path.Combine(AppContext.BaseDirectory, "Data", "Recipes.json");
                if (File.Exists(recipesPath))
                {
                    string recipesJson = File.ReadAllText(recipesPath);
                    var recipeList = JsonSerializer.Deserialize<List<RecipeTemplate>>(recipesJson, jsonOptions);
                    if (recipeList != null)
                    {
                        foreach (var recipe in recipeList)
                        {
                            Recipes.TryAdd(recipe.RecipeId, recipe);
                        }
                    }
                    Console.WriteLine($"[DataManager] Loaded {Recipes.Count} Recipes.");
                }
                else
                {
                    Console.WriteLine("[DataManager] Warning: Recipes.json not found.");
                }

                // Load Loot Tables
                string lootTablesPath = Path.Combine(AppContext.BaseDirectory, "Data", "LootTables.json");
                if (File.Exists(lootTablesPath))
                {
                    string lootTablesJson = File.ReadAllText(lootTablesPath);
                    var lootTableList = JsonSerializer.Deserialize<List<LootTableTemplate>>(lootTablesJson, jsonOptions);
                    if (lootTableList != null)
                    {
                        foreach (var lt in lootTableList)
                        {
                            LootTables.TryAdd(lt.NpcTemplateId, lt);
                        }
                    }
                    Console.WriteLine($"[DataManager] Loaded {LootTables.Count} Loot tables.");
                }
                else
                {
                    Console.WriteLine("[DataManager] Warning: LootTables.json not found.");
                }

                // Load NPCs
                string npcsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Npcs.json");
                if (File.Exists(npcsPath))
                {
                    string npcsJson = File.ReadAllText(npcsPath);
                    var npcList = JsonSerializer.Deserialize<List<NpcTemplate>>(npcsJson, jsonOptions);
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
                    var spawnerList = JsonSerializer.Deserialize<List<SpawnerTemplate>>(spawnersJson, jsonOptions);
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
