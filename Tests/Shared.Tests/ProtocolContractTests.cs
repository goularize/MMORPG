using System;
using System.IO;
using System.Linq;
using Shared.Constants;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Shared.Tests
{
    /// <summary>
    /// Guards the wire contract shared by the Unity client and the server.
    /// If a test here fails, a protocol change must be deliberate (bump GameRules.GameVersion).
    /// </summary>
    public class ProtocolContractTests
    {
        // Frozen registry: renumbering or removing an opcode breaks every deployed client.
        private static readonly (string Name, ushort Value)[] ExpectedOpCodes =
        {
            ("Unknown", 0), ("SignInRequest", 1), ("SignInResponse", 2), ("SignUpRequest", 3), ("SignUpResponse", 4),
            ("EntitySpawn", 5), ("EntityDespawn", 6), ("EntityPositionUpdate", 7),
            ("CharacterListRequest", 8), ("CharacterListResponse", 9), ("CharacterCreateRequest", 10), ("CharacterCreateResponse", 11),
            ("CharacterDeleteRequest", 12), ("CharacterDeleteResponse", 13), ("CharacterSelectRequest", 14), ("CharacterSelectResponse", 15),
            ("PlayerMoveRequest", 16), ("ChatMessageRequest", 17), ("ChatMessageBroadcast", 18),
            ("StatsUpdate", 19), ("VitalsUpdate", 20), ("EntityInteractRequest", 21), ("EntityAttackRequest", 22),
            ("SetBindPointRequest", 23), ("SetBindPointResponse", 24), ("PlayerRespawnRequest", 25), ("PlayerRespawnResponse", 26),
            ("EntityCombatEvent", 27), ("PlayerExpUpdate", 28), ("PlayerLevelUp", 29),
            ("AllocateStatPointRequest", 30), ("AllocateStatPointResponse", 31), ("PlayerProgressionSync", 32),
            ("MoveInventoryItemRequest", 70), ("SplitItemStackRequest", 71), ("UseConsumableItemRequest", 72), ("EquipItemRequest", 73),
            ("UnequipItemRequest", 74), ("UpgradeItemRequest", 75), ("LearnRecipeRequest", 76), ("CraftItemRequest", 77), ("DropItemRequest", 78),
            ("InventorySync", 80), ("InventorySlotUpdate", 81), ("EquippedItemsSync", 82), ("UpgradeItemResponse", 83), ("CraftItemResponse", 84),
            ("OpenLootSatchelRequest", 85), ("LootItemRequest", 86), ("LootAllRequest", 87), ("LootSatchelSync", 88), ("LootSatchelClose", 89),
        };

        [Fact]
        public void OpCodes_AreUnique()
        {
            var values = Enum.GetValues<OpCode>().Select(o => (ushort)o).ToList();
            Assert.Equal(values.Count, values.Distinct().Count());
        }

        [Fact]
        public void OpCodes_MatchFrozenRegistry()
        {
            var actual = Enum.GetValues<OpCode>().Select(o => (o.ToString(), (ushort)o)).OrderBy(t => t.Item2).ToArray();
            var expected = ExpectedOpCodes.Select(t => (t.Name, t.Value)).OrderBy(t => t.Value).ToArray();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Packet_Header_IsLittleEndianLengthThenOpCode()
        {
            using var packet = new Packet(OpCode.PlayerMoveRequest); // 16 = 0x0010
            packet.Write(1); // 4 payload bytes
            byte[] bytes = packet.ToArray();

            Assert.Equal(new byte[] { 0x08, 0x00, 0x10, 0x00, 0x01, 0x00, 0x00, 0x00 }, bytes);
        }

        [Fact]
        public void Packet_PrimitiveLayouts_AreStable()
        {
            using var packet = new Packet(OpCode.Unknown);
            packet.Write((byte)7);
            packet.Write(true);
            packet.Write(-2);
            packet.Write(1L);
            packet.Write(1.0f);
            packet.Write("Hi");
            packet.Write(new Vector3(1f, 2f, 3f));
            byte[] payload = packet.ToArray().Skip(4).ToArray();

            byte[] expected =
                new byte[] { 0x07 }                                   // byte
                .Concat(new byte[] { 0x01 })                          // bool
                .Concat(new byte[] { 0xFE, 0xFF, 0xFF, 0xFF })        // int -2 (LE)
                .Concat(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 })        // long 1 (LE)
                .Concat(new byte[] { 0x00, 0x00, 0x80, 0x3F })        // float 1.0
                .Concat(new byte[] { 0x02, (byte)'H', (byte)'i' })    // string: 1-byte length prefix + UTF-8
                .Concat(BitConverter.GetBytes(1f))                    // Vector3 = 3 floats (X, Y, Z)
                .Concat(BitConverter.GetBytes(2f))
                .Concat(BitConverter.GetBytes(3f))
                .ToArray();

            Assert.Equal(expected, payload);
        }

        [Fact]
        public void Packet_Strings_Utf8_RoundTrip()
        {
            using var write = new Packet(OpCode.ChatMessageBroadcast);
            write.Write("Olá, 世界");
            using var read = new Packet(write.ToArray());
            Assert.Equal("Olá, 世界", read.ReadString());
        }

        [Fact]
        public void Packet_ReadingPastPayload_Throws()
        {
            using var write = new Packet(OpCode.VitalsUpdate);
            write.Write(5);
            using var read = new Packet(write.ToArray());

            Assert.Equal(5, read.ReadInt());
            Assert.Throws<EndOfStreamException>(() => read.ReadInt());
        }

        [Fact]
        public void Packet_TruncatedPayload_Throws()
        {
            using var write = new Packet(OpCode.EntitySpawn);
            write.Write("a long enough name");
            byte[] bytes = write.ToArray();
            byte[] truncated = bytes.Take(bytes.Length - 5).ToArray();

            using var read = new Packet(truncated);
            Assert.Throws<EndOfStreamException>(() => read.ReadString());
        }

        [Fact]
        public void Packet_TruncatedHeader_Throws()
        {
            Assert.ThrowsAny<EndOfStreamException>(() => new Packet(new byte[] { 0x04, 0x00 }));
        }

        [Fact]
        public void Packet_MaxSizeBoundary_IsEnforced()
        {
            // 4 header bytes + payload. Exactly 65535 total must frame; one more byte must not silently wrap.
            using var fits = new Packet(OpCode.Unknown);
            for (int i = 0; i < ushort.MaxValue - 4; i++) fits.Write((byte)0);
            Assert.Equal(ushort.MaxValue, fits.ToArray().Length);

            using var tooBig = new Packet(OpCode.Unknown);
            for (int i = 0; i < ushort.MaxValue - 3; i++) tooBig.Write((byte)0);
            Assert.Throws<InvalidOperationException>(() => tooBig.ToArray());
        }

        [Fact]
        public void GameRules_Sanity()
        {
            Assert.False(string.IsNullOrWhiteSpace(GameRules.GameVersion));
            Assert.True(GameRules.InteractRange <= GameRules.AoiRadius);
            Assert.True(GameRules.MinPlayerMoveSpeed <= GameRules.BasePlayerMoveSpeed);
            Assert.True(GameRules.MinPlayerAttackInterval <= GameRules.BasePlayerAttackInterval);
        }
    }
}
