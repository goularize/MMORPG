using System;
using Server.Data;
using Server.Data.Models;
using Server.Database.Models;
using Server.Handlers;
using Shared.Enums;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    /// <summary>
    /// Pins the byte layout of server-written packets. The Unity client reader must consume
    /// fields in exactly this order; reorder here only together with the client (and bump the version).
    /// </summary>
    public class PacketLayoutTests
    {
        [Fact]
        public void InventoryHandler_WriteItemData_LayoutIsStable()
        {
            var id = Guid.NewGuid();
            var item = new CharacterItem
            {
                Id = id, TemplateId = 101, BagIndex = 1, SlotIndex = 7, Quantity = 3, UpgradeLevel = 5, Rarity = ItemRarity.Epic,
                RolledPhysicalAttack = 11, RolledMagicAttack = 12, RolledPhysicalDefense = 13, RolledMagicDefense = 14,
                RolledStrength = 15, RolledIntelligence = 16, RolledConstitution = 17, RolledKnowledge = 18,
                RolledCritChance = 0.1f, RolledCritMultiplier = 0.2f, RolledDodgeChance = 0.3f,
                RolledMovementSpeed = 0.4f, RolledAttackSpeedBonus = 0.5f, RolledHealthRegen = 19, RolledManaRegen = 20
            };

            DataManager.Items[101] = new ItemTemplate
            {
                TemplateId = 101, Name = "Test Sword", Description = "Cuts things.", Type = ItemType.Equipment,
                Slot = EquipmentSlot.MainHand, RequiredLevel = 4, MaxStack = 1, BasePrice = 30, IsTwoHanded = true, Icon = "sword_test"
            };

            try
            {
                using var write = new Packet(OpCode.InventorySlotUpdate);
                InventoryHandler.WriteItemData(write, item);
                AssertLayout(write, id);
            }
            finally
            {
                DataManager.Items.Remove(101);
            }
        }

        private static void AssertLayout(Packet write, Guid id)
        {
            using var read = new Packet(write.ToArray());

            Assert.Equal(id.ToString(), read.ReadString());
            Assert.Equal(101, read.ReadInt());   // TemplateId
            Assert.Equal(1, read.ReadInt());     // BagIndex
            Assert.Equal(7, read.ReadInt());     // SlotIndex
            Assert.Equal(3, read.ReadInt());     // Quantity
            Assert.Equal(5, read.ReadInt());     // UpgradeLevel
            Assert.Equal((byte)ItemRarity.Epic, read.ReadByte());
            for (int expected = 11; expected <= 18; expected++)
                Assert.Equal(expected, read.ReadInt());   // 4 attack/defense stats, then Str/Int/Con/Know
            Assert.Equal(0.1f, read.ReadFloat());  // CritChance
            Assert.Equal(0.2f, read.ReadFloat());  // CritMultiplier
            Assert.Equal(0.3f, read.ReadFloat());  // DodgeChance
            Assert.Equal(0.4f, read.ReadFloat());  // MovementSpeed
            Assert.Equal(0.5f, read.ReadFloat());  // AttackSpeedBonus
            Assert.Equal(19, read.ReadInt());      // HealthRegen
            Assert.Equal(20, read.ReadInt());      // ManaRegen
            Assert.Equal("Test Sword", read.ReadString());   // template display data
            Assert.Equal("Cuts things.", read.ReadString());
            Assert.Equal((byte)ItemType.Equipment, read.ReadByte());
            Assert.Equal((byte)EquipmentSlot.MainHand, read.ReadByte());
            Assert.Equal(4, read.ReadInt());       // RequiredLevel
            Assert.Equal(1, read.ReadInt());       // MaxStack
            Assert.Equal(30, read.ReadInt());      // BasePrice
            Assert.True(read.ReadBool());          // IsTwoHanded
            Assert.Equal("sword_test", read.ReadString());
            Assert.Throws<System.IO.EndOfStreamException>(() => read.ReadByte());
        }

        [Fact]
        public void InventoryHandler_WriteItemData_UnknownTemplate_WritesPlaceholderDisplayData()
        {
            var item = new CharacterItem { TemplateId = 987654 };

            using var write = new Packet(OpCode.InventorySlotUpdate);
            InventoryHandler.WriteItemData(write, item);
            using var read = new Packet(write.ToArray());

            read.ReadString();                                   // Id
            for (int i = 0; i < 5; i++) read.ReadInt();          // TemplateId .. UpgradeLevel
            read.ReadByte();                                     // Rarity
            for (int i = 0; i < 8; i++) read.ReadInt();          // primary stats
            for (int i = 0; i < 5; i++) read.ReadFloat();        // secondary stats
            read.ReadInt(); read.ReadInt();                      // regen

            Assert.Equal("Unknown item", read.ReadString());
            Assert.Equal(string.Empty, read.ReadString());
            Assert.Equal((byte)ItemType.Unknown, read.ReadByte());
            Assert.Equal((byte)EquipmentSlot.None, read.ReadByte());
            Assert.Equal(1, read.ReadInt());
            Assert.Equal(1, read.ReadInt());
            Assert.Equal(0, read.ReadInt());
            Assert.False(read.ReadBool());
            Assert.Equal(string.Empty, read.ReadString());
            Assert.Throws<System.IO.EndOfStreamException>(() => read.ReadByte());
        }
    }
}
