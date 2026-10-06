using System;
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

            using var write = new Packet(OpCode.InventorySlotUpdate);
            InventoryHandler.WriteItemData(write, item);
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
            Assert.Throws<System.IO.EndOfStreamException>(() => read.ReadByte());
        }
    }
}
