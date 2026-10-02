using System;
using System.Linq;
using System.Threading;
using Server.Handlers;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class MobAITests
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
        public void PassiveMob_DoesNotAggroOnSight_RetaliatesWhenAttacked()
        {
            var map = SetupFreshMap();

            var playerClient = new MockClientConnection { AccountId = 1, PlayerId = 101 };
            var player = new Player(101, "PlayerOne", playerClient)
            {
                Position = new Vector3(2, 0, 0),
                Health = 100,
                Attack = 20,
                CritChance = 0f,
                DodgeChance = 0f
            };
            map.AddPlayer(player);

            var mob = new NPC(201, "NeutralDeer")
            {
                BehaviorType = MobBehaviorType.Passive,
                Position = new Vector3(0, 0, 0),
                SpawnPosition = new Vector3(0, 0, 0),
                AggroRadius = 10f,
                Health = 50,
                MaxHealth = 50,
                Defense = 0,
                DodgeChance = 0f,
                CritChance = 0f
            };
            map.AddNPC(mob);

            // Step 1: Tick AI with player standing inside aggro radius
            mob.UpdateAI(map, 0.1f);
            Assert.Equal(AIState.Idle, mob.CurrentState);
            Assert.Null(mob.Target);

            // Step 2: Player attacks the passive mob
            player.KnownEntities.Add(mob.Id);
            using var writePacket = new Packet(OpCode.EntityAttackRequest);
            writePacket.Write(mob.Id);
            using var attackPacket = new Packet(writePacket.ToArray());

            CombatHandler.HandleAttackRequest(playerClient, attackPacket);

            // Assert mob took damage and retaliated
            Assert.True(mob.Health < 50, "Mob should have lost health after attack.");
            Assert.Equal(player, mob.Target);
            Assert.Equal(AIState.Chase, mob.CurrentState);
        }

        [Fact]
        public void AggressiveMob_AggrosOnSight_ChasesAndAttacksPlayer()
        {
            var map = SetupFreshMap();

            var playerClient = new MockClientConnection { AccountId = 2, PlayerId = 102 };
            var player = new Player(102, "Hero", playerClient)
            {
                Position = new Vector3(5, 0, 0),
                Health = 100,
                MaxHealth = 100,
                Defense = 0,
                CritChance = 0f,
                DodgeChance = 0f
            };
            map.AddPlayer(player);

            var mob = new NPC(202, "AngryGoblin")
            {
                BehaviorType = MobBehaviorType.Aggressive,
                Position = new Vector3(0, 0, 0),
                SpawnPosition = new Vector3(0, 0, 0),
                AggroRadius = 10f,
                AttackRange = 2f,
                Attack = 25,
                CritChance = 0f,
                DodgeChance = 0f,
                BaseAttackSpeed = 0.5f,
                LastAttackTime = DateTime.UtcNow.AddSeconds(-2)
            };
            map.AddNPC(mob);

            // Step 1: Mob detects player in aggro radius and starts chasing
            mob.UpdateAI(map, 0.1f);
            Assert.Equal(player, mob.Target);
            Assert.Equal(AIState.Chase, mob.CurrentState);

            // Step 2: Move player into melee range and tick AI
            player.Position = new Vector3(1.5f, 0, 0); // Within AttackRange (2.0f)
            mob.UpdateAI(map, 0.1f);

            // Should have transitioned to Attack and struck the player
            Assert.Equal(AIState.Attack, mob.CurrentState);
            Assert.True(player.Health < 100, "Player should have taken damage from mob attack.");

            // Verify combat event and vitals update sent to player
            Assert.Contains(playerClient.SentPackets, p => p.PacketId == OpCode.EntityCombatEvent);
            Assert.Contains(playerClient.SentPackets, p => p.PacketId == OpCode.VitalsUpdate);
        }

        [Fact]
        public void FriendlyNpc_CannotBeAttacked_DoesNotAggro()
        {
            var map = SetupFreshMap();

            var playerClient = new MockClientConnection { AccountId = 3, PlayerId = 103 };
            var player = new Player(103, "Villager", playerClient)
            {
                Position = new Vector3(1, 0, 0),
                Health = 100,
                Attack = 25
            };
            map.AddPlayer(player);

            var blacksmith = new NPC(203, "Town Blacksmith")
            {
                BehaviorType = MobBehaviorType.Friendly,
                Position = new Vector3(0, 0, 0),
                SpawnPosition = new Vector3(0, 0, 0),
                Health = 500,
                MaxHealth = 500
            };
            map.AddNPC(blacksmith);

            player.KnownEntities.Add(blacksmith.Id);
            using var writePacket = new Packet(OpCode.EntityAttackRequest);
            writePacket.Write(blacksmith.Id);
            using var attackPacket = new Packet(writePacket.ToArray());

            CombatHandler.HandleAttackRequest(playerClient, attackPacket);

            // Friendly NPC must not lose health, change target, or change state
            Assert.Equal(500, blacksmith.Health);
            Assert.Null(blacksmith.Target);
            Assert.Equal(AIState.Idle, blacksmith.CurrentState);
        }

        [Fact]
        public void PackMob_AlertsNearbyPackMembersWhenAttacked()
        {
            var map = SetupFreshMap();

            var playerClient = new MockClientConnection { AccountId = 4, PlayerId = 104 };
            var player = new Player(104, "Adventurer", playerClient)
            {
                Position = new Vector3(1, 0, 0),
                Health = 100,
                Attack = 15,
                CritChance = 0f,
                DodgeChance = 0f
            };
            map.AddPlayer(player);

            var mobA = new NPC(204, "Wolf Alpha")
            {
                BehaviorType = MobBehaviorType.Pack,
                Position = new Vector3(0, 0, 0),
                SpawnPosition = new Vector3(0, 0, 0),
                PackAssistRadius = 12f,
                Health = 50,
                MaxHealth = 50,
                DodgeChance = 0f
            };
            var mobB = new NPC(205, "Wolf Packmate")
            {
                BehaviorType = MobBehaviorType.Pack,
                Position = new Vector3(3, 0, 0), // Within 12f assist radius
                SpawnPosition = new Vector3(3, 0, 0),
                PackAssistRadius = 12f,
                Health = 50,
                MaxHealth = 50,
                DodgeChance = 0f
            };
            var mobC = new NPC(206, "DistantWolf")
            {
                BehaviorType = MobBehaviorType.Pack,
                Position = new Vector3(50, 0, 0), // Outside assist radius
                SpawnPosition = new Vector3(50, 0, 0),
                PackAssistRadius = 12f,
                Health = 50,
                MaxHealth = 50,
                DodgeChance = 0f
            };

            map.AddNPC(mobA);
            map.AddNPC(mobB);
            map.AddNPC(mobC);

            // Player attacks Mob A
            player.KnownEntities.Add(mobA.Id);
            using var writePacket = new Packet(OpCode.EntityAttackRequest);
            writePacket.Write(mobA.Id);
            using var attackPacket = new Packet(writePacket.ToArray());

            CombatHandler.HandleAttackRequest(playerClient, attackPacket);

            // Mob A retaliates
            Assert.Equal(player, mobA.Target);
            Assert.Equal(AIState.Chase, mobA.CurrentState);

            // Mob B (nearby packmate) should have been alerted and joined the chase!
            Assert.Equal(player, mobB.Target);
            Assert.Equal(AIState.Chase, mobB.CurrentState);

            // Mob C (distant packmate) should not be alerted
            Assert.Null(mobC.Target);
            Assert.Equal(AIState.Idle, mobC.CurrentState);
        }

        [Fact]
        public void Mob_LeashesAndReturnsToSpawn_WhenPlayerRunsTooFar()
        {
            var map = SetupFreshMap();

            var playerClient = new MockClientConnection { AccountId = 5, PlayerId = 105 };
            var player = new Player(105, "Runner", playerClient)
            {
                Position = new Vector3(5, 0, 0),
                Health = 100
            };
            map.AddPlayer(player);

            var mob = new NPC(207, "GuardDog")
            {
                BehaviorType = MobBehaviorType.Aggressive,
                Position = new Vector3(4, 0, 0),
                SpawnPosition = new Vector3(0, 0, 0),
                LeashRadius = 15f,
                Health = 40,
                MaxHealth = 100,
                Target = player,
                CurrentState = AIState.Chase
            };
            map.AddNPC(mob);

            // Player runs 40 units away (far beyond 15f LeashRadius)
            player.Position = new Vector3(40, 0, 0);

            // Tick mob AI
            mob.UpdateAI(map, 0.1f);

            // Mob should drop target and start returning to spawn
            Assert.Null(mob.Target);
            Assert.Equal(AIState.ReturnToSpawn, mob.CurrentState);

            // Move mob close to spawn position and tick again
            mob.Position = new Vector3(0.1f, 0, 0);
            mob.UpdateAI(map, 0.1f);

            // Mob should be back at spawn, Idle, and fully healed
            Assert.Equal(AIState.Idle, mob.CurrentState);
            Assert.Equal(mob.MaxHealth, mob.Health);
            Assert.Equal(mob.SpawnPosition.X, mob.Position.X);
        }

        [Fact]
        public void Mob_DiesAndRespawnsAfterTimer()
        {
            var map = SetupFreshMap();

            var mob = new NPC(208, "Zombie")
            {
                BehaviorType = MobBehaviorType.Aggressive,
                Position = new Vector3(5, 5, 0),
                SpawnPosition = new Vector3(0, 0, 0),
                Health = 50,
                MaxHealth = 50,
                RespawnTimeSeconds = 0.2f
            };
            map.AddNPC(mob);

            // Fatal blow
            mob.Die(map);
            Assert.Equal(AIState.Dead, mob.CurrentState);
            Assert.Equal(0, mob.Health);

            // Tick immediately -> should still be dead
            mob.UpdateAI(map, 0.05f);
            Assert.Equal(AIState.Dead, mob.CurrentState);

            // Wait for respawn timer to elapse
            Thread.Sleep(250);

            // Tick AI again -> mob should have respawned at SpawnPosition
            mob.UpdateAI(map, 0.05f);
            Assert.Equal(AIState.Idle, mob.CurrentState);
            Assert.Equal(mob.MaxHealth, mob.Health);
            Assert.Equal(mob.SpawnPosition.X, mob.Position.X);
            Assert.Equal(mob.SpawnPosition.Y, mob.Position.Y);
        }
    }
}
