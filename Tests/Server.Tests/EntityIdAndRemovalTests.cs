using System;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Server.World;
using Server.World.Entities;
using Shared.Constants;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class EntityIdAndRemovalTests
    {
        private static int CountSent(MockClientConnection client, OpCode opCode) =>
            client.SentPackets.Count(p => p.PacketId == opCode);

        private static bool WaitFor(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(20);
            }
            return condition();
        }

        [Fact]
        public void Allocator_IssuesUniqueIncreasingIds_OfTheRequestedKind()
        {
            int a = EntityIdAllocator.Next(EntityIdKind.Npc);
            int b = EntityIdAllocator.Next(EntityIdKind.Npc);
            int satchel = EntityIdAllocator.Next(EntityIdKind.Satchel);

            Assert.True(b > a);
            Assert.Equal(EntityIdKind.Npc, EntityIds.GetKind(a));
            Assert.Equal(EntityIdKind.Satchel, EntityIds.GetKind(satchel));
            Assert.NotEqual(a, satchel);
        }

        [Fact]
        public void Allocator_PlayerKind_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => EntityIdAllocator.Next(EntityIdKind.Player));
        }

        [Fact]
        public void LootSatchel_GetsSatchelKindId_ThatCannotCollideWithAPlayerId()
        {
            var satchel = new LootSatchel(new Vector3(0, 0, 0), ownerPlayerId: 1);

            Assert.Equal(EntityIdKind.Satchel, EntityIds.GetKind(satchel.Id));
            Assert.False(EntityIds.IsValidPlayerId(satchel.Id));
        }

        [Fact]
        public void RemovedAndReAddedPlayer_IsSentSpawnsForNearbyEntitiesAgain()
        {
            var map = new MapInstance(77);
            var client = new MockClientConnection { AccountId = 1, PlayerId = 5001 };
            var player = new Player(5001, "Returner", client) { Position = new Vector3(0, 0, 0), Health = 100, MaxHealth = 100 };
            map.AddPlayer(player);

            var npc = new NPC(EntityIdAllocator.Next(EntityIdKind.Npc), "Deer") { Position = new Vector3(3, 0, 0), SpawnPosition = new Vector3(3, 0, 0) };
            map.AddNPC(npc);

            map.Update();
            Assert.Contains(npc.Id, player.KnownEntities);
            Assert.Equal(1, CountSent(client, OpCode.EntitySpawn));

            // Respawn-style round trip: leave the map, come back (e.g. after the client reloaded its scene)
            map.RemovePlayer(player.Id, savePosition: false);
            map.AddPlayer(player);
            client.SentPackets.Clear();

            map.Update();

            Assert.Equal(1, CountSent(client, OpCode.EntitySpawn));
            Assert.Contains(npc.Id, player.KnownEntities);
        }

        [Fact]
        public void RemovePlayer_DoesNotTouchTheKnownSetUntilTheGameLoopRuns()
        {
            var map = new MapInstance(78);
            var client = new MockClientConnection { AccountId = 1, PlayerId = 5002 };
            var player = new Player(5002, "Leaver", client) { Position = new Vector3(0, 0, 0), Health = 100, MaxHealth = 100 };
            map.AddPlayer(player);
            player.KnownEntities.Add(123);

            map.RemovePlayer(player.Id, savePosition: false);

            // Handlers only flag the reset; the game thread owns KnownEntities
            Assert.True(player.AoiResetRequested);
            Assert.Contains(123, player.KnownEntities);
        }

        private static (Character character, string dbName) SeedCharacter(int id, float x)
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () =>
            {
                var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
                var ctx = new AppDbContext(options);
                ctx.Database.EnsureCreated();
                return ctx;
            };

            using var db = AppDbContext.Factory();
            var character = new Character { Id = id, AccountId = 1, Name = "Saved" + id, X = x };
            db.Characters.Add(character);
            db.SaveChanges();
            return (character, dbName);
        }

        [Fact]
        public void RemovePlayer_SavesThePositionFromRemovalTime_NotALaterOne()
        {
            SeedCharacter(5003, x: 0f);
            var map = new MapInstance(79);
            var client = new MockClientConnection { AccountId = 1, PlayerId = 5003 };
            var player = new Player(5003, "Saved5003", client) { Position = new Vector3(10, 0, 0) };
            map.AddPlayer(player);

            map.RemovePlayer(player.Id);
            player.Position = new Vector3(99, 0, 0); // e.g. teleported to the bind point right after

            Assert.True(WaitFor(() => { using var db = AppDbContext.Factory(); return db.Characters.Find(5003)!.X == 10f; }),
                "The position at removal time (10) should have been persisted.");
        }

        [Fact]
        public void RemovePlayer_WithSavePositionFalse_DoesNotWriteThePosition()
        {
            SeedCharacter(5004, x: 1f);
            var map = new MapInstance(80);
            var client = new MockClientConnection { AccountId = 1, PlayerId = 5004 };
            var player = new Player(5004, "Saved5004", client) { Position = new Vector3(50, 0, 0) };
            map.AddPlayer(player);

            map.RemovePlayer(player.Id, savePosition: false);
            Thread.Sleep(300);

            using var db = AppDbContext.Factory();
            Assert.Equal(1f, db.Characters.Find(5004)!.X);
        }

        [Theory]
        [InlineData(EntityIds.MaxSequence, true)]
        [InlineData(EntityIds.MaxSequence + 1, false)]
        public void SelectCharacter_OnlyAcceptsIdsInsideThePlayerRange(int characterId, bool accepted)
        {
            SeedCharacter(characterId, x: 0f);
            var client = new MockClientConnection { AccountId = 1 };

            using var write = new Packet(OpCode.CharacterSelectRequest);
            write.Write(characterId);
            using var read = new Packet(write.ToArray());
            CharacterHandler.HandleSelectRequest(client, read);
            GameLogic.Commands.DrainAll(); // world entry runs on the game thread

            try
            {
                Assert.Equal(accepted, client.PlayerId.HasValue);
                Assert.Equal(accepted, GameLogic.MapMgr.GetPlayer(characterId) != null);
            }
            finally
            {
                GameLogic.MapMgr.RemovePlayer(characterId, savePosition: false);
            }
        }
    }
}
