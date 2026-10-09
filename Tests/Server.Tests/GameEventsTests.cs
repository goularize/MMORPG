using System;
using System.Collections.Generic;
using Server.Handlers;
using Server.World;
using Server.World.Entities;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class GameEventsTests : NpcWorldTestBase
    {
        [Fact]
        public void AKill_IsCountedOnce_ForTheKillerOnly()
        {
            var (_, killer) = AddPlayer("Killer");
            var (_, bystander) = AddPlayer("Bystander");
            var slime = AddNpc(101, "Slime");

            Kill(slime, killer);

            Assert.Equal(1, killer.Progress.GetKillCount(101));
            Assert.Equal(0, bystander.Progress.GetKillCount(101));
        }

        [Fact]
        public void KillingAnAlreadyDeadNpcAgain_DoesNotCountTwice()
        {
            var (_, killer) = AddPlayer();
            var slime = AddNpc(101, "Slime");

            Kill(slime, killer);
            Kill(slime, killer);

            Assert.Equal(1, killer.Progress.GetKillCount(101));
        }

        [Fact]
        public void ADeathWithoutAKiller_RaisesNothing()
        {
            var raised = 0;
            void OnKilled(Player p, int templateId) => raised++;
            GameEvents.Instance.NpcKilled += OnKilled;
            try
            {
                AddNpc(101, "Slime").Die(Map, killer: null);
            }
            finally
            {
                GameEvents.Instance.NpcKilled -= OnKilled;
            }

            Assert.Equal(0, raised);
        }

        [Fact]
        public void KillCountsAreUpdatedBeforeOtherSubscribersRun()
        {
            var (_, killer) = AddPlayer();
            int seenByLaterSubscriber = -1;
            void OnKilled(Player p, int templateId) => seenByLaterSubscriber = p.Progress.GetKillCount(templateId);
            GameEvents.Instance.NpcKilled += OnKilled;
            try
            {
                Kill(AddNpc(101, "Slime"), killer);
            }
            finally
            {
                GameEvents.Instance.NpcKilled -= OnKilled;
            }

            Assert.Equal(1, seenByLaterSubscriber);
        }

        [Fact]
        public void AFailingSubscriber_DoesNotStopTheOthers_OrTheKill()
        {
            var (_, killer) = AddPlayer();
            var reached = false;
            void Thrower(Player p, int templateId) => throw new InvalidOperationException("boom");
            void After(Player p, int templateId) => reached = true;
            GameEvents.Instance.NpcKilled += Thrower;
            GameEvents.Instance.NpcKilled += After;
            try
            {
                Kill(AddNpc(101, "Slime"), killer);
            }
            finally
            {
                GameEvents.Instance.NpcKilled -= Thrower;
                GameEvents.Instance.NpcKilled -= After;
            }

            Assert.True(reached);
            Assert.Equal(1, killer.Progress.GetKillCount(101));
        }

        [Fact]
        public void BackpackOperations_RaiseItemGainedAndItemLost_WithTheQuantities()
        {
            var (_, player) = AddPlayer();
            var gained = new List<(int, int)>();
            var lost = new List<(int, int)>();
            void OnGained(Player p, int id, int qty) => gained.Add((id, qty));
            void OnLost(Player p, int id, int qty) => lost.Add((id, qty));
            GameEvents.Instance.ItemGained += OnGained;
            GameEvents.Instance.ItemLost += OnLost;
            try
            {
                InventoryOps.AddToBackpack(player, 3001, 7);
                InventoryOps.RemoveFromBackpack(player, 3001, 5);
                InventoryOps.RemoveFromBackpack(player, 3001, 50); // only 2 left: only 2 are lost
            }
            finally
            {
                GameEvents.Instance.ItemGained -= OnGained;
                GameEvents.Instance.ItemLost -= OnLost;
            }

            Assert.Equal(new[] { (3001, 7) }, gained);
            Assert.Equal(new[] { (3001, 5), (3001, 2) }, lost);
        }

        [Fact]
        public void DroppingItems_RaisesItemLost()
        {
            var (client, player) = AddPlayer();
            Give(player, 3001, 10);
            var slot = player.Inventory[0].SlotIndex;
            var lost = new List<(int, int)>();
            void OnLost(Player p, int id, int qty) => lost.Add((id, qty));
            GameEvents.Instance.ItemLost += OnLost;
            try
            {
                using var write = new Packet(OpCode.DropItemRequest);
                write.Write(0);
                write.Write(slot);
                write.Write(4);
                InventoryHandler.HandleDropItem(client, new Packet(write.ToArray()));
            }
            finally
            {
                GameEvents.Instance.ItemLost -= OnLost;
            }

            Assert.Equal(new[] { (3001, 4) }, lost);
            Assert.Equal(6, Held(player, 3001));
        }

        [Fact]
        public void LootingASatchel_RaisesItemGained_ForWhatActuallyMoved()
        {
            var (client, player) = AddPlayer();
            Give(player, 3001, 90); // 9 free in that stack
            var item = ItemFactory.CreateItem(3001, 0, 15)!;
            var satchel = new LootSatchel(player.Position, player.Id, 0, new List<Server.Database.Models.CharacterItem> { item });
            Map.SpawnLootSatchel(satchel);
            var gained = new List<(int, int)>();
            void OnGained(Player p, int id, int qty) => gained.Add((id, qty));
            GameEvents.Instance.ItemGained += OnGained;
            try
            {
                using var write = new Packet(OpCode.LootAllRequest);
                write.Write(satchel.Id);
                LootHandler.HandleLootAll(client, new Packet(write.ToArray()));
            }
            finally
            {
                GameEvents.Instance.ItemGained -= OnGained;
            }

            Assert.Equal(new[] { (3001, 15) }, gained);
            Assert.Equal(105, Held(player, 3001));
        }
    }
}
