using System;
using System.Linq;
using Server.Handlers;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Network;
using Shared.Math;
using Xunit;
using System.Collections.Generic;

namespace Server.Tests
{
    public class CombatHandlerTests
    {
        [Fact]
        public void HandleAttackRequest_ShouldDealDamageUntilDeath()
        {
            // Arrange
            var attackerClient = new MockClientConnection { AccountId = 1, PlayerId = 100 };
            var attacker = new Player(100, "Attacker", attackerClient)
            {
                Position = new Vector3(0, 0, 0),
                Health = 100,
                Attack = 20,
                CritChance = 0.0f,    // Disable crits for predictable damage
                DodgeChance = 0.0f,   // Disable dodge
                BaseAttackSpeed = 1.0f
            };

            var targetClient = new MockClientConnection { AccountId = 2, PlayerId = 200 };
            var target = new Player(200, "Target", targetClient)
            {
                Position = new Vector3(1, 0, 0), // Within melee range (2.5f)
                Defense = 10,
                MaxHealth = 30,
                Health = 30,
                CritChance = 0.0f,
                DodgeChance = 0.0f
            };

            // Set AoI so they know each other
            attacker.KnownEntities.Add(target.Id);
            target.KnownEntities.Add(attacker.Id);

            GameLogic.MapMgr.ActiveMaps[1].Players.Clear();
            GameLogic.MapMgr.AddPlayer(attacker);
            GameLogic.MapMgr.AddPlayer(target);

            // Act
            int attackCount = 0;
            
            // It should take 2 or 3 hits to kill a 30 HP target with ~15 damage per hit.
            while (target.Health > 0 && attackCount < 10)
            {
                // Reset cooldown so we can hit immediately
                attacker.LastAttackTime = DateTime.UtcNow.AddSeconds(-2);

                using var writePacket = new Packet(OpCode.EntityAttackRequest);
                writePacket.Write(target.Id);

                using var packet = new Packet(writePacket.ToArray());
                
                // Track packets sent to target to see Combat Events
                int initialPacketCount = targetClient.SentPackets.Count;

                CombatHandler.HandleAttackRequest(attackerClient, packet);
                
                attackCount++;

                // Verify the broadcast happened
                var newPackets = targetClient.SentPackets.Skip(initialPacketCount).ToList();
                Assert.Contains(newPackets, p => p.PacketId == OpCode.EntityCombatEvent);
                
                // Note: We don't read the exact damage from the packet here because we know target.Health drops natively.
            }

            // Assert
            Assert.Equal(0, target.Health);
            Assert.True(attackCount >= 2 && attackCount <= 3, $"Took {attackCount} attacks to kill target. Expected 2 or 3.");
            
            // Try one more attack now that target is dead
            attacker.LastAttackTime = DateTime.UtcNow.AddSeconds(-2);
            using var deadWritePacket = new Packet(OpCode.EntityAttackRequest);
            deadWritePacket.Write(target.Id);
            using var deadPacket = new Packet(deadWritePacket.ToArray());
            
            int finalPacketCount = attackerClient.SentPackets.Count;
            CombatHandler.HandleAttackRequest(attackerClient, deadPacket);
            
            // Should be ignored because target is dead (no new combat events broadcasted)
            Assert.Equal(finalPacketCount, attackerClient.SentPackets.Count);
        }
    }
}
