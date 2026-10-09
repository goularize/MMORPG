using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Handlers;
using Server.World.Entities;

namespace Server.World
{
    /// <summary>
    /// Backpack operations for systems that give or take items without a dedicated packet (quests). Everything
    /// runs on the game thread, goes through the write-behind queue and tells the client about every slot it touches.
    /// </summary>
    public static class InventoryOps
    {
        private static int MaxStackOf(int templateId) =>
            DataManager.Items.TryGetValue(templateId, out var template) ? Math.Max(1, template.MaxStack) : 1;

        /// <summary>How many of the item are in the backpack (equipped items are not counted: they cannot be handed in).</summary>
        public static int CountInBackpack(Player player, int templateId)
        {
            int total = 0;
            foreach (var item in player.Inventory)
            {
                if (item.TemplateId == templateId) total += item.Quantity;
            }
            return total;
        }

        /// <summary>How many of the item the player owns, in the backpack or equipped.</summary>
        public static int CountOwned(Player player, int templateId)
        {
            int total = CountInBackpack(player, templateId);
            foreach (var item in player.EquippedItems.Values)
            {
                if (item.TemplateId == templateId) total += item.Quantity;
            }
            return total;
        }

        /// <summary>
        /// Whether the backpack can end up holding the additions after the removals, without touching it. Mirrors
        /// <see cref="RemoveFromBackpack"/> and <see cref="AddToBackpack"/> stack by stack, so a quest turn-in can be
        /// refused before anything is taken. Returns false when a removal is not fully available.
        /// </summary>
        public static bool CanFit(Player player, IReadOnlyDictionary<int, int> removals, IEnumerable<(int TemplateId, int Quantity)> additions)
        {
            var stacks = player.Inventory.Select(i => (i.TemplateId, i.Quantity)).ToList();

            foreach (var (templateId, quantity) in removals)
            {
                int remaining = quantity;
                for (int i = 0; i < stacks.Count && remaining > 0; i++)
                {
                    if (stacks[i].TemplateId != templateId) continue;

                    int taken = Math.Min(remaining, stacks[i].Quantity);
                    remaining -= taken;
                    stacks[i] = (templateId, stacks[i].Quantity - taken);
                }
                if (remaining > 0) return false;
                stacks.RemoveAll(s => s.Quantity <= 0);
            }

            foreach (var (templateId, quantity) in additions)
            {
                int remaining = quantity;
                int maxStack = MaxStackOf(templateId);

                if (maxStack > 1)
                {
                    for (int i = 0; i < stacks.Count && remaining > 0; i++)
                    {
                        if (stacks[i].TemplateId != templateId || stacks[i].Quantity >= maxStack) continue;

                        int moved = Math.Min(maxStack - stacks[i].Quantity, remaining);
                        remaining -= moved;
                        stacks[i] = (templateId, stacks[i].Quantity + moved);
                    }
                }

                while (remaining > 0)
                {
                    if (stacks.Count >= player.InventorySlots) return false;

                    int placed = Math.Min(maxStack, remaining);
                    stacks.Add((templateId, placed));
                    remaining -= placed;
                }
            }

            return true;
        }

        /// <summary>Takes the quantity out of the backpack, first stacks first. Returns how much was actually removed.</summary>
        public static int RemoveFromBackpack(Player player, int templateId, int quantity)
        {
            int remaining = quantity;

            foreach (var item in player.Inventory.Where(i => i.TemplateId == templateId).ToList())
            {
                if (remaining <= 0) break;

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
            }

            int removed = quantity - remaining;
            if (removed > 0) GameEvents.Instance.RaiseItemLost(player, templateId, removed);
            return removed;
        }

        /// <summary>
        /// Puts the quantity in the backpack: tops up partial stacks first, then uses free slots. Returns false (after
        /// adding what fit) when the backpack ran out of room, so callers check <see cref="CanFit"/> first.
        /// </summary>
        public static bool AddToBackpack(Player player, int templateId, int quantity)
        {
            int remaining = quantity;
            int maxStack = MaxStackOf(templateId);

            if (maxStack > 1)
            {
                foreach (var stack in player.Inventory.Where(i => i.TemplateId == templateId && i.Quantity < maxStack).ToList())
                {
                    if (remaining <= 0) break;

                    int moved = Math.Min(maxStack - stack.Quantity, remaining);
                    stack.Quantity += moved;
                    remaining -= moved;
                    InventoryHandler.SaveDbItem(stack);
                    InventoryHandler.SendInventorySlotUpdate(player, stack, stack.BagIndex, stack.SlotIndex);
                }
            }

            while (remaining > 0)
            {
                int? slot = InventoryHandler.FindFirstEmptySlot(player, 0);
                if (!slot.HasValue) break;

                int placed = Math.Min(maxStack, remaining);
                var item = ItemFactory.CreateItem(templateId, player.Id, placed);
                if (item == null) break;

                item.BagIndex = 0;
                item.SlotIndex = slot.Value;
                player.Inventory.Add(item);
                InventoryHandler.SaveDbItem(item);
                InventoryHandler.SendInventorySlotUpdate(player, item, 0, slot.Value);
                remaining -= placed;
            }

            int added = quantity - remaining;
            if (added > 0) GameEvents.Instance.RaiseItemGained(player, templateId, added);
            return remaining <= 0;
        }
    }
}
