using System;
using System.Linq;
using Server.Data;
using Server.Database;
using Server.Database.Models;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Network;
using Server.Persistence;

namespace Server.Handlers
{
    public static class CraftingHandler
    {
        public static void HandleLearnRecipe(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            int bagIndex = packet.ReadInt();
            int slotIndex = packet.ReadInt();

            var recipeItem = player.Inventory.FirstOrDefault(i => i.BagIndex == bagIndex && i.SlotIndex == slotIndex);
            if (recipeItem == null) return;

            if (!DataManager.Items.TryGetValue(recipeItem.TemplateId, out var itemTemplate) || itemTemplate.Type != ItemType.Recipe)
            {
                return;
            }

            // Recipe ID matches RecipeItem TemplateId (or mapped via recipe catalog)
            int recipeId = recipeItem.TemplateId;
            if (!DataManager.Recipes.ContainsKey(recipeId)) return;

            if (player.LearnedRecipes.Contains(recipeId))
            {
                Console.WriteLine($"[Crafting] Player {player.Name} already knows recipe {recipeId}.");
                return;
            }

            // Consume recipe item
            recipeItem.Quantity--;
            if (recipeItem.Quantity <= 0)
            {
                player.Inventory.Remove(recipeItem);
                InventoryHandler.DeleteDbItem(recipeItem);
                InventoryHandler.SendInventorySlotUpdate(player, null, bagIndex, slotIndex);
            }
            else
            {
                InventoryHandler.SaveDbItem(recipeItem);
                InventoryHandler.SendInventorySlotUpdate(player, recipeItem, bagIndex, slotIndex);
            }

            // Register learned recipe
            player.LearnedRecipes.Add(recipeId);

            // Learned recipes are durable: queue the row together with the consumed recipe item
            PersistenceService.Instance.QueueRecipe(player.Id, recipeId, urgent: true);
            player.QueueSave(urgent: true);

            Console.WriteLine($"[Crafting] {player.Name} successfully learned recipe: {itemTemplate.Name}.");
        }

        public static void HandleCraftItem(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            int recipeId = packet.ReadInt();

            if (!DataManager.Recipes.TryGetValue(recipeId, out var recipe))
            {
                SendCraftResponse(player, false, 0, "Recipe not found.");
                return;
            }

            if (!player.LearnedRecipes.Contains(recipeId))
            {
                SendCraftResponse(player, false, 0, "You haven't learned this recipe yet.");
                return;
            }

            if (player.Gold < recipe.RequiredGold)
            {
                SendCraftResponse(player, false, 0, $"Insufficient gold. Requires {recipe.RequiredGold} Gold.");
                return;
            }

            // Check if player has all ingredients
            foreach (var ingredient in recipe.Ingredients)
            {
                int inStock = player.Inventory.Where(i => i.TemplateId == ingredient.ItemTemplateId).Sum(i => i.Quantity);
                if (inStock < ingredient.Quantity)
                {
                    string ingredientName = DataManager.Items.TryGetValue(ingredient.ItemTemplateId, out var ingTemplate) ? ingTemplate.Name : "Material";
                    SendCraftResponse(player, false, 0, $"Missing materials: {ingredientName} ({inStock}/{ingredient.Quantity}).");
                    return;
                }
            }

            // Check for empty inventory slot
            int? freeSlot = InventoryHandler.FindFirstEmptySlot(player);
            if (!freeSlot.HasValue)
            {
                SendCraftResponse(player, false, 0, "Inventory is full. Make room before crafting.");
                return;
            }

            // Instantiate the result first so a failure aborts before any cost is charged
            var craftedItem = ItemFactory.CreateItem(recipe.ResultItemTemplateId, player.Id, recipe.ResultQuantity);
            if (craftedItem == null)
            {
                SendCraftResponse(player, false, 0, "Failed to instantiate crafted item.");
                return;
            }

            // Deduct Gold & Materials
            player.Gold -= recipe.RequiredGold;
            foreach (var ingredient in recipe.Ingredients)
            {
                DeductMaterial(player, ingredient.ItemTemplateId, ingredient.Quantity);
                GameEvents.Instance.RaiseItemLost(player, ingredient.ItemTemplateId, ingredient.Quantity);
            }

            craftedItem.BagIndex = 0;
            craftedItem.SlotIndex = freeSlot.Value;
            player.Inventory.Add(craftedItem);

            InventoryHandler.SaveDbItem(craftedItem);
            InventoryHandler.SendInventorySlotUpdate(player, craftedItem, 0, freeSlot.Value);
            GameEvents.Instance.RaiseItemGained(player, craftedItem.TemplateId, craftedItem.Quantity);

            player.QueueSave(urgent: true); // crafting creates and destroys value

            string resultName = DataManager.Items.TryGetValue(recipe.ResultItemTemplateId, out var resTemplate) ? resTemplate.Name : "Item";
            SendCraftResponse(player, true, recipe.ResultItemTemplateId, $"Successfully crafted {resultName}!");
        }

        private static void DeductMaterial(Player player, int templateId, int quantity)
        {
            int remaining = quantity;
            var matchingItems = player.Inventory.Where(i => i.TemplateId == templateId).ToList();

            foreach (var item in matchingItems)
            {
                if (item.Quantity <= remaining)
                {
                    remaining -= item.Quantity;
                    player.Inventory.Remove(item);
                    InventoryHandler.DeleteDbItem(item);
                    InventoryHandler.SendInventorySlotUpdate(player, null, item.BagIndex, item.SlotIndex);
                }
                else
                {
                    item.Quantity -= remaining;
                    remaining = 0;
                    InventoryHandler.SaveDbItem(item);
                    InventoryHandler.SendInventorySlotUpdate(player, item, item.BagIndex, item.SlotIndex);
                }

                if (remaining <= 0) break;
            }
        }

        private static void SendCraftResponse(Player player, bool success, int resultTemplateId, string message)
        {
            using Packet response = new Packet(OpCode.CraftItemResponse);
            response.Write(success);
            response.Write(resultTemplateId);
            response.Write(message);
            player.Connection.Send(response);
        }
    }
}
