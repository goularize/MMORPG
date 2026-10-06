using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server;
using Server.Database;
using Server.Database.Models;
using Server.Persistence;
using Server.World;
using Server.World.Entities;
using Shared.Math;
using Xunit;

namespace Server.Tests
{
    // These tests use their own (asynchronous) service instances. Everything else in the suite runs the shared
    // instance inline, see TestAssemblyInit.
    public class PersistenceServiceTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private int _factoryCalls;
        private int _failFirstCalls;
        private bool _alwaysFail;

        private AppDbContext Db()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
            var ctx = new AppDbContext(options);
            ctx.Database.EnsureCreated();
            return ctx;
        }

        private AppDbContext FlakyDb()
        {
            int call = Interlocked.Increment(ref _factoryCalls);
            if (_alwaysFail || call <= _failFirstCalls) throw new InvalidOperationException("database unavailable");
            return Db();
        }

        private readonly System.Collections.Generic.List<PersistenceService> _services = new();

        private PersistenceService Create(double flushSeconds = 30, int workers = 2, int maxAttempts = 5)
        {
            var service = new PersistenceService(FlakyDb, flushSeconds, workers, maxAttempts);
            _services.Add(service);
            return service;
        }

        public void Dispose()
        {
            foreach (var s in _services) s.Stop(TimeSpan.FromSeconds(2));
        }

        private void SeedCharacter(int id, long gold = 0)
        {
            using var db = Db();
            db.Characters.Add(new Character { Id = id, AccountId = 1, Name = "Char" + id, Gold = gold });
            db.SaveChanges();
        }

        private static CharacterState State(int id, long gold, float x = 0f, int mapId = 1) =>
            new(id, 1, 0, 0, 100, 50, 10, 10, 10, 10, gold, 20, mapId, x, 0f, 0f, 1, 0f, 0f, 0f);

        private long GoldOf(int id) { using var db = Db(); return db.Characters.Find(id)!.Gold; }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(5);
            }
            return condition();
        }

        [Fact]
        public void QueuedCharacterState_IsWrittenBehind_NotImmediately_ThenAfterTheDelay()
        {
            SeedCharacter(1);
            var service = Create(flushSeconds: 0.4);

            service.QueueCharacter(State(1, gold: 500));

            Assert.Equal(0, GoldOf(1)); // the game thread did not wait for (or perform) the write
            Assert.True(WaitUntil(() => GoldOf(1) == 500), "the write-behind flush should land after the delay");
        }

        [Fact]
        public void ManyQueuedStates_AreCoalescedIntoOneWrite_WithTheLatestValues()
        {
            SeedCharacter(2);
            var service = Create(flushSeconds: 30);

            for (int gold = 1; gold <= 100; gold++) service.QueueCharacter(State(2, gold));
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            Assert.Equal(100, GoldOf(2));
            Assert.Equal(1, service.FlushedBatchCount);
        }

        [Fact]
        public void UrgentChanges_AreFlushedWithoutWaitingForTheWriteBehindDelay()
        {
            SeedCharacter(3);
            var service = Create(flushSeconds: 30);

            service.QueueCharacter(State(3, 77), urgent: true);

            Assert.True(WaitUntil(() => GoldOf(3) == 77, 3000));
        }

        [Fact]
        public void Expedite_MakesPendingChangesDurable()
        {
            SeedCharacter(4);
            var service = Create(flushSeconds: 30);
            service.QueueCharacter(State(4, 12));
            Assert.Equal(0, GoldOf(4));

            service.Expedite(4);

            Assert.True(WaitUntil(() => GoldOf(4) == 12, 3000));
        }

        [Fact]
        public void QueuedItem_IsASnapshot_LaterChangesToTheLiveItemAreNotSaved()
        {
            SeedCharacter(5);
            var service = Create();
            var item = new CharacterItem { CharacterId = 5, TemplateId = 1001, Quantity = 3 };

            service.QueueItem(item);
            item.Quantity = 999; // the game thread keeps changing the live object
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Equal(3, db.CharacterItems.Find(item.Id)!.Quantity);
        }

        [Fact]
        public void ItemUpsertThenDelete_ResultsInNoItem_AndUpsertOfAnExistingItemUpdatesIt()
        {
            SeedCharacter(6);
            var service = Create();
            var kept = new CharacterItem { CharacterId = 6, TemplateId = 1, Quantity = 1 };
            var removed = new CharacterItem { CharacterId = 6, TemplateId = 2, Quantity = 1 };

            service.QueueItem(kept);
            service.QueueItem(removed);
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            kept.Quantity = 7;
            service.QueueItem(kept);
            service.QueueItem(removed);
            service.QueueItemDelete(removed);
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Equal(7, db.CharacterItems.Find(kept.Id)!.Quantity);
            Assert.Null(db.CharacterItems.Find(removed.Id));
        }

        [Fact]
        public void CharacterItemsAndRecipes_AreWrittenInOneBatch_AndRecipesAreNotDuplicated()
        {
            SeedCharacter(7);
            var service = Create();
            service.QueueCharacter(State(7, 40));
            service.QueueItem(new CharacterItem { CharacterId = 7, TemplateId = 5, Quantity = 2 });
            service.QueueRecipe(7, 4001);
            service.QueueRecipe(7, 4001);

            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, service.FlushedBatchCount); // character + item + recipe went out together

            service.QueueRecipe(7, 4001); // learning it again later is idempotent
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Equal(1, db.CharacterLearnedRecipes.Count(r => r.CharacterId == 7));
            Assert.Equal(1, db.CharacterItems.Count(i => i.CharacterId == 7));
            Assert.Equal(40, db.Characters.Find(7)!.Gold);
        }

        [Fact]
        public void ConcurrentCharacters_EachEndWithTheirLastQueuedState_InOrder()
        {
            const int characters = 40;
            const int updates = 50;
            for (int id = 100; id < 100 + characters; id++) SeedCharacter(id);
            var service = Create(flushSeconds: 0.02, workers: 4);

            // The game thread queues interleaved updates for all characters while the workers flush in the background
            for (int gold = 1; gold <= updates; gold++)
            {
                for (int id = 100; id < 100 + characters; id++) service.QueueCharacter(State(id, gold));
            }
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(10)));

            for (int id = 100; id < 100 + characters; id++) Assert.Equal(updates, GoldOf(id));
        }

        [Fact]
        public void FailedFlush_IsRetried_AndTheDataIsNotLost()
        {
            SeedCharacter(8);
            Interlocked.Exchange(ref _factoryCalls, 0);
            _failFirstCalls = 2; // the first two flush attempts hit an unavailable database
            var service = Create(flushSeconds: 0.0);

            service.QueueCharacter(State(8, 321));

            Assert.True(service.Flush(8, TimeSpan.FromSeconds(10)));
            Assert.Equal(321, GoldOf(8));
            Assert.Equal(2, service.FailedFlushCount);
            Assert.Equal(0, service.DroppedBatchCount);
        }

        [Fact]
        public void PermanentlyFailingBatch_IsGivenUpOnAfterTheMaxAttempts_InsteadOfRetryingForever()
        {
            SeedCharacter(9);
            _alwaysFail = true;
            var service = Create(flushSeconds: 0.0, maxAttempts: 2);

            service.QueueCharacter(State(9, 5));

            Assert.True(service.Flush(9, TimeSpan.FromSeconds(10)));
            Assert.Equal(1, service.DroppedBatchCount);
            Assert.Equal(0, service.PendingCharacterCount);
        }

        [Fact]
        public void Flush_WaitsForPendingChanges_AndReturnsImmediatelyWhenNothingIsPending()
        {
            SeedCharacter(10);
            var service = Create(flushSeconds: 30);

            Assert.True(service.Flush(10, TimeSpan.FromMilliseconds(50))); // nothing queued
            service.QueueCharacter(State(10, 9));

            Assert.True(service.Flush(10, TimeSpan.FromSeconds(5)));
            Assert.Equal(9, GoldOf(10));
        }

        [Fact]
        public void Stop_FlushesEverything_AndLaterWritesAreAppliedSynchronously()
        {
            SeedCharacter(11);
            SeedCharacter(12);
            var service = Create(flushSeconds: 30);
            service.QueueCharacter(State(11, 11));
            service.QueueCharacter(State(12, 12));

            Assert.True(service.Stop(TimeSpan.FromSeconds(5)));
            Assert.Equal(11, GoldOf(11));
            Assert.Equal(12, GoldOf(12));

            service.QueueCharacter(State(11, 99)); // e.g. a late disconnect during shutdown
            Assert.Equal(99, GoldOf(11));
        }

        [Fact]
        public void ChatLogs_AreWrittenOnTheirOwnLane_AndDrainedOnStop()
        {
            var service = Create();
            for (int i = 0; i < 50; i++)
            {
                service.QueueChatLog(new ChatLog { Channel = Shared.Network.ChatChannel.Global, SenderName = "A", Message = "m" + i });
            }

            Assert.True(service.Stop(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Equal(50, db.ChatLogs.Count());
        }

        [Fact]
        public void InlineMode_AppliesWritesSynchronously()
        {
            SeedCharacter(13);
            var service = Create();
            service.Inline = true;

            service.QueueCharacter(State(13, 4));

            Assert.Equal(4, GoldOf(13));
        }
    }

    public class PlayerPersistenceTests
    {
        private static AppDbContext Db(string name)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
            var ctx = new AppDbContext(options);
            ctx.Database.EnsureCreated();
            return ctx;
        }

        [Fact]
        public void CreatePersistenceState_CapturesTheLiveStateIncludingMapPositionAndBind()
        {
            var player = new Player(21, "Snap", new TestClient(21))
            {
                Level = 4, Exp = 250, Gold = 17, Health = 33, Mana = 12, MapId = 2,
                Position = new Vector3(1, 2, 3), BindMapId = 1, BindPosition = new Vector3(9, 8, 7)
            };

            var state = player.CreatePersistenceState();

            Assert.Equal(21, state.CharacterId);
            Assert.Equal((4, 250L, 17L, 33, 12), (state.Level, state.Exp, state.Gold, state.Health, state.Mana));
            Assert.Equal((2, 1f, 2f, 3f), (state.MapId, state.X, state.Y, state.Z));
            Assert.Equal((1, 9f, 8f, 7f), (state.BindMapId, state.BindX, state.BindY, state.BindZ));
        }

        [Fact]
        public void SaveAllPlayers_PersistsEveryOnlinePlayer_ForShutdown()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => Db(dbName);
            using (var db = AppDbContext.Factory())
            {
                db.Characters.Add(new Character { Id = 9501, AccountId = 1, Name = "Online1" });
                db.Characters.Add(new Character { Id = 9502, AccountId = 1, Name = "Online2" });
                db.SaveChanges();
            }
            GameLogic.MapMgr.AddPlayer(new Player(9501, "Online1", new TestClient(1)) { Gold = 111, MapId = 1 });
            GameLogic.MapMgr.AddPlayer(new Player(9502, "Online2", new TestClient(2)) { Gold = 222, MapId = 2 });

            GameLogic.MapMgr.SaveAllPlayers();

            using var check = AppDbContext.Factory();
            Assert.Equal(111, check.Characters.Find(9501)!.Gold);
            Assert.Equal(222, check.Characters.Find(9502)!.Gold);
            GameLogic.MapMgr.RemovePlayer(9501, saveState: false);
            GameLogic.MapMgr.RemovePlayer(9502, saveState: false);
        }

        [Fact]
        public void RemovePlayer_PersistsTheFinalState_IncludingVitalsAndMap()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => Db(dbName);
            using (var db = AppDbContext.Factory())
            {
                db.Characters.Add(new Character { Id = 9503, AccountId = 1, Name = "Leaver", Health = 100 });
                db.SaveChanges();
            }
            var player = new Player(9503, "Leaver", new TestClient(3)) { Health = 12, Mana = 5, MapId = 1, Position = new Vector3(4, 0, 0) };
            GameLogic.MapMgr.AddPlayer(player);

            GameLogic.MapMgr.RemovePlayer(9503);

            using var check = AppDbContext.Factory();
            var row = check.Characters.Find(9503)!;
            Assert.Equal(12, row.Health); // previously only saved together with progression
            Assert.Equal(4f, row.X);
        }

        [Fact]
        public void Autosave_QueuesAPeriodicSnapshot_WithoutAnyExplicitEvent()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => Db(dbName);
            using (var db = AppDbContext.Factory())
            {
                db.Characters.Add(new Character { Id = 9504, AccountId = 1, Name = "Auto", Gold = 0 });
                db.SaveChanges();
            }
            Environment.SetEnvironmentVariable("AUTOSAVE_SECONDS", "1");
            ServerConfig.Initialize();
            try
            {
                var player = new Player(9504, "Auto", new TestClient(4)) { Gold = 5 };
                player.Update(); // schedules the first (staggered) autosave within the next second
                Thread.Sleep(1200);

                player.Update(); // due now

                using var check = AppDbContext.Factory();
                Assert.Equal(5, check.Characters.Find(9504)!.Gold);
            }
            finally
            {
                Environment.SetEnvironmentVariable("AUTOSAVE_SECONDS", null);
                ServerConfig.Initialize();
            }
        }
    }
}
