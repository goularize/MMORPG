using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Server.World;
using Server.World.Entities;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class LootAndHarvestingTests : IDisposable
    {
        private AppDbContext GetInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        public LootAndHarvestingTests()
        {
            DataManager.Initialize();

            if (!GameLogic.MapMgr.ActiveMaps.ContainsKey(1))
            {
                GameLogic.MapMgr.ActiveMaps.TryAdd(1, new MapInstance(1));
            }
        }

        public void Dispose()
        {
            AppDbContext.Factory = () => new AppDbContext();
        }

        [Fact]
        public void LootTables_LoadedSuccessfully_AndContainExpectedMobs()
        {
            Assert.NotEmpty(DataManager.LootTables);
            Assert.True(DataManager.LootTables.ContainsKey(100));  // Goblin
            Assert.True(DataManager.LootTables.ContainsKey(202));  // Angry Goblin
            Assert.True(DataManager.LootTables.ContainsKey(204));  // Wolf Alpha
            Assert.True(DataManager.LootTables.ContainsKey(208));  // Zombie
            Assert.True(DataManager.LootTables.ContainsKey(101)); // Slime

            var goblinTable = DataManager.LootTables[202];
            Assert.InRange(goblinTable.MinGold, 1, 10);
            Assert.InRange(goblinTable.MaxGold, 10, 30);
            Assert.NotEmpty(goblinTable.Entries);
        }

        [Fact]
        public void LootSatchel_OwnershipRights_ExclusivityFor30Seconds()
        {
            var position = new Vector3(10, 0, 10);
            int killerId = 101;
            int strangerId = 102;

            var satchel = new LootSatchel(position, killerId, 50);

            var killerClient = new MockClientConnection { AccountId = 1, PlayerId = killerId };
            var killer = new Player(killerId, "KillerHero", killerClient) { MapId = 1, Position = position };

            var strangerClient = new MockClientConnection { AccountId = 2, PlayerId = strangerId };
            var stranger = new Player(strangerId, "CuriousStranger", strangerClient) { MapId = 1, Position = position };

            // Killer always has loot rights
            Assert.True(satchel.CanLoot(killer));

            // Stranger is denied during the first 30 seconds
            Assert.False(satchel.CanLoot(stranger));
            Assert.False(satchel.IsPublic);
            Assert.False(satchel.IsExpired);

            // Fast-forward past 30 seconds (ownership expires, public access begins)
            satchel.CreatedTime = DateTime.UtcNow.AddSeconds(-31);
            Assert.True(satchel.IsPublic);
            Assert.True(satchel.CanLoot(stranger));
            Assert.False(satchel.IsExpired);

            // Fast-forward past 120 seconds (decay expired)
            satchel.CreatedTime = DateTime.UtcNow.AddSeconds(-121);
            Assert.True(satchel.IsExpired);
        }

        [Fact]
        public void MobDeath_SpawnsLootSatchel_WithDrops()
        {
            var map = GameLogic.MapMgr.GetMap(1)!;
            var killerClient = new MockClientConnection { AccountId = 1, PlayerId = 201 };
            var killer = new Player(201, "Slayer", killerClient) { MapId = 1, Position = new Vector3(5, 0, 5) };
            map.AddPlayer(killer);

            var mob = new NPC(88801, "Angry Goblin")
            {
                TemplateId = 202,
                Position = new Vector3(5, 0, 5),
                SpawnPosition = new Vector3(5, 0, 5)
            };
            mob.CalculateDerivedStats();
            map.AddNPC(mob);

            int initialSatchelCount = map.LootSatchels.Count;

            // Kill the mob
            mob.Die(map, killer);

            // Ensure satchel spawned
            Assert.True(map.LootSatchels.Count > initialSatchelCount);
            var spawnedSatchel = map.LootSatchels.Values.FirstOrDefault(s => s.OwnerPlayerId == killer.Id);
            Assert.NotNull(spawnedSatchel);
            Assert.Equal(killer.Id, spawnedSatchel.OwnerPlayerId);
            Assert.True(spawnedSatchel.Gold > 0 || spawnedSatchel.Items.Count > 0);
        }

        [Fact]
        public void HandleOpenLootSatchel_SyncsContents_AndEnforcesDistanceAndOwnership()
        {
            var map = GameLogic.MapMgr.GetMap(1)!;

            var killerClient = new MockClientConnection { AccountId = 1, PlayerId = 301 };
            var killer = new Player(301, "LootInspector", killerClient) { MapId = 1, Position = new Vector3(10, 0, 10) };
            map.Players[301] = killer;

            var strangerClient = new MockClientConnection { AccountId = 2, PlayerId = 302 };
            var stranger = new Player(302, "LootThief", strangerClient) { MapId = 1, Position = new Vector3(10, 0, 10) };
            map.Players[302] = stranger;

            var item = ItemFactory.CreateItem(1001, 0, 1)!;
            var satchel = new LootSatchel(new Vector3(10, 0, 10), killer.Id, 75, new[] { item });
            map.SpawnLootSatchel(satchel);

            // 1. Stranger attempts to open during exclusive ownership -> Rejected
            using (var openPacket = new Packet(OpCode.OpenLootSatchelRequest))
            {
                openPacket.Write(satchel.Id);
                using var readPacket = new Packet(openPacket.ToArray());
                LootHandler.HandleOpenLootSatchel(strangerClient, readPacket);
            }

            Assert.Contains(strangerClient.SentPackets, p => p.PacketId == OpCode.LootSatchelClose);

            // 2. Killer attempts to open from too far away -> Rejected
            killer.Position = new Vector3(50, 0, 50);
            using (var openPacket = new Packet(OpCode.OpenLootSatchelRequest))
            {
                openPacket.Write(satchel.Id);
                using var readPacket = new Packet(openPacket.ToArray());
                LootHandler.HandleOpenLootSatchel(killerClient, readPacket);
            }

            Assert.Contains(killerClient.SentPackets, p => p.PacketId == OpCode.LootSatchelClose);

            // 3. Killer opens within range -> Receives LootSatchelSync
            killer.Position = new Vector3(11, 0, 10);
            killerClient.SentPackets.Clear();
            using (var openPacket = new Packet(OpCode.OpenLootSatchelRequest))
            {
                openPacket.Write(satchel.Id);
                using var readPacket = new Packet(openPacket.ToArray());
                LootHandler.HandleOpenLootSatchel(killerClient, readPacket);
            }

            var syncPacket = killerClient.SentPackets.FirstOrDefault(p => p.PacketId == OpCode.LootSatchelSync);
            Assert.NotNull(syncPacket);
            Assert.Equal(satchel.Id, syncPacket.ReadInt());
            Assert.Equal(75, syncPacket.ReadLong());
            Assert.Equal(1, syncPacket.ReadInt()); // 1 item
        }

        [Fact]
        public void HandleLootItem_MovesItemToPlayerInventory_AndSaves()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var map = GameLogic.MapMgr.GetMap(1)!;
            var killerClient = new MockClientConnection { AccountId = 1, PlayerId = 401 };
            var killer = new Player(401, "ItemPicker", killerClient) { MapId = 1, Position = new Vector3(0, 0, 0) };
            map.Players[401] = killer;

            var sword = ItemFactory.CreateItem(1001, 0, 1)!;
            var potion = ItemFactory.CreateItem(2001, 0, 3)!;
            var satchel = new LootSatchel(new Vector3(0, 0, 0), killer.Id, 20, new[] { sword, potion });
            map.SpawnLootSatchel(satchel);

            // Loot the sword
            using (var lootPacket = new Packet(OpCode.LootItemRequest))
            {
                lootPacket.Write(satchel.Id);
                lootPacket.Write(sword.Id.ToString());
                using var readPacket = new Packet(lootPacket.ToArray());
                LootHandler.HandleLootItem(killerClient, readPacket);
            }

            // Verify sword is now in player inventory
            Assert.Contains(killer.Inventory, i => i.Id == sword.Id && i.TemplateId == 1001);
            // Verify sword is removed from satchel
            Assert.DoesNotContain(satchel.Items, i => i.Id == sword.Id);
            // Potion still remains in satchel
            Assert.Contains(satchel.Items, i => i.Id == potion.Id);
            // Satchel not yet empty
            Assert.False(satchel.IsEmpty);
        }

        [Fact]
        public void HandleLootAll_TransfersGoldAndItems_AndDespawnsEmptySatchel()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var map = GameLogic.MapMgr.GetMap(1)!;
            var killerClient = new MockClientConnection { AccountId = 1, PlayerId = 501 };
            var killer = new Player(501, "LootAllHero", killerClient) { MapId = 1, Position = new Vector3(0, 0, 0), Gold = 10 };
            map.Players[501] = killer;

            var sword = ItemFactory.CreateItem(1001, 0, 1)!;
            var ore = ItemFactory.CreateItem(3001, 0, 5)!;
            var satchel = new LootSatchel(new Vector3(0, 0, 0), killer.Id, 45, new[] { sword, ore });
            map.SpawnLootSatchel(satchel);

            Assert.True(map.LootSatchels.ContainsKey(satchel.Id));

            // Call LootAll
            using (var lootAllPacket = new Packet(OpCode.LootAllRequest))
            {
                lootAllPacket.Write(satchel.Id);
                using var readPacket = new Packet(lootAllPacket.ToArray());
                LootHandler.HandleLootAll(killerClient, readPacket);
            }

            // Gold transferred
            Assert.Equal(55, killer.Gold);
            Assert.Equal(0, satchel.Gold);

            // Items transferred
            Assert.Equal(2, killer.Inventory.Count);
            Assert.Empty(satchel.Items);
            Assert.True(satchel.IsEmpty);

            // Satchel despawned from map
            Assert.False(map.LootSatchels.ContainsKey(satchel.Id));
            Assert.Contains(killerClient.SentPackets, p => p.PacketId == OpCode.LootSatchelClose);
        }

        [Fact]
        public void InventoryFull_RejectsLooting_KeepsItemInSatchel()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            var map = GameLogic.MapMgr.GetMap(1)!;
            var killerClient = new MockClientConnection { AccountId = 1, PlayerId = 601 };
            var killer = new Player(601, "FullBags", killerClient)
            {
                MapId = 1,
                Position = new Vector3(0, 0, 0),
                InventorySlots = 2 // Small inventory for test
            };
            map.Players[601] = killer;

            // Fill all 2 slots
            var dummy1 = ItemFactory.CreateItem(1001, killer.Id, 1)!;
            dummy1.SlotIndex = 0;
            killer.Inventory.Add(dummy1);

            var dummy2 = ItemFactory.CreateItem(1002, killer.Id, 1)!;
            dummy2.SlotIndex = 1;
            killer.Inventory.Add(dummy2);

            var extraSword = ItemFactory.CreateItem(1004, 0, 1)!;
            var satchel = new LootSatchel(new Vector3(0, 0, 0), killer.Id, 0, new[] { extraSword });
            map.SpawnLootSatchel(satchel);

            // Attempt to loot with full inventory
            using (var lootPacket = new Packet(OpCode.LootItemRequest))
            {
                lootPacket.Write(satchel.Id);
                lootPacket.Write(extraSword.Id.ToString());
                using var readPacket = new Packet(lootPacket.ToArray());
                LootHandler.HandleLootItem(killerClient, readPacket);
            }

            // Item still in satchel
            Assert.Contains(satchel.Items, i => i.Id == extraSword.Id);
            // Inventory remained at 2
            Assert.Equal(2, killer.Inventory.Count);
            // Sent LootSatchelClose with "Inventory is full."
            Assert.Contains(killerClient.SentPackets, p => p.PacketId == OpCode.LootSatchelClose);
        }

        [Fact]
        public void MapInstance_Update_DespawnsExpiredSatchel()
        {
            var map = GameLogic.MapMgr.GetMap(1)!;
            var satchel = new LootSatchel(new Vector3(0, 0, 0), 701, 100);
            map.SpawnLootSatchel(satchel);
            Assert.True(map.LootSatchels.ContainsKey(satchel.Id));

            // Set time past decay duration
            satchel.CreatedTime = DateTime.UtcNow.AddSeconds(-125);
            Assert.True(satchel.IsExpired);

            // Trigger Map Update
            map.Update();

            // Satchel should be automatically removed
            Assert.False(map.LootSatchels.ContainsKey(satchel.Id));
        }

        [Fact]
        public void ResourceHarvesting_DepletionSpawnsSatchel()
        {
            var map = GameLogic.MapMgr.GetMap(1)!;
            var harvesterClient = new MockClientConnection { AccountId = 1, PlayerId = 801 };
            var harvester = new Player(801, "Woodcutter", harvesterClient) { MapId = 1, Position = new Vector3(20, 0, 20) };
            map.Players[801] = harvester;

            var tree = new Resource(99901, "Oak Tree", new Vector3(20, 0, 20), 30.0f);
            map.Resources[tree.Id] = tree;

            // Chop down tree to 0 HP
            tree.TakeDamage(100, map, harvester);

            Assert.True(tree.IsDepleted);

            // Verify a satchel was dropped by harvester
            var harvestSatchel = map.LootSatchels.Values.FirstOrDefault(s => s.OwnerPlayerId == harvester.Id && s.Position == tree.Position);
            Assert.NotNull(harvestSatchel);
            Assert.Contains(harvestSatchel.Items, i => i.TemplateId == 3003); // Oak Wood
        }
    }
}
