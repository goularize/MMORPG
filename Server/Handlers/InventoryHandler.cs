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

namespace Server.Handlers
{
    public static class InventoryHandler
    {
        public static void WriteItemData(Packet packet, CharacterItem item)
        {
            packet.Write(item.Id.ToString());
            packet.Write(item.TemplateId);
            packet.Write(item.BagIndex);
            packet.Write(item.SlotIndex);
            packet.Write(item.Quantity);
            packet.Write(item.UpgradeLevel);
            packet.Write((byte)item.Rarity);

            // Primary Stats
            packet.Write(item.RolledPhysicalAttack);
            packet.Write(item.RolledMagicAttack);
            packet.Write(item.RolledPhysicalDefense);
            packet.Write(item.RolledMagicDefense);
            packet.Write(item.RolledStrength);
            packet.Write(item.RolledIntelligence);
            packet.Write(item.RolledConstitution);
            packet.Write(item.RolledKnowledge);

            // Secondary Stats
            packet.Write(item.RolledCritChance);
            packet.Write(item.RolledCritMultiplier);
            packet.Write(item.RolledDodgeChance);
            packet.Write(item.RolledMovementSpeed);
            packet.Write(item.RolledAttackSpeedBonus);
            packet.Write(item.RolledHealthRegen);
            packet.Write(item.RolledManaRegen);
        }

        public static void SendInventorySync(Player player)
        {
            using Packet packet = new Packet(OpCode.InventorySync);
            packet.Write(player.Gold);
            packet.Write(player.InventorySlots);
            packet.Write(player.Inventory.Count);

            foreach (var item in player.Inventory)
            {
                WriteItemData(packet, item);
            }

            player.Connection.Send(packet);
        }

        public static void SendInventorySlotUpdate(Player player, CharacterItem? item, int bagIndex, int slotIndex)
        {
            using Packet packet = new Packet(OpCode.InventorySlotUpdate);
            packet.Write(bagIndex);
            packet.Write(slotIndex);

            if (item != null)
            {
                packet.Write(true);
                WriteItemData(packet, item);
            }
            else
            {
                packet.Write(false);
            }

            player.Connection.Send(packet);
        }

        public static void HandleMoveItem(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null) return;

            int fromBag = packet.ReadInt();
            int fromSlot = packet.ReadInt();
            int toBag = packet.ReadInt();
            int toSlot = packet.ReadInt();

            if (fromBag != 0 || toBag != 0) return; // Only main backpack supported in Bag 0 for now
            if (fromSlot < 0 || fromSlot >= player.InventorySlots || toSlot < 0 || toSlot >= player.InventorySlots) return;
            if (fromSlot == toSlot) return;

            var sourceItem = player.Inventory.FirstOrDefault(i => i.BagIndex == fromBag && i.SlotIndex == fromSlot);
            if (sourceItem == null) return;

            var targetItem = player.Inventory.FirstOrDefault(i => i.BagIndex == toBag && i.SlotIndex == toSlot);

            if (targetItem == null)
            {
                // Simple move into empty slot
                sourceItem.BagIndex = toBag;
                sourceItem.SlotIndex = toSlot;

                SendInventorySlotUpdate(player, null, fromBag, fromSlot);
                SendInventorySlotUpdate(player, sourceItem, toBag, toSlot);
            }
            else
            {
                // If same stackable item template, try stacking
                if (sourceItem.TemplateId == targetItem.TemplateId && DataManager.Items.TryGetValue(sourceItem.TemplateId, out var template) && template.MaxStack > 1)
                {
                    int spaceLeft = template.MaxStack - targetItem.Quantity;
                    if (spaceLeft > 0)
                    {
                        int amountToMove = Math.Min(sourceItem.Quantity, spaceLeft);
                        targetItem.Quantity += amountToMove;
                        sourceItem.Quantity -= amountToMove;

                        if (sourceItem.Quantity <= 0)
                        {
                            player.Inventory.Remove(sourceItem);
                            DeleteDbItem(sourceItem.Id);
                            SendInventorySlotUpdate(player, null, fromBag, fromSlot);
                        }
                        else
                        {
                            SendInventorySlotUpdate(player, sourceItem, fromBag, fromSlot);
                        }

                        SendInventorySlotUpdate(player, targetItem, toBag, toSlot);
                        SaveDbItem(targetItem);
                        return;
                    }
                }

                // Swap slots
                sourceItem.BagIndex = toBag;
                sourceItem.SlotIndex = toSlot;
                targetItem.BagIndex = fromBag;
                targetItem.SlotIndex = fromSlot;

                SendInventorySlotUpdate(player, sourceItem, toBag, toSlot);
                SendInventorySlotUpdate(player, targetItem, fromBag, fromSlot);
                SaveDbItem(targetItem);
            }

            SaveDbItem(sourceItem);
        }

        public static void HandleSplitStack(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null) return;

            int bagIndex = packet.ReadInt();
            int slotIndex = packet.ReadInt();
            int splitAmount = packet.ReadInt();
            int targetBag = packet.ReadInt();
            int targetSlot = packet.ReadInt();

            if (bagIndex != 0 || targetBag != 0) return;
            if (slotIndex < 0 || slotIndex >= player.InventorySlots || targetSlot < 0 || targetSlot >= player.InventorySlots) return;
            if (splitAmount <= 0) return;

            var sourceItem = player.Inventory.FirstOrDefault(i => i.BagIndex == bagIndex && i.SlotIndex == slotIndex);
            if (sourceItem == null || sourceItem.Quantity <= splitAmount) return;

            var targetItem = player.Inventory.FirstOrDefault(i => i.BagIndex == targetBag && i.SlotIndex == targetSlot);
            if (targetItem != null) return; // Target must be empty

            sourceItem.Quantity -= splitAmount;

            var newItem = new CharacterItem
            {
                Id = Guid.NewGuid(),
                CharacterId = player.Id,
                TemplateId = sourceItem.TemplateId,
                BagIndex = targetBag,
                SlotIndex = targetSlot,
                Quantity = splitAmount,
                Rarity = sourceItem.Rarity,
                UpgradeLevel = sourceItem.UpgradeLevel
            };

            player.Inventory.Add(newItem);

            SaveDbItem(sourceItem);
            SaveDbItem(newItem);

            SendInventorySlotUpdate(player, sourceItem, bagIndex, slotIndex);
            SendInventorySlotUpdate(player, newItem, targetBag, targetSlot);
        }

        public static void HandleUseConsumable(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            int bagIndex = packet.ReadInt();
            int slotIndex = packet.ReadInt();

            var item = player.Inventory.FirstOrDefault(i => i.BagIndex == bagIndex && i.SlotIndex == slotIndex);
            if (item == null) return;

            if (!DataManager.Items.TryGetValue(item.TemplateId, out var template) || template.Type != ItemType.Consumable)
            {
                return;
            }

            bool effectApplied = false;

            if (template.RestoreHealth > 0 && player.Health < player.MaxHealth)
            {
                player.Health = Math.Min(player.MaxHealth, player.Health + template.RestoreHealth);
                effectApplied = true;
            }

            if (template.RestoreMana > 0 && player.Mana < player.MaxMana)
            {
                player.Mana = Math.Min(player.MaxMana, player.Mana + template.RestoreMana);
                effectApplied = true;
            }

            if (!effectApplied) return;

            // Consume item
            item.Quantity--;
            if (item.Quantity <= 0)
            {
                player.Inventory.Remove(item);
                DeleteDbItem(item.Id);
                SendInventorySlotUpdate(player, null, bagIndex, slotIndex);
            }
            else
            {
                SaveDbItem(item);
                SendInventorySlotUpdate(player, item, bagIndex, slotIndex);
            }

            // Sync vitals to client
            using Packet vitalsPacket = new Packet(OpCode.VitalsUpdate);
            vitalsPacket.Write(player.Id);
            vitalsPacket.Write(player.Health);
            vitalsPacket.Write(player.Mana);
            player.Connection.Send(vitalsPacket);
        }

        public static void HandleDropItem(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null) return;

            int bagIndex = packet.ReadInt();
            int slotIndex = packet.ReadInt();
            int quantity = packet.ReadInt();

            var item = player.Inventory.FirstOrDefault(i => i.BagIndex == bagIndex && i.SlotIndex == slotIndex);
            if (item == null) return;

            if (quantity >= item.Quantity)
            {
                player.Inventory.Remove(item);
                DeleteDbItem(item.Id);
                SendInventorySlotUpdate(player, null, bagIndex, slotIndex);
            }
            else
            {
                item.Quantity -= quantity;
                SaveDbItem(item);
                SendInventorySlotUpdate(player, item, bagIndex, slotIndex);
            }
        }

        public static int? FindFirstEmptySlot(Player player, int bagIndex = 0)
        {
            var occupiedSlots = player.Inventory.Where(i => i.BagIndex == bagIndex).Select(i => i.SlotIndex).ToHashSet();
            for (int slot = 0; slot < player.InventorySlots; slot++)
            {
                if (!occupiedSlots.Contains(slot)) return slot;
            }
            return null;
        }

        public static void SaveDbItem(CharacterItem item)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var db = AppDbContext.Factory();
                    var existing = db.CharacterItems.Find(item.Id);
                    if (existing != null)
                    {
                        db.Entry(existing).CurrentValues.SetValues(item);
                    }
                    else
                    {
                        db.CharacterItems.Add(item);
                    }
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Inventory] Error saving item {item.Id}: {ex.Message}");
                }
            });
        }

        public static void DeleteDbItem(Guid itemId)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using var db = AppDbContext.Factory();
                    var item = db.CharacterItems.Find(itemId);
                    if (item != null)
                    {
                        db.CharacterItems.Remove(item);
                        db.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Inventory] Error deleting item {itemId}: {ex.Message}");
                }
            });
        }
    }
}
