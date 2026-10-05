using System;
using System.Linq;
using Server.Handlers;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class ProgressionTests
    {
        private MapInstance SetupFreshMap()
        {
            var map = GameLogic.MapMgr.GetMap(1);
            if (map == null)
            {
                map = new MapInstance(1);
                GameLogic.MapMgr.ActiveMaps.TryAdd(1, map);
            }
            map.Players.Clear();
            map.NPCs.Clear();
            return map;
        }

        [Fact]
        public void Player_AddExp_LevelsUpAndRestoresVitals()
        {
            var map = SetupFreshMap();
            var client = new MockClientConnection { AccountId = 1, PlayerId = 1001 };
            var player = new Player(1001, "Hero", client)
            {
                Level = 1,
                Exp = 0,
                StatPoints = 0,
                Constitution = 10,
                Knowledge = 10,
                Strength = 10,
                Intelligence = 10
            };
            player.CalculateDerivedStats();

            // Damage player beforehand
            player.Health = 20;
            player.Mana = 10;
            map.AddPlayer(player);

            long reqExp = ServerConfig.GetExpForNextLevel(1); // 100 EXP

            // Act: Grant exactly enough EXP to level up
            player.AddExp(reqExp);

            // Assert
            Assert.Equal(2, player.Level);
            Assert.Equal(0, player.Exp);
            Assert.Equal(ServerConfig.StatPointsPerLevel, player.StatPoints);

            // Vitals must be fully restored upon level-up
            Assert.Equal(player.MaxHealth, player.Health);
            Assert.Equal(player.MaxMana, player.Mana);

            // Packets sent
            Assert.Contains(client.SentPackets, p => p.PacketId == OpCode.PlayerLevelUp);
            Assert.Contains(client.SentPackets, p => p.PacketId == OpCode.PlayerExpUpdate);
            Assert.Contains(client.SentPackets, p => p.PacketId == OpCode.StatsUpdate);
            Assert.Contains(client.SentPackets, p => p.PacketId == OpCode.VitalsUpdate);
        }

        [Fact]
        public void Player_AddExp_MultipleLevelsRollover()
        {
            var map = SetupFreshMap();
            var client = new MockClientConnection { AccountId = 2, PlayerId = 1002 };
            var player = new Player(1002, "SuperHero", client)
            {
                Level = 1,
                Exp = 0,
                StatPoints = 0
            };
            player.CalculateDerivedStats();
            map.AddPlayer(player);

            long lvl1Exp = ServerConfig.GetExpForNextLevel(1); // 100
            long lvl2Exp = ServerConfig.GetExpForNextLevel(2); // 282
            long totalForTwoLevels = lvl1Exp + lvl2Exp; // 382

            // Grant 400 EXP -> should reach Level 3 with 18 leftover EXP
            player.AddExp(totalForTwoLevels + 18);

            Assert.Equal(3, player.Level);
            Assert.Equal(18, player.Exp);
            Assert.Equal(ServerConfig.StatPointsPerLevel * 2, player.StatPoints);
        }

        [Fact]
        public void MobDeath_AwardsExpToAttackingPlayer()
        {
            var map = SetupFreshMap();
            var client = new MockClientConnection { AccountId = 3, PlayerId = 1003 };
            var player = new Player(1003, "MonsterHunter", client)
            {
                Level = 1,
                Exp = 0,
                CritChance = 0f,
                DodgeChance = 0f,
                BaseAttackSpeed = 0.5f,
                Position = new Vector3(0, 0, 0)
            };
            player.CalculateDerivedStats();
            player.Health = player.MaxHealth;
            player.Attack = 50;
            map.AddPlayer(player);

            long reqExp = ServerConfig.GetExpForNextLevel(1); // 100
            var mob = new NPC(2001, "Slime")
            {
                BehaviorType = MobBehaviorType.Aggressive,
                Position = new Vector3(1, 0, 0),
                Health = 10,
                MaxHealth = 10,
                Defense = 0,
                DodgeChance = 0f,
                ExpYield = reqExp
            };
            map.AddNPC(mob);

            player.KnownEntities.Add(mob.Id);
            using var writePacket = new Packet(OpCode.EntityAttackRequest);
            writePacket.Write(mob.Id);
            using var packet = new Packet(writePacket.ToArray());

            // Act: Fatal strike on mob
            CombatHandler.HandleAttackRequest(client, packet);

            // Assert mob died
            Assert.Equal(0, mob.Health);
            Assert.Equal(AIState.Dead, mob.CurrentState);

            // Assert player was awarded EXP and leveled up
            Assert.Equal(2, player.Level);
            Assert.Equal(ServerConfig.StatPointsPerLevel, player.StatPoints);
        }

        [Fact]
        public void ProgressionHandler_AllocateStatPoint_IncreasesAttributeAndRecalculatesDerivedStats()
        {
            var map = SetupFreshMap();
            var client = new MockClientConnection { AccountId = 4, PlayerId = 1004 };
            var player = new Player(1004, "Warrior", client)
            {
                Level = 1,
                StatPoints = 5,
                Strength = 10,
                Constitution = 10,
                Intelligence = 10,
                Knowledge = 10
            };
            player.CalculateDerivedStats();
            int originalAttack = player.Attack;
            map.AddPlayer(player);

            // Request allocating 3 points into Strength (StatType.Strength = 0)
            using var writePacket = new Packet(OpCode.AllocateStatPointRequest);
            writePacket.Write((byte)StatType.Strength);
            writePacket.Write(3);
            using var packet = new Packet(writePacket.ToArray());

            // Act
            ProgressionHandler.HandleAllocateStatPoint(client, packet);

            // Assert
            Assert.Equal(13, player.Strength);
            Assert.Equal(2, player.StatPoints);
            Assert.True(player.Attack > originalAttack, "Attack derived stat should increase with Strength.");

            // Response packet verification
            var response = client.SentPackets.FirstOrDefault(p => p.PacketId == OpCode.AllocateStatPointResponse);
            Assert.NotNull(response);
            Assert.True(response.ReadBool()); // isSuccess
            Assert.Equal((byte)StatType.Strength, response.ReadByte());
            Assert.Equal(13, response.ReadInt()); // newStatValue
            Assert.Equal(2, response.ReadInt());  // remainingStatPoints

            // Verify StatsUpdate was emitted
            Assert.Contains(client.SentPackets, p => p.PacketId == OpCode.StatsUpdate);
        }

        [Fact]
        public void ProgressionHandler_AllocateStatPoint_RejectsWhenInsufficientPoints()
        {
            var map = SetupFreshMap();
            var client = new MockClientConnection { AccountId = 5, PlayerId = 1005 };
            var player = new Player(1005, "Mage", client)
            {
                Level = 1,
                StatPoints = 1,
                Intelligence = 10
            };
            player.CalculateDerivedStats();
            map.AddPlayer(player);

            // Try allocating 5 points when only having 1 point
            using var writePacket = new Packet(OpCode.AllocateStatPointRequest);
            writePacket.Write((byte)StatType.Intelligence);
            writePacket.Write(5);
            using var packet = new Packet(writePacket.ToArray());

            // Act
            ProgressionHandler.HandleAllocateStatPoint(client, packet);

            // Assert
            Assert.Equal(10, player.Intelligence); // Unchanged
            Assert.Equal(1, player.StatPoints);    // Unchanged

            var response = client.SentPackets.FirstOrDefault(p => p.PacketId == OpCode.AllocateStatPointResponse);
            Assert.NotNull(response);
            Assert.False(response.ReadBool()); // isSuccess == false
        }

        [Fact]
        public void PlayerProgressionSync_PacketStructure_MatchesExpectedPayload()
        {
            // Simulate packet written by Server
            using var writePacket = new Packet(OpCode.PlayerProgressionSync);
            writePacket.Write(5);             // Level
            writePacket.Write(250L);          // Exp
            writePacket.Write(500L);          // ExpToNextLevel
            writePacket.Write(10);            // StatPoints
            writePacket.Write(15);            // Strength
            writePacket.Write(12);            // Intelligence
            writePacket.Write(14);            // Constitution
            writePacket.Write(11);            // Knowledge

            using var readPacket = new Packet(writePacket.ToArray());
            Assert.Equal(OpCode.PlayerProgressionSync, readPacket.PacketId);
            Assert.Equal(5, readPacket.ReadInt());
            Assert.Equal(250L, readPacket.ReadLong());
            Assert.Equal(500L, readPacket.ReadLong());
            Assert.Equal(10, readPacket.ReadInt());
            Assert.Equal(15, readPacket.ReadInt());
            Assert.Equal(12, readPacket.ReadInt());
            Assert.Equal(14, readPacket.ReadInt());
            Assert.Equal(11, readPacket.ReadInt());
        }
    }
}
