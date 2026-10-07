using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data.Models;
using Shared.Enums;

namespace Server.Data
{
    /// <summary>
    /// Startup validation of the static data tables. It reports every problem it finds (not just the first), so a
    /// designer can fix a data change in one pass instead of one server restart per mistake.
    /// </summary>
    public static class DataValidator
    {
        /// <summary>Errors stop the server from starting; warnings are reported and tolerated.</summary>
        public record Result(List<string> Errors, List<string> Warnings);

        public static Result Validate(
            IReadOnlyList<ItemTemplate> items,
            IReadOnlyList<RecipeTemplate> recipes,
            IReadOnlyList<LootTableTemplate> lootTables,
            IReadOnlyList<NpcTemplate> npcs,
            IReadOnlyList<SpawnerTemplate> spawners,
            CharacterCreationTemplate characterCreation,
            IReadOnlyList<ResourceNodeTemplate>? resourceNodes = null,
            IReadOnlyList<ResourceSpawnTemplate>? resourceSpawns = null)
        {
            resourceNodes ??= Array.Empty<ResourceNodeTemplate>();
            resourceSpawns ??= Array.Empty<ResourceSpawnTemplate>();
            var errors = new List<string>();
            var warnings = new List<string>();

            ReportDuplicates(errors, "Items.json", "TemplateId", items.Select(i => i.TemplateId));
            ReportDuplicates(errors, "Recipes.json", "RecipeId", recipes.Select(r => r.RecipeId));
            ReportDuplicates(errors, "LootTables.json", "NpcTemplateId", lootTables.Select(l => l.NpcTemplateId));
            ReportDuplicates(errors, "Npcs.json", "TemplateId", npcs.Select(n => n.TemplateId));

            var itemIds = items.Select(i => i.TemplateId).ToHashSet();
            var npcIds = npcs.Select(n => n.TemplateId).ToHashSet();

            foreach (var item in items)
            {
                string at = $"Items.json item {item.TemplateId}";
                if (item.TemplateId <= 0) errors.Add($"{at}: TemplateId must be positive.");
                if (string.IsNullOrWhiteSpace(item.Name)) errors.Add($"{at}: Name is empty.");
                if (item.Type == ItemType.Unknown) errors.Add($"{at}: Type is missing or Unknown.");
                if (item.MaxStack < 1) errors.Add($"{at}: MaxStack must be at least 1 (is {item.MaxStack}).");
            }

            foreach (var npc in npcs)
            {
                string at = $"Npcs.json npc {npc.TemplateId}";
                if (npc.TemplateId <= 0) errors.Add($"{at}: TemplateId must be positive.");
                if (string.IsNullOrWhiteSpace(npc.Name)) errors.Add($"{at}: Name is empty.");
                if (!string.Equals(npc.Type, "Enemy", StringComparison.OrdinalIgnoreCase) && !npc.IsFriendly)
                    errors.Add($"{at}: Type '{npc.Type}' must be Enemy or Friendly.");
                if (!Enum.TryParse<MobBehaviorType>(npc.Behavior, true, out _))
                    errors.Add($"{at}: Behavior '{npc.Behavior}' is not one of {string.Join(", ", Enum.GetNames<MobBehaviorType>())}.");
                if (npc.LevelRange is not { Length: 2 } || npc.LevelRange[0] < 1 || npc.LevelRange[0] > npc.LevelRange[1])
                    errors.Add($"{at}: LevelRange must be [min, max] with 1 <= min <= max.");
                if (npc.StatVarianceRange is not { Length: 2 } || npc.StatVarianceRange[0] > npc.StatVarianceRange[1])
                    errors.Add($"{at}: StatVarianceRange must be [min, max] with min <= max.");
            }

            foreach (var table in lootTables)
            {
                string at = $"LootTables.json table for npc {table.NpcTemplateId}";
                // Planned content may have its table before its NPC exists, so this is not fatal (see S4-G5)
                if (!npcIds.Contains(table.NpcTemplateId)) warnings.Add($"{at}: no NPC template has this id, the table is unused.");
                if (table.MinGold < 0 || table.MinGold > table.MaxGold) errors.Add($"{at}: gold range must satisfy 0 <= MinGold <= MaxGold.");
                if (table.GoldChance < 0f || table.GoldChance > 1f) errors.Add($"{at}: GoldChance must be between 0 and 1.");

                foreach (var entry in table.Entries)
                {
                    if (!itemIds.Contains(entry.ItemTemplateId)) errors.Add($"{at}: entry references unknown item {entry.ItemTemplateId}.");
                    if (entry.DropChance < 0f || entry.DropChance > 1f) errors.Add($"{at}: item {entry.ItemTemplateId} DropChance must be between 0 and 1.");
                    if (entry.MinQuantity < 1 || entry.MinQuantity > entry.MaxQuantity) errors.Add($"{at}: item {entry.ItemTemplateId} quantity range must satisfy 1 <= Min <= Max.");
                }
            }

            foreach (var recipe in recipes)
            {
                string at = $"Recipes.json recipe {recipe.RecipeId}";
                if (!itemIds.Contains(recipe.ResultItemTemplateId)) errors.Add($"{at}: result references unknown item {recipe.ResultItemTemplateId}.");
                if (recipe.ResultQuantity < 1) errors.Add($"{at}: ResultQuantity must be at least 1.");
                if (recipe.RequiredGold < 0) errors.Add($"{at}: RequiredGold cannot be negative.");
                if (recipe.Ingredients.Count == 0) errors.Add($"{at}: has no ingredients.");
                foreach (var ingredient in recipe.Ingredients)
                {
                    if (!itemIds.Contains(ingredient.ItemTemplateId)) errors.Add($"{at}: ingredient references unknown item {ingredient.ItemTemplateId}.");
                    if (ingredient.Quantity < 1) errors.Add($"{at}: ingredient {ingredient.ItemTemplateId} quantity must be at least 1.");
                }
            }

            for (int i = 0; i < spawners.Count; i++)
            {
                var spawner = spawners[i];
                string at = $"Spawners.json spawner #{i} (map {spawner.MapId})";
                if (!npcIds.Contains(spawner.TemplateId)) errors.Add($"{at}: references unknown NPC template {spawner.TemplateId}.");
                if (spawner.Amount < 1) errors.Add($"{at}: Amount must be at least 1.");
                if (spawner.Radius < 0f) errors.Add($"{at}: Radius cannot be negative.");
            }

            ReportDuplicates(errors, "ResourceNodes.json", "TemplateId", resourceNodes.Select(r => r.TemplateId));
            foreach (var node in resourceNodes)
            {
                string at = $"ResourceNodes.json node {node.TemplateId}";
                if (node.TemplateId <= 0) errors.Add($"{at}: TemplateId must be positive.");
                if (string.IsNullOrWhiteSpace(node.Name)) errors.Add($"{at}: Name is empty.");
                if (node.MaxHealth < 1) errors.Add($"{at}: MaxHealth must be at least 1.");
                if (node.Defense < 0) errors.Add($"{at}: Defense cannot be negative.");
                if (node.RespawnTimeSeconds < 0f) errors.Add($"{at}: RespawnTimeSeconds cannot be negative.");
                if (node.Drops.Count == 0) warnings.Add($"{at}: has no drops, harvesting it yields nothing.");

                foreach (var entry in node.Drops)
                {
                    if (!itemIds.Contains(entry.ItemTemplateId)) errors.Add($"{at}: drop references unknown item {entry.ItemTemplateId}.");
                    if (entry.DropChance < 0f || entry.DropChance > 1f) errors.Add($"{at}: item {entry.ItemTemplateId} DropChance must be between 0 and 1.");
                    if (entry.MinQuantity < 1 || entry.MinQuantity > entry.MaxQuantity) errors.Add($"{at}: item {entry.ItemTemplateId} quantity range must satisfy 1 <= Min <= Max.");
                }
            }

            var nodeIds = resourceNodes.Select(r => r.TemplateId).ToHashSet();
            for (int i = 0; i < resourceSpawns.Count; i++)
            {
                if (!nodeIds.Contains(resourceSpawns[i].TemplateId))
                    errors.Add($"ResourceSpawns.json spawn #{i} (map {resourceSpawns[i].MapId}): references unknown resource node {resourceSpawns[i].TemplateId}.");
            }

            ValidateCharacterCreation(errors, characterCreation, items);

            return new Result(errors, warnings);
        }

        private static void ValidateCharacterCreation(List<string> errors, CharacterCreationTemplate c, IReadOnlyList<ItemTemplate> items)
        {
            const string at = "CharacterCreation.json";
            if (c.Health < 1) errors.Add($"{at}: Health must be at least 1.");
            if (c.Mana < 0) errors.Add($"{at}: Mana cannot be negative.");
            if (c.Strength < 0 || c.Intelligence < 0 || c.Constitution < 0 || c.Knowledge < 0) errors.Add($"{at}: base stats cannot be negative.");
            if (c.Gold < 0) errors.Add($"{at}: Gold cannot be negative.");
            if (c.StartMapId < 1) errors.Add($"{at}: StartMapId must be positive.");

            if (c.StarterItems.Count > ServerConfig.DefaultBackpackSlots)
                errors.Add($"{at}: {c.StarterItems.Count} starter items do not fit in the {ServerConfig.DefaultBackpackSlots}-slot backpack.");

            var itemsById = items.GroupBy(i => i.TemplateId).ToDictionary(g => g.Key, g => g.First());
            foreach (var starter in c.StarterItems)
            {
                if (!itemsById.TryGetValue(starter.TemplateId, out var template))
                    errors.Add($"{at}: starter item references unknown item {starter.TemplateId}.");
                else if (starter.Quantity < 1 || starter.Quantity > Math.Max(1, template.MaxStack))
                    errors.Add($"{at}: starter item {starter.TemplateId} quantity must be between 1 and its MaxStack ({template.MaxStack}).");
            }
        }

        private static void ReportDuplicates(List<string> errors, string file, string idName, IEnumerable<int> ids)
        {
            foreach (var group in ids.GroupBy(id => id).Where(g => g.Count() > 1))
                errors.Add($"{file}: {idName} {group.Key} is defined {group.Count()} times.");
        }
    }
}
