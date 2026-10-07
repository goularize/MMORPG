using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Server.Data.Models;

namespace Server.Data
{
    /// <summary>Thrown when static data is missing, malformed or inconsistent; carries every problem found.</summary>
    public class DataLoadException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public DataLoadException(IReadOnlyList<string> errors)
            : base($"Static data is invalid ({errors.Count} error(s)):{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", errors)}")
        {
            Errors = errors;
        }
    }

    public static class DataManager
    {
        public static Dictionary<int, NpcTemplate> Npcs { get; private set; } = new();
        public static List<SpawnerTemplate> Spawners { get; private set; } = new();
        public static Dictionary<int, ResourceNodeTemplate> ResourceNodes { get; private set; } = new();
        public static List<ResourceSpawnTemplate> ResourceSpawns { get; private set; } = new();
        public static Dictionary<int, ItemTemplate> Items { get; private set; } = new();
        public static Dictionary<int, RecipeTemplate> Recipes { get; private set; } = new();
        public static Dictionary<int, LootTableTemplate> LootTables { get; private set; } = new();
        public static CharacterCreationTemplate CharacterCreation { get; private set; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        /// <summary>
        /// Loads and validates every data table, then replaces the live tables. Fails fast: if any file is missing,
        /// malformed or inconsistent, a <see cref="DataLoadException"/> lists every problem and the live tables are
        /// left untouched, so the server never runs on empty or half-loaded data.
        /// </summary>
        public static void Initialize(string? dataDirectory = null)
        {
            dataDirectory ??= Path.Combine(AppContext.BaseDirectory, "Data");
            var errors = new List<string>();

            var items = Read<ItemTemplate>(dataDirectory, "Items.json", errors);
            var recipes = Read<RecipeTemplate>(dataDirectory, "Recipes.json", errors);
            var lootTables = Read<LootTableTemplate>(dataDirectory, "LootTables.json", errors);
            var npcs = Read<NpcTemplate>(dataDirectory, "Npcs.json", errors);
            var spawners = Read<SpawnerTemplate>(dataDirectory, "Spawners.json", errors);
            var resourceNodes = Read<ResourceNodeTemplate>(dataDirectory, "ResourceNodes.json", errors);
            var resourceSpawns = Read<ResourceSpawnTemplate>(dataDirectory, "ResourceSpawns.json", errors);
            var characterCreation = ReadOne<CharacterCreationTemplate>(dataDirectory, "CharacterCreation.json", errors);

            // Cross-references only make sense once every file parsed
            var warnings = new List<string>();
            if (errors.Count == 0)
            {
                var result = DataValidator.Validate(items, recipes, lootTables, npcs, spawners, characterCreation, resourceNodes, resourceSpawns);
                errors.AddRange(result.Errors);
                warnings.AddRange(result.Warnings);
            }

            foreach (var warning in warnings) Console.WriteLine($"[DataManager] Warning: {warning}");

            if (errors.Count > 0)
            {
                throw new DataLoadException(errors);
            }

            Items = items.ToDictionary(i => i.TemplateId);
            Recipes = recipes.ToDictionary(r => r.RecipeId);
            LootTables = lootTables.ToDictionary(l => l.NpcTemplateId);
            Npcs = npcs.ToDictionary(n => n.TemplateId);
            Spawners = spawners;
            ResourceNodes = resourceNodes.ToDictionary(r => r.TemplateId);
            ResourceSpawns = resourceSpawns;
            CharacterCreation = characterCreation;

            Console.WriteLine($"[DataManager] Loaded {Items.Count} items, {Recipes.Count} recipes, {LootTables.Count} loot tables, {Npcs.Count} NPC templates, {Spawners.Count} spawners, {ResourceNodes.Count} resource node types, {ResourceSpawns.Count} resource spawns.");
        }

        private static T ReadOne<T>(string directory, string fileName, List<string> errors) where T : new()
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"{fileName}: file not found at {path}.");
                return new T();
            }

            try
            {
                var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
                if (value != null) return value;
                errors.Add($"{fileName}: file is empty or 'null'.");
            }
            catch (JsonException ex)
            {
                errors.Add($"{fileName}: malformed JSON ({ex.Message}).");
            }
            return new T();
        }

        private static List<T> Read<T>(string directory, string fileName, List<string> errors)
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
            {
                errors.Add($"{fileName}: file not found at {path}.");
                return new List<T>();
            }

            try
            {
                var list = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), JsonOptions);
                if (list == null)
                {
                    errors.Add($"{fileName}: file is empty or 'null'.");
                    return new List<T>();
                }
                if (list.Any(entry => entry == null))
                {
                    errors.Add($"{fileName}: contains a null entry.");
                    return new List<T>();
                }
                return list;
            }
            catch (JsonException ex)
            {
                errors.Add($"{fileName}: malformed JSON ({ex.Message}).");
                return new List<T>();
            }
        }
    }
}
