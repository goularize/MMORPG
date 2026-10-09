using System;
using System.Linq;
using Server.Data;
using Server.Database.Models;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Network;
using Server.Persistence;

namespace Server.Handlers
{
    public static class RefinementHandler
    {
        private static readonly Random _rng = new();

        public const int WEAPON_STONE_ID = 3004;
        public const int ARMOR_STONE_ID = 3005;
        public const int RADIANT_GEM_ID = 3006;

        public static void HandleUpgradeItem(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            bool isEquipped = packet.ReadBool();
            int slotOrBag = packet.ReadInt();
            int slotIndex = packet.ReadInt();

            CharacterItem? item = null;
            EquipmentSlot equippedSlot = EquipmentSlot.None;

            if (isEquipped)
            {
                equippedSlot = (EquipmentSlot)slotOrBag;
                player.EquippedItems.TryGetValue(equippedSlot, out item);
            }
            else
            {
                item = player.Inventory.FirstOrDefault(i => i.BagIndex == slotOrBag && i.SlotIndex == slotIndex);
            }

            if (item == null)
            {
                SendResponse(player, 0, 0, "Item not found.");
                return;
            }

            if (!DataManager.Items.TryGetValue(item.TemplateId, out var template) || template.Type != ItemType.Equipment)
            {
                SendResponse(player, 0, 0, "Only equipment items can be refined.");
                return;
            }

            if (item.UpgradeLevel >= 15)
            {
                SendResponse(player, 2, item.UpgradeLevel, "Item has reached the maximum upgrade level (+15).");
                return;
            }

            // Determine Catalyst requirements
            int catalystId = template.Slot switch
            {
                EquipmentSlot.MainHand or EquipmentSlot.OffHand => WEAPON_STONE_ID,
                EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Feet => ARMOR_STONE_ID,
                _ => RADIANT_GEM_ID
            };

            bool requiresExtraGem = item.UpgradeLevel >= 6;
            long requiredGold = (item.UpgradeLevel + 1) * 50;

            // Check Gold
            if (player.Gold < requiredGold)
            {
                SendResponse(player, 2, item.UpgradeLevel, $"Not enough gold. Requires {requiredGold} Gold.");
                return;
            }

            // Check Catalyst In Stock
            int catalystCount = player.Inventory.Where(i => i.TemplateId == catalystId).Sum(i => i.Quantity);
            if (catalystCount < 1)
            {
                string catalystName = DataManager.Items.TryGetValue(catalystId, out var catTemplate) ? catTemplate.Name : "Enhancement Stone";
                SendResponse(player, 2, item.UpgradeLevel, $"Missing required catalyst: {catalystName}.");
                return;
            }

            if (requiresExtraGem)
            {
                int gemCount = player.Inventory.Where(i => i.TemplateId == RADIANT_GEM_ID).Sum(i => i.Quantity);
                int neededGems = (catalystId == RADIANT_GEM_ID) ? 2 : 1;
                if (gemCount < neededGems)
                {
                    SendResponse(player, 2, item.UpgradeLevel, "Missing required Radiant Gemstone for high-tier refinement.");
                    return;
                }
            }

            // Deduct Gold and Materials
            player.Gold -= requiredGold;
            DeductMaterial(player, catalystId, 1);
            if (requiresExtraGem) DeductMaterial(player, RADIANT_GEM_ID, 1);

            // Determine Success Rate & Outcome
            double roll = _rng.NextDouble();
            byte status = 1; // 1 = Success, 2 = Failed (Keep), 3 = Failed (Degrade), 0 = Broken
            string message = string.Empty;

            if (item.UpgradeLevel <= 2)
            {
                // Tier 1: Safe (+1 to +3) - 100% Success
                item.UpgradeLevel++;
                status = 1;
                message = $"Refinement successful! {template.Name} is now +{item.UpgradeLevel}!";
            }
            else if (item.UpgradeLevel <= 5)
            {
                // Tier 2: Moderate (+4 to +6) - 70% down to 50%
                double chance = 0.70 - ((item.UpgradeLevel - 3) * 0.10);
                if (roll < chance)
                {
                    item.UpgradeLevel++;
                    status = 1;
                    message = $"Refinement successful! {template.Name} is now +{item.UpgradeLevel}!";
                }
                else
                {
                    status = 2;
                    message = $"Refinement failed. The item level remains +{item.UpgradeLevel}.";
                }
            }
            else if (item.UpgradeLevel <= 8)
            {
                // Tier 3: High Risk (+7 to +9) - 40% down to 25% (Failure degrades -1)
                double chance = 0.40 - ((item.UpgradeLevel - 6) * 0.075);
                if (roll < chance)
                {
                    item.UpgradeLevel++;
                    status = 1;
                    message = $"Refinement successful! {template.Name} reached +{item.UpgradeLevel}!";
                }
                else
                {
                    item.UpgradeLevel = Math.Max(0, item.UpgradeLevel - 1);
                    status = 3;
                    message = $"Refinement failed! {template.Name} degraded to +{item.UpgradeLevel}!";
                }
            }
            else
            {
                // Tier 4: Extreme Risk (+10 and above) - 15% down to 5% (Failure DESTROYS item)
                double chance = Math.Max(0.05, 0.15 - ((item.UpgradeLevel - 9) * 0.03));
                if (roll < chance)
                {
                    item.UpgradeLevel++;
                    status = 1;
                    message = $"LEGENDARY REFINEMENT! {template.Name} surged to +{item.UpgradeLevel}!";
                }
                else
                {
                    status = 0;
                    message = $"CRITICAL FAILURE! {template.Name} was shattered and destroyed!";
                }
            }

            // Apply result
            if (status == 0) // Destroyed
            {
                if (isEquipped)
                {
                    player.EquippedItems.Remove(equippedSlot);
                    EquipmentHandler.SyncPlayerStats(player);
                    EquipmentHandler.SendEquippedItemsSync(player);
                }
                else
                {
                    player.Inventory.Remove(item);
                    InventoryHandler.SendInventorySlotUpdate(player, null, slotOrBag, slotIndex);
                    GameEvents.Instance.RaiseItemLost(player, item.TemplateId, item.Quantity);
                }
                InventoryHandler.DeleteDbItem(item);
            }
            else
            {
                InventoryHandler.SaveDbItem(item);
                if (isEquipped)
                {
                    EquipmentHandler.SyncPlayerStats(player);
                    EquipmentHandler.SendEquippedItemsSync(player);
                }
                else
                {
                    InventoryHandler.SendInventorySlotUpdate(player, item, slotOrBag, slotIndex);
                }
            }

            player.QueueSave(urgent: true); // refining destroys materials and gold
            SendResponse(player, status, item.UpgradeLevel, message);
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

            if (remaining < quantity) GameEvents.Instance.RaiseItemLost(player, templateId, quantity - remaining);
        }

        private static void SendResponse(Player player, byte status, int newLevel, string message)
        {
            using Packet response = new Packet(OpCode.UpgradeItemResponse);
            response.Write(status);
            response.Write(newLevel);
            response.Write(message);
            player.Connection.Send(response);
        }
    }
}
