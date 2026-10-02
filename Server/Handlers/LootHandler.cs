using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Database.Models;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Network;

namespace Server.Handlers
{
    public static class LootHandler
    {
        public const float INTERACT_RANGE = 3.0f;

        public static void HandleOpenLootSatchel(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            int satchelId = packet.ReadInt();
            var map = GameLogic.MapMgr.GetMap(player.MapId);
            if (map == null) return;

            if (!map.LootSatchels.TryGetValue(satchelId, out var satchel))
            {
                SendLootSatchelClose(player, satchelId, "Satchel does not exist or has despawned.");
                return;
            }

            if (Shared.Math.Vector3.Distance(player.Position, satchel.Position) > INTERACT_RANGE)
            {
                SendLootSatchelClose(player, satchelId, "You are too far away to open this satchel.");
                return;
            }

            if (!satchel.CanLoot(player))
            {
                int remainingSec = (int)Math.Ceiling(satchel.RemainingOwnershipSeconds);
                SendLootSatchelClose(player, satchelId, $"You do not have looting rights yet. Exclusive rights expire in {remainingSec}s.");
                return;
            }

            SendLootSatchelSync(player, satchel);
        }

        public static void HandleLootItem(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            int satchelId = packet.ReadInt();
            string itemIdStr = packet.ReadString();
            if (!Guid.TryParse(itemIdStr, out Guid itemId)) return;

            var map = GameLogic.MapMgr.GetMap(player.MapId);
            if (map == null) return;

            if (!map.LootSatchels.TryGetValue(satchelId, out var satchel))
            {
                SendLootSatchelClose(player, satchelId, "Satchel does not exist or has despawned.");
                return;
            }

            if (Shared.Math.Vector3.Distance(player.Position, satchel.Position) > INTERACT_RANGE)
            {
                SendLootSatchelClose(player, satchelId, "You are too far away to loot this satchel.");
                return;
            }

            if (!satchel.CanLoot(player))
            {
                int remainingSec = (int)Math.Ceiling(satchel.RemainingOwnershipSeconds);
                SendLootSatchelClose(player, satchelId, $"You do not have looting rights yet. Exclusive rights expire in {remainingSec}s.");
                return;
            }

            lock (satchel.Lock)
            {
                var itemToLoot = satchel.Items.FirstOrDefault(i => i.Id == itemId);
                if (itemToLoot == null)
                {
                    SendLootSatchelSync(player, satchel);
                    return;
                }

                var template = DataManager.Items.TryGetValue(itemToLoot.TemplateId, out var tmpl) ? tmpl : null;
                int maxStack = template?.MaxStack ?? 1;
                bool itemAdded = false;

                // 1. Try stacking into existing inventory stack if stackable
                if (maxStack > 1)
                {
                    var existingStack = player.Inventory.FirstOrDefault(i => i.TemplateId == itemToLoot.TemplateId && i.Quantity < maxStack);
                    if (existingStack != null)
                    {
                        int space = maxStack - existingStack.Quantity;
                        if (itemToLoot.Quantity <= space)
                        {
                            existingStack.Quantity += itemToLoot.Quantity;
                            satchel.Items.Remove(itemToLoot);
                            InventoryHandler.SaveDbItem(existingStack);
                            InventoryHandler.SendInventorySlotUpdate(player, existingStack, existingStack.BagIndex, existingStack.SlotIndex);
                            itemAdded = true;
                        }
                        else
                        {
                            // Partial stack merge
                            int? freeSlot = InventoryHandler.FindFirstEmptySlot(player, 0);
                            if (!freeSlot.HasValue)
                            {
                                SendLootSatchelClose(player, satchelId, "Inventory is full.");
                                return;
                            }

                            existingStack.Quantity += space;
                            InventoryHandler.SaveDbItem(existingStack);
                            InventoryHandler.SendInventorySlotUpdate(player, existingStack, existingStack.BagIndex, existingStack.SlotIndex);

                            itemToLoot.Quantity -= space;
                            satchel.Items.Remove(itemToLoot);

                            itemToLoot.CharacterId = player.Id;
                            itemToLoot.BagIndex = 0;
                            itemToLoot.SlotIndex = freeSlot.Value;
                            player.Inventory.Add(itemToLoot);
                            InventoryHandler.SaveDbItem(itemToLoot);
                            InventoryHandler.SendInventorySlotUpdate(player, itemToLoot, 0, freeSlot.Value);
                            itemAdded = true;
                        }
                    }
                }

                // 2. Put into empty slot
                if (!itemAdded)
                {
                    int? freeSlot = InventoryHandler.FindFirstEmptySlot(player, 0);
                    if (!freeSlot.HasValue)
                    {
                        SendLootSatchelClose(player, satchelId, "Inventory is full.");
                        return;
                    }

                    satchel.Items.Remove(itemToLoot);
                    itemToLoot.CharacterId = player.Id;
                    itemToLoot.BagIndex = 0;
                    itemToLoot.SlotIndex = freeSlot.Value;
                    player.Inventory.Add(itemToLoot);
                    InventoryHandler.SaveDbItem(itemToLoot);
                    InventoryHandler.SendInventorySlotUpdate(player, itemToLoot, 0, freeSlot.Value);
                }
            }

            if (satchel.IsEmpty)
            {
                map.DespawnLootSatchel(satchel.Id);
                SendLootSatchelClose(player, satchelId, "Satchel emptied.");
            }
            else
            {
                SendLootSatchelSync(player, satchel);
            }
        }

        public static void HandleLootAll(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            int satchelId = packet.ReadInt();
            var map = GameLogic.MapMgr.GetMap(player.MapId);
            if (map == null) return;

            if (!map.LootSatchels.TryGetValue(satchelId, out var satchel))
            {
                SendLootSatchelClose(player, satchelId, "Satchel does not exist or has despawned.");
                return;
            }

            if (Shared.Math.Vector3.Distance(player.Position, satchel.Position) > INTERACT_RANGE)
            {
                SendLootSatchelClose(player, satchelId, "You are too far away to loot this satchel.");
                return;
            }

            if (!satchel.CanLoot(player))
            {
                int remainingSec = (int)Math.Ceiling(satchel.RemainingOwnershipSeconds);
                SendLootSatchelClose(player, satchelId, $"You do not have looting rights yet. Exclusive rights expire in {remainingSec}s.");
                return;
            }

            bool inventoryFull = false;

            lock (satchel.Lock)
            {
                // 1. Loot Gold
                if (satchel.Gold > 0)
                {
                    player.Gold += satchel.Gold;
                    satchel.Gold = 0;
                    player.SaveProgressionToDatabase();
                    InventoryHandler.SendInventorySync(player);
                }

                // 2. Loot Items
                var itemsToProcess = satchel.Items.ToList();
                foreach (var item in itemsToProcess)
                {
                    var template = DataManager.Items.TryGetValue(item.TemplateId, out var tmpl) ? tmpl : null;
                    int maxStack = template?.MaxStack ?? 1;
                    bool itemAdded = false;

                    if (maxStack > 1)
                    {
                        var existing = player.Inventory.FirstOrDefault(i => i.TemplateId == item.TemplateId && i.Quantity + item.Quantity <= maxStack);
                        if (existing != null)
                        {
                            existing.Quantity += item.Quantity;
                            satchel.Items.Remove(item);
                            InventoryHandler.SaveDbItem(existing);
                            InventoryHandler.SendInventorySlotUpdate(player, existing, existing.BagIndex, existing.SlotIndex);
                            itemAdded = true;
                        }
                    }

                    if (!itemAdded)
                    {
                        int? freeSlot = InventoryHandler.FindFirstEmptySlot(player, 0);
                        if (!freeSlot.HasValue)
                        {
                            inventoryFull = true;
                            break;
                        }

                        satchel.Items.Remove(item);
                        item.CharacterId = player.Id;
                        item.BagIndex = 0;
                        item.SlotIndex = freeSlot.Value;
                        player.Inventory.Add(item);
                        InventoryHandler.SaveDbItem(item);
                        InventoryHandler.SendInventorySlotUpdate(player, item, 0, freeSlot.Value);
                    }
                }
            }

            if (satchel.IsEmpty)
            {
                map.DespawnLootSatchel(satchel.Id);
                SendLootSatchelClose(player, satchelId, "All loot gathered.");
            }
            else
            {
                SendLootSatchelSync(player, satchel);
                if (inventoryFull)
                {
                    SendLootSatchelClose(player, satchelId, "Inventory full. Some items could not be looted.");
                }
            }
        }

        public static void SendLootSatchelSync(Player player, LootSatchel satchel)
        {
            using Packet syncPacket = new Packet(OpCode.LootSatchelSync);
            syncPacket.Write(satchel.Id);
            syncPacket.Write(satchel.Gold);

            lock (satchel.Lock)
            {
                syncPacket.Write(satchel.Items.Count);
                foreach (var item in satchel.Items)
                {
                    InventoryHandler.WriteItemData(syncPacket, item);
                }
            }

            player.Connection.Send(syncPacket);
        }

        public static void SendLootSatchelClose(Player player, int satchelId, string reason)
        {
            using Packet closePacket = new Packet(OpCode.LootSatchelClose);
            closePacket.Write(satchelId);
            closePacket.Write(reason);
            player.Connection.Send(closePacket);
        }
    }
}
