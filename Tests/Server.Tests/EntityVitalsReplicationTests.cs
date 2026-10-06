using System.Linq;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class EntityVitalsReplicationTests
    {
        private static MapInstance SetupFreshMap()
        {
            var map = GameLogic.MapMgr.GetMap(1);
            if (map == null)
            {
                map = new MapInstance(1);
                GameLogic.MapMgr.ActiveMaps.TryAdd(1, map);
            }
            map.Players.Clear();
            map.NPCs.Clear();
            map.Resources.Clear();
            map.LootSatchels.Clear();
            return map;
        }

        private static (Player player, MockClientConnection client) AddObserver(MapInstance map, int id, Vector3 position)
        {
            var client = new MockClientConnection { AccountId = id, PlayerId = id };
            var player = new Player(id, $"Observer{id}", client) { Position = position, Health = 100, MaxHealth = 100 };
            map.AddPlayer(player);
            return (player, client);
        }

        private static NPC AddNpc(MapInstance map, int id, Vector3 position)
        {
            var npc = new NPC(id, "Slime")
            {
                BehaviorType = MobBehaviorType.Passive,
                Position = position,
                SpawnPosition = position,
                Health = 100,
                MaxHealth = 100
            };
            map.AddNPC(npc);
            return npc;
        }

        private static (int id, int health, int maxHealth)[] Vitals(MockClientConnection client) =>
            client.SentPackets.Where(p => p.PacketId == OpCode.EntityVitals)
                .Select(p => (p.ReadInt(), p.ReadInt(), p.ReadInt())).ToArray();

        private static int[] Deaths(MockClientConnection client) =>
            client.SentPackets.Where(p => p.PacketId == OpCode.EntityDeath).Select(p => p.ReadInt()).ToArray();

        [Fact]
        public void NewObserver_ReceivesCurrentHealth_WithSpawn()
        {
            var map = SetupFreshMap();
            var npc = AddNpc(map, 7001, new Vector3(5, 0, 0));
            npc.Health = 40;
            var (_, observer) = AddObserver(map, 101, new Vector3(0, 0, 0));

            map.Update();

            Assert.Contains(Vitals(observer), v => v == (7001, 40, 100));
            Assert.Empty(Deaths(observer));
        }

        [Fact]
        public void HealthChange_IsBroadcastToObserversInRange_AndOnlyThem()
        {
            var map = SetupFreshMap();
            var npc = AddNpc(map, 7002, new Vector3(5, 0, 0));
            var (_, near) = AddObserver(map, 102, new Vector3(0, 0, 0));
            var (_, far) = AddObserver(map, 103, new Vector3(500, 0, 0));
            map.Update(); // initial spawn + baseline
            near.SentPackets.Clear();
            far.SentPackets.Clear();

            npc.Health = 60;
            map.Update();

            Assert.Equal(new[] { (7002, 60, 100) }, Vitals(near));
            Assert.Empty(Vitals(far));
            Assert.Empty(Deaths(near));
        }

        [Fact]
        public void Unchanged_Health_IsNotRebroadcast()
        {
            var map = SetupFreshMap();
            var npc = AddNpc(map, 7003, new Vector3(5, 0, 0));
            var (_, observer) = AddObserver(map, 104, new Vector3(0, 0, 0));
            map.Update();
            npc.Health = 50;
            map.Update();
            observer.SentPackets.Clear();

            map.Update();
            map.Update();

            Assert.Empty(Vitals(observer));
        }

        [Fact]
        public void Death_BroadcastsVitalsAndEntityDeath_Once()
        {
            var map = SetupFreshMap();
            var npc = AddNpc(map, 7004, new Vector3(5, 0, 0));
            var (_, observer) = AddObserver(map, 105, new Vector3(0, 0, 0));
            map.Update();
            observer.SentPackets.Clear();

            npc.Die(map);
            map.Update();
            map.Update();

            Assert.Equal(new[] { (7004, 0, 100) }, Vitals(observer));
            Assert.Equal(new[] { 7004 }, Deaths(observer));
        }

        [Fact]
        public void PlayerDeath_IsBroadcastToOtherPlayersInRange()
        {
            var map = SetupFreshMap();
            var (victim, _) = AddObserver(map, 106, new Vector3(0, 0, 0));
            var (_, witness) = AddObserver(map, 107, new Vector3(3, 0, 0));
            map.Update();
            witness.SentPackets.Clear();

            victim.Health = 0;
            map.Update();

            Assert.Equal(new[] { (106, 0, 100) }, Vitals(witness));
            Assert.Equal(new[] { 106 }, Deaths(witness));
        }

        [Fact]
        public void LateJoiner_SeesCorpseAsDead()
        {
            var map = SetupFreshMap();
            var npc = AddNpc(map, 7005, new Vector3(5, 0, 0));
            npc.Die(map);
            map.Update(); // baseline recorded with no observers
            var (_, late) = AddObserver(map, 108, new Vector3(0, 0, 0));

            map.Update();

            Assert.Contains(Vitals(late), v => v == (7005, 0, 100));
            Assert.Equal(new[] { 7005 }, Deaths(late));
        }

        [Fact]
        public void Revive_BroadcastsFullHealth_WithoutDeathEvent()
        {
            var map = SetupFreshMap();
            var npc = AddNpc(map, 7006, new Vector3(5, 0, 0));
            var (_, observer) = AddObserver(map, 109, new Vector3(0, 0, 0));
            map.Update();
            npc.Die(map);
            map.Update();
            observer.SentPackets.Clear();

            npc.Health = npc.MaxHealth; // respawn / revive
            map.Update();

            Assert.Equal(new[] { (7006, 100, 100) }, Vitals(observer));
            Assert.Empty(Deaths(observer));
        }

        [Fact]
        public void ResourceDepletionAndRespawn_AreReplicated()
        {
            var map = SetupFreshMap();
            var node = new Resource(8001, "IronOre", new Vector3(4, 0, 0), respawnTime: 60f);
            map.Resources.TryAdd(node.Id, node);
            var (_, observer) = AddObserver(map, 110, new Vector3(0, 0, 0));
            map.Update();
            observer.SentPackets.Clear();

            node.TakeDamage(1000);
            map.Update();

            Assert.Equal(new[] { (8001, 0, 100) }, Vitals(observer));
            Assert.Equal(new[] { 8001 }, Deaths(observer));
        }

        [Fact]
        public void EntityVitals_And_EntityDeath_PacketLayout_IsStable()
        {
            var map = SetupFreshMap();
            var npc = AddNpc(map, 7007, new Vector3(5, 0, 0));
            var (_, observer) = AddObserver(map, 111, new Vector3(0, 0, 0));
            map.Update();
            observer.SentPackets.Clear();
            npc.Die(map);
            map.Update();

            var vitals = observer.SentPackets.Single(p => p.PacketId == OpCode.EntityVitals);
            Assert.Equal((7007, 0, 100), (vitals.ReadInt(), vitals.ReadInt(), vitals.ReadInt()));
            Assert.Throws<System.IO.EndOfStreamException>(() => vitals.ReadByte());

            var death = observer.SentPackets.Single(p => p.PacketId == OpCode.EntityDeath);
            Assert.Equal(7007, death.ReadInt());
            Assert.Throws<System.IO.EndOfStreamException>(() => death.ReadByte());
        }
    }
}
