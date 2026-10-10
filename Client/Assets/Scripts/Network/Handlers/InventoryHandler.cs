using System;
using System.Collections.Generic;
using Shared.Enums;
using Shared.Network;

namespace Client.Network.Handlers
{
    /// <summary>One item as the server describes it: where it is, what the character rolled and how to show it.</summary>
    public sealed class ItemView
    {
        public string Id;
        public int TemplateId;
        public int BagIndex;
        public int SlotIndex;
        public int Quantity;
        public int UpgradeLevel;
        public ItemRarity Rarity;

        // Rolled stats (zero = the item does not have it)
        public int PhysicalAttack, MagicAttack, PhysicalDefense, MagicDefense;
        public int Strength, Intelligence, Constitution, Knowledge;
        public float CritChance, CritMultiplier, DodgeChance, MovementSpeed, AttackSpeedBonus;
        public int HealthRegen, ManaRegen;

        // Display data of the template
        public string Name;
        public string Description;
        public ItemType Type;
        public EquipmentSlot Slot;
        public int RequiredLevel;
        public int MaxStack;
        public int BasePrice;
        public bool IsTwoHanded;
        public string Icon;
    }

    /// <summary>
    /// The backpack and paperdoll mirror. The server sends the whole inventory when the character enters the world
    /// (InventorySync, with the gold) and then one InventorySlotUpdate per changed backpack slot; the equipment is
    /// resent whole (EquippedItemsSync). Every action here only asks the server: nothing changes until its answer arrives.
    /// </summary>
    public static class InventoryHandler
    {
        /// <summary>The main backpack is bag 0; the other bags are not supported by the server yet.</summary>
        public const int MainBag = 0;

        private static readonly Dictionary<int, ItemView> _backpack = new();
        private static readonly Dictionary<EquipmentSlot, ItemView> _equipment = new();

        public static long Gold { get; private set; }

        /// <summary>Number of backpack slots (0 until the first sync).</summary>
        public static int SlotCount { get; private set; }

        /// <summary>Anything in the backpack, the equipment or the gold changed.</summary>
        public static event Action OnInventoryChanged;

        public static bool TryGetBackpackItem(int slot, out ItemView item) => _backpack.TryGetValue(slot, out item);

        public static bool TryGetEquipped(EquipmentSlot slot, out ItemView item) => _equipment.TryGetValue(slot, out item);

        public static int FreeSlots
        {
            get
            {
                int used = 0;
                for (int i = 0; i < SlotCount; i++) if (_backpack.ContainsKey(i)) used++;
                return SlotCount - used;
            }
        }

        /// <summary>Back to an empty inventory (called when returning to character select).</summary>
        public static void ResetSession()
        {
            _backpack.Clear();
            _equipment.Clear();
            Gold = 0;
            SlotCount = 0;
        }

        // ---- Requests ----

        public static void RequestMove(int fromSlot, int toSlot)
        {
            using (Packet packet = new Packet(OpCode.MoveInventoryItemRequest))
            {
                packet.Write(MainBag);
                packet.Write(fromSlot);
                packet.Write(MainBag);
                packet.Write(toSlot);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        /// <summary>Moves part of a stack into an empty slot.</summary>
        public static void RequestSplit(int slot, int amount, int toSlot)
        {
            using (Packet packet = new Packet(OpCode.SplitItemStackRequest))
            {
                packet.Write(MainBag);
                packet.Write(slot);
                packet.Write(amount);
                packet.Write(MainBag);
                packet.Write(toSlot);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public static void RequestUse(int slot)
        {
            using (Packet packet = new Packet(OpCode.UseConsumableItemRequest))
            {
                packet.Write(MainBag);
                packet.Write(slot);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public static void RequestEquip(int slot, EquipmentSlot target)
        {
            using (Packet packet = new Packet(OpCode.EquipItemRequest))
            {
                packet.Write(MainBag);
                packet.Write(slot);
                packet.Write((byte)target);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        /// <summary>The server puts the item into the first free backpack slot.</summary>
        public static void RequestUnequip(EquipmentSlot slot)
        {
            using (Packet packet = new Packet(OpCode.UnequipItemRequest))
            {
                packet.Write((byte)slot);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        /// <summary>Destroys the items (the server does not put them in the world).</summary>
        public static void RequestDrop(int slot, int quantity)
        {
            using (Packet packet = new Packet(OpCode.DropItemRequest))
            {
                packet.Write(MainBag);
                packet.Write(slot);
                packet.Write(quantity);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        // ---- Server packets ----

        public static void HandleInventorySync(Packet packet)
        {
            Gold = packet.ReadLong();
            SlotCount = packet.ReadInt();

            _backpack.Clear();
            int count = packet.ReadInt();
            for (int i = 0; i < count; i++)
            {
                var item = ReadItem(packet);
                if (item.BagIndex == MainBag) _backpack[item.SlotIndex] = item;
            }

            OnInventoryChanged?.Invoke();
        }

        public static void HandleInventorySlotUpdate(Packet packet)
        {
            int bag = packet.ReadInt();
            int slot = packet.ReadInt();
            bool hasItem = packet.ReadBool();
            var item = hasItem ? ReadItem(packet) : null;

            if (bag != MainBag) return;

            if (item == null) _backpack.Remove(slot);
            else _backpack[slot] = item;

            OnInventoryChanged?.Invoke();
        }

        public static void HandleEquippedItemsSync(Packet packet)
        {
            _equipment.Clear();
            int count = packet.ReadInt();
            for (int i = 0; i < count; i++)
            {
                var slot = (EquipmentSlot)packet.ReadByte();
                _equipment[slot] = ReadItem(packet);
            }

            OnInventoryChanged?.Invoke();
        }

        // The layout is pinned by PacketLayoutTests.InventoryHandler_WriteItemData_LayoutIsStable on the server
        private static ItemView ReadItem(Packet packet)
        {
            var item = new ItemView
            {
                Id = packet.ReadString(),
                TemplateId = packet.ReadInt(),
                BagIndex = packet.ReadInt(),
                SlotIndex = packet.ReadInt(),
                Quantity = packet.ReadInt(),
                UpgradeLevel = packet.ReadInt(),
                Rarity = (ItemRarity)packet.ReadByte(),
                PhysicalAttack = packet.ReadInt(),
                MagicAttack = packet.ReadInt(),
                PhysicalDefense = packet.ReadInt(),
                MagicDefense = packet.ReadInt(),
                Strength = packet.ReadInt(),
                Intelligence = packet.ReadInt(),
                Constitution = packet.ReadInt(),
                Knowledge = packet.ReadInt(),
                CritChance = packet.ReadFloat(),
                CritMultiplier = packet.ReadFloat(),
                DodgeChance = packet.ReadFloat(),
                MovementSpeed = packet.ReadFloat(),
                AttackSpeedBonus = packet.ReadFloat(),
                HealthRegen = packet.ReadInt(),
                ManaRegen = packet.ReadInt(),
                Name = packet.ReadString(),
                Description = packet.ReadString(),
                Type = (ItemType)packet.ReadByte(),
                Slot = (EquipmentSlot)packet.ReadByte(),
                RequiredLevel = packet.ReadInt(),
                MaxStack = packet.ReadInt(),
                BasePrice = packet.ReadInt(),
                IsTwoHanded = packet.ReadBool(),
                Icon = packet.ReadString()
            };
            return item;
        }
    }
}
