using System;
using System.Linq;
using Server.Data;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Network;

namespace Server.Handlers
{
    public static class EquipmentHandler
    {
        public static void SendEquippedItemsSync(Player player)
        {
            using Packet packet = new Packet(OpCode.EquippedItemsSync);
            packet.Write(player.EquippedItems.Count);

            foreach (var kvp in player.EquippedItems)
            {
                packet.Write((byte)kvp.Key);
                InventoryHandler.WriteItemData(packet, kvp.Value);
            }

            player.Connection.Send(packet);
        }

        public static void HandleEquipItem(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            int bagIndex = packet.ReadInt();
            int slotIndex = packet.ReadInt();
            byte targetSlotByte = packet.ReadByte();

            if (!Enum.IsDefined(typeof(EquipmentSlot), targetSlotByte) || targetSlotByte == 0) return;
            EquipmentSlot targetSlot = (EquipmentSlot)targetSlotByte;

            var itemToEquip = player.Inventory.FirstOrDefault(i => i.BagIndex == bagIndex && i.SlotIndex == slotIndex);
            if (itemToEquip == null) return;

            if (!DataManager.Items.TryGetValue(itemToEquip.TemplateId, out var template) || template.Type != ItemType.Equipment)
            {
                return;
            }

            // Check level requirement
            if (player.Level < template.RequiredLevel)
            {
                Console.WriteLine($"[Equipment] Player {player.Name} is level {player.Level}, requires {template.RequiredLevel} to equip {template.Name}.");
                return;
            }

            // Validate slot compatibility
            if (template.Slot != targetSlot)
            {
                Console.WriteLine($"[Equipment] Item {template.Name} belongs in slot {template.Slot}, cannot equip to {targetSlot}.");
                return;
            }

            // Two-Handed weapon handling
            if (targetSlot == EquipmentSlot.MainHand && template.IsTwoHanded)
            {
                // Must unequip OffHand if present
                if (player.EquippedItems.TryGetValue(EquipmentSlot.OffHand, out var offhandItem))
                {
                    int? freeSlot = InventoryHandler.FindFirstEmptySlot(player);
                    if (!freeSlot.HasValue)
                    {
                        Console.WriteLine($"[Equipment] Cannot equip 2H weapon: Inventory is full to unequip OffHand.");
                        return;
                    }

                    player.EquippedItems.Remove(EquipmentSlot.OffHand);
                    offhandItem.IsEquipped = false;
                    offhandItem.EquippedSlot = null;
                    offhandItem.BagIndex = 0;
                    offhandItem.SlotIndex = freeSlot.Value;
                    player.Inventory.Add(offhandItem);
                    InventoryHandler.SaveDbItem(offhandItem);
                    InventoryHandler.SendInventorySlotUpdate(player, offhandItem, 0, freeSlot.Value);
                }
            }
            else if (targetSlot == EquipmentSlot.OffHand)
            {
                // If MainHand has a 2-Handed weapon, must unequip MainHand
                if (player.EquippedItems.TryGetValue(EquipmentSlot.MainHand, out var mainhandItem) &&
                    DataManager.Items.TryGetValue(mainhandItem.TemplateId, out var mainTemplate) && mainTemplate.IsTwoHanded)
                {
                    int? freeSlot = InventoryHandler.FindFirstEmptySlot(player);
                    if (!freeSlot.HasValue)
                    {
                        Console.WriteLine($"[Equipment] Cannot equip OffHand: Inventory is full to unequip 2H MainHand.");
                        return;
                    }

                    player.EquippedItems.Remove(EquipmentSlot.MainHand);
                    mainhandItem.IsEquipped = false;
                    mainhandItem.EquippedSlot = null;
                    mainhandItem.BagIndex = 0;
                    mainhandItem.SlotIndex = freeSlot.Value;
                    player.Inventory.Add(mainhandItem);
                    InventoryHandler.SaveDbItem(mainhandItem);
                    InventoryHandler.SendInventorySlotUpdate(player, mainhandItem, 0, freeSlot.Value);
                }
            }

            // Check if slot already has an equipped item (swap)
            if (player.EquippedItems.TryGetValue(targetSlot, out var existingEquippedItem))
            {
                // Put existing equipped item into the bag slot
                existingEquippedItem.IsEquipped = false;
                existingEquippedItem.EquippedSlot = null;
                existingEquippedItem.BagIndex = bagIndex;
                existingEquippedItem.SlotIndex = slotIndex;
                InventoryHandler.SaveDbItem(existingEquippedItem);

                // Put itemToEquip into equipment slot
                player.Inventory.Remove(itemToEquip);
                itemToEquip.IsEquipped = true;
                itemToEquip.EquippedSlot = targetSlot;
                itemToEquip.BagIndex = -1;
                itemToEquip.SlotIndex = -1;
                player.EquippedItems[targetSlot] = itemToEquip;
                InventoryHandler.SaveDbItem(itemToEquip);

                // Add existing back to player.Inventory
                player.Inventory.Add(existingEquippedItem);

                InventoryHandler.SendInventorySlotUpdate(player, existingEquippedItem, bagIndex, slotIndex);
            }
            else
            {
                // Simple equip: remove from bag slot, place in equipment
                player.Inventory.Remove(itemToEquip);
                itemToEquip.IsEquipped = true;
                itemToEquip.EquippedSlot = targetSlot;
                itemToEquip.BagIndex = -1;
                itemToEquip.SlotIndex = -1;
                player.EquippedItems[targetSlot] = itemToEquip;
                InventoryHandler.SaveDbItem(itemToEquip);

                InventoryHandler.SendInventorySlotUpdate(player, null, bagIndex, slotIndex);
            }

            // Recalculate derived player combat stats & sync
            SyncPlayerStats(player);
            SendEquippedItemsSync(player);
        }

        public static void HandleUnequipItem(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            byte slotByte = packet.ReadByte();
            if (!Enum.IsDefined(typeof(EquipmentSlot), slotByte) || slotByte == 0) return;
            EquipmentSlot slot = (EquipmentSlot)slotByte;

            if (!player.EquippedItems.TryGetValue(slot, out var equippedItem)) return;

            int? freeSlot = InventoryHandler.FindFirstEmptySlot(player);
            if (!freeSlot.HasValue)
            {
                Console.WriteLine($"[Equipment] Cannot unequip {slot}: Inventory is full.");
                return;
            }

            player.EquippedItems.Remove(slot);
            equippedItem.IsEquipped = false;
            equippedItem.EquippedSlot = null;
            equippedItem.BagIndex = 0;
            equippedItem.SlotIndex = freeSlot.Value;
            player.Inventory.Add(equippedItem);

            InventoryHandler.SaveDbItem(equippedItem);
            InventoryHandler.SendInventorySlotUpdate(player, equippedItem, 0, freeSlot.Value);

            SyncPlayerStats(player);
            SendEquippedItemsSync(player);
        }

        public static void SyncPlayerStats(Player player)
        {
            player.CalculateDerivedStats();

            using Packet statsPacket = new Packet(OpCode.StatsUpdate);
            statsPacket.Write(player.MaxHealth);
            statsPacket.Write(player.MaxMana);
            statsPacket.Write(player.Attack);
            statsPacket.Write(player.MagicAttack);
            statsPacket.Write(player.Defense);
            statsPacket.Write(player.MagicDefense);
            player.Connection.Send(statsPacket);

            using Packet vitalsPacket = new Packet(OpCode.VitalsUpdate);
            vitalsPacket.Write(player.Id);
            vitalsPacket.Write(player.Health);
            vitalsPacket.Write(player.Mana);
            player.Connection.Send(vitalsPacket);
        }
    }
}
