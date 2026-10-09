using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Database.Models;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Network;
using Server.Persistence;

namespace Server.Handlers
{
    public static class LootHandler
    {
        public const float INTERACT_RANGE = Shared.Constants.GameRules.InteractRange;

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

            bool fullyLooted;
            lock (satchel.Lock)
            {
                var itemToLoot = satchel.Items.FirstOrDefault(i => i.Id == itemId);
                if (itemToLoot == null)
                {
                    SendLootSatchelSync(player, satchel);
                    return;
                }

                fullyLooted = TryMoveToInventory(player, satchel, itemToLoot);
            }

            // Satchels only live in memory, so whatever left one must be durable before it can be lost
            PersistenceService.Instance.Expedite(player.Id);

            if (!fullyLooted)
            {
                SendLootSatchelClose(player, satchelId, "Inventory is full.");
                return;
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
                    player.QueueSave(urgent: true);
                    InventoryHandler.SendInventorySync(player);
                }

                // 2. Loot Items
                var itemsToProcess = satchel.Items.ToList();
                foreach (var item in itemsToProcess)
                {
                    if (!TryMoveToInventory(player, satchel, item))
                    {
                        inventoryFull = true;
                        break;
                    }
                }
            }

            // Satchels only live in memory, so whatever left one must be durable before it can be lost
            PersistenceService.Instance.Expedite(player.Id);

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

        /// <summary>
        /// Moves a satchel item into the player's backpack: tops up existing partial stacks first, then puts what is
        /// left in the first free slot. Returns false when the backpack ran out of room; whatever did fit has been
        /// moved and the rest stays in the satchel with its quantity reduced. Caller holds the satchel lock.
        /// </summary>
        private static bool TryMoveToInventory(Player player, LootSatchel satchel, CharacterItem item)
        {
            int maxStack = DataManager.Items.TryGetValue(item.TemplateId, out var template) ? template.MaxStack : 1;
            int templateId = item.TemplateId;
            int startQuantity = item.Quantity;

            if (maxStack > 1)
            {
                var partialStacks = player.Inventory
                    .Where(i => i.TemplateId == item.TemplateId && i.Quantity < maxStack)
                    .ToList();

                foreach (var stack in partialStacks)
                {
                    int moved = Math.Min(maxStack - stack.Quantity, item.Quantity);
                    stack.Quantity += moved;
                    item.Quantity -= moved;
                    InventoryHandler.SaveDbItem(stack);
                    InventoryHandler.SendInventorySlotUpdate(player, stack, stack.BagIndex, stack.SlotIndex);

                    if (item.Quantity <= 0)
                    {
                        satchel.Items.Remove(item);
                        GameEvents.Instance.RaiseItemGained(player, templateId, startQuantity);
                        return true;
                    }
                }
            }

            int? freeSlot = InventoryHandler.FindFirstEmptySlot(player, 0);
            if (!freeSlot.HasValue)
            {
                int toppedUp = startQuantity - item.Quantity;
                if (toppedUp > 0) GameEvents.Instance.RaiseItemGained(player, templateId, toppedUp);
                return false;
            }

            satchel.Items.Remove(item);
            item.CharacterId = player.Id;
            item.BagIndex = 0;
            item.SlotIndex = freeSlot.Value;
            player.Inventory.Add(item);
            InventoryHandler.SaveDbItem(item);
            InventoryHandler.SendInventorySlotUpdate(player, item, 0, freeSlot.Value);
            GameEvents.Instance.RaiseItemGained(player, templateId, startQuantity);
            return true;
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
