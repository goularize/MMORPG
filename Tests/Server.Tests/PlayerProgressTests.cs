using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Server.Persistence;
using Server.World;
using Server.World.Entities;
using Shared.Constants;
using Shared.Enums;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class PlayerProgressTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly List<PersistenceService> _services = new();
        private int _factoryCalls;
        private int _failFirstCalls;

        private AppDbContext Db()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
            var ctx = new AppDbContext(options);
            ctx.Database.EnsureCreated();
            return ctx;
        }

        private AppDbContext FlakyDb()
        {
            if (Interlocked.Increment(ref _factoryCalls) <= _failFirstCalls) throw new InvalidOperationException("database unavailable");
            return Db();
        }

        private PersistenceService CreateService(double flushSeconds = 30)
        {
            var service = new PersistenceService(FlakyDb, flushSeconds, workers: 2, maxAttempts: 5);
            _services.Add(service);
            return service;
        }

        public void Dispose()
        {
            foreach (var s in _services) s.Stop(TimeSpan.FromSeconds(2));
        }

        private void SeedCharacter(int id)
        {
            using var db = Db();
            db.Characters.Add(new Character { Id = id, AccountId = 1, Name = "Char" + id });
            db.SaveChanges();
        }

        // ---- In-memory behaviour ----

        [Fact]
        public void Flags_AreSetAndClearedIdempotently()
        {
            var progress = new PlayerProgress(1, null);

            Assert.False(progress.HasFlag("met_augustos"));
            Assert.True(progress.SetFlag("met_augustos"));
            Assert.False(progress.SetFlag("met_augustos")); // already set
            Assert.True(progress.HasFlag("met_augustos"));

            Assert.True(progress.ClearFlag("met_augustos"));
            Assert.False(progress.ClearFlag("met_augustos"));
            Assert.False(progress.HasFlag("met_augustos"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("has space")]
        [InlineData("semi;colon")]
        public void Flags_WithInvalidNames_AreRejected(string flag)
        {
            var progress = new PlayerProgress(1, null);

            Assert.Throws<ArgumentException>(() => progress.SetFlag(flag));
            Assert.Throws<ArgumentException>(() => progress.ClearFlag(flag));
            Assert.Empty(progress.Flags);
        }

        [Fact]
        public void Flags_AreCappedAtTheSharedMaximumLength()
        {
            var progress = new PlayerProgress(1, null);

            Assert.True(progress.SetFlag(new string('a', ProgressRules.FlagMaxLength)));
            Assert.Throws<ArgumentException>(() => progress.SetFlag(new string('a', ProgressRules.FlagMaxLength + 1)));
            Assert.Throws<ArgumentException>(() => progress.SetFlag(null!));
        }

        [Fact]
        public void KillCounts_AccumulatePerTemplate()
        {
            var progress = new PlayerProgress(1, null);

            Assert.Equal(0, progress.GetKillCount(101));
            Assert.Equal(1, progress.AddKill(101));
            Assert.Equal(2, progress.AddKill(101));
            Assert.Equal(1, progress.AddKill(100));

            Assert.Equal(2, progress.GetKillCount(101));
            Assert.Equal(1, progress.GetKillCount(100));
        }

        [Fact]
        public void QuestLog_StartsAvailable_AndTracksStateAndObjectives()
        {
            var progress = new PlayerProgress(1, null);
            Assert.Equal(QuestState.Available, progress.GetQuestState(5));
            Assert.Null(progress.GetQuest(5));

            progress.SetQuest(5, QuestState.Active, new[] { 3, 0 });
            Assert.Equal(QuestState.Active, progress.GetQuestState(5));
            Assert.Equal(new[] { 3, 0 }, progress.GetQuest(5)!.Objectives);

            progress.SetQuest(5, QuestState.Rewarded, new[] { 10, 1 });
            Assert.Equal(QuestState.Rewarded, progress.GetQuestState(5));
        }

        [Fact]
        public void QuestLog_SettingAvailableOrRemoving_ClearsTheEntry()
        {
            var progress = new PlayerProgress(1, null);
            progress.SetQuest(5, QuestState.Active);
            progress.SetQuest(6, QuestState.Active);

            progress.SetQuest(5, QuestState.Available);
            Assert.True(progress.RemoveQuest(6));
            Assert.False(progress.RemoveQuest(6));

            Assert.Empty(progress.Quests);
        }

        [Fact]
        public void QuestLog_CopiesTheObjectiveArray()
        {
            var progress = new PlayerProgress(1, null);
            var counters = new[] { 1, 2 };

            progress.SetQuest(5, QuestState.Active, counters);
            counters[0] = 99;

            Assert.Equal(new[] { 1, 2 }, progress.GetQuest(5)!.Objectives);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"a\":1}")]
        public void ParseObjectives_ToleratesMissingOrMalformedJson(string? json)
        {
            Assert.Empty(PlayerProgress.ParseObjectives(json));
        }

        [Fact]
        public void Load_FillsTheProgress_WithoutQueueingAnyWrite()
        {
            var service = CreateService();
            var progress = new PlayerProgress(1, service);

            progress.Load(
                new[] { "a", "b" },
                new Dictionary<int, int> { [101] = 7 },
                new[] { (5, QuestState.ReadyToTurnIn, "[10]"), (6, QuestState.Available, "[]") });

            Assert.True(progress.HasFlag("a"));
            Assert.Equal(7, progress.GetKillCount(101));
            Assert.Equal(QuestState.ReadyToTurnIn, progress.GetQuestState(5));
            Assert.Equal(new[] { 10 }, progress.GetQuest(5)!.Objectives);
            Assert.Null(progress.GetQuest(6)); // an "Available" row is never a log entry
            Assert.Equal(0, service.PendingCharacterCount);
        }

        // ---- Write-behind persistence ----

        [Fact]
        public void Changes_ArePersistedByTheWriteBehindQueue()
        {
            SeedCharacter(1);
            var service = CreateService();
            var progress = new PlayerProgress(1, service);

            progress.SetFlag("heard_slime_history");
            progress.AddKill(101);
            progress.AddKill(101);

            using (var db = Db()) Assert.Empty(db.CharacterFlags); // nothing written inline by the game thread

            progress.SetQuest(5, QuestState.Active, new[] { 2 }); // durable: also flushes what was pending
            Assert.True(service.Flush(1, TimeSpan.FromSeconds(5)));
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var check = Db();
            Assert.Equal("heard_slime_history", Assert.Single(check.CharacterFlags.Where(f => f.CharacterId == 1)).Flag);
            Assert.Equal(2, check.CharacterKillCounts.Find(1, 101)!.Count);
            var quest = check.CharacterQuests.Find(1, 5)!;
            Assert.Equal(QuestState.Active, quest.State);
            Assert.Equal("[2]", quest.ProgressJson);
        }

        [Fact]
        public void ManyKills_AreCoalescedIntoOneWrite_WithTheLatestTotal()
        {
            SeedCharacter(2);
            var service = CreateService();
            var progress = new PlayerProgress(2, service);

            for (int i = 0; i < 50; i++) progress.AddKill(101);
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Equal(1, service.FlushedBatchCount);
            Assert.Equal(50, db.CharacterKillCounts.Find(2, 101)!.Count);
            Assert.Equal(1, db.CharacterKillCounts.Count(k => k.CharacterId == 2));
        }

        [Fact]
        public void ClearingAFlag_AfterItWasStored_RemovesTheRow()
        {
            SeedCharacter(3);
            var service = CreateService();
            var progress = new PlayerProgress(3, service);
            progress.SetFlag("temp");
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            progress.ClearFlag("temp");
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Empty(db.CharacterFlags.Where(f => f.CharacterId == 3));
        }

        [Fact]
        public void SettingThenClearingAFlagInOneWindow_LeavesNoRow()
        {
            SeedCharacter(4);
            var service = CreateService();
            var progress = new PlayerProgress(4, service);

            progress.SetFlag("blink");
            progress.ClearFlag("blink");
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Empty(db.CharacterFlags.Where(f => f.CharacterId == 4));
        }

        [Fact]
        public void AbandoningAQuest_DeletesItsRow()
        {
            SeedCharacter(5);
            var service = CreateService();
            var progress = new PlayerProgress(5, service);
            progress.SetQuest(7, QuestState.Active, new[] { 1 });
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            progress.RemoveQuest(7);
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Null(db.CharacterQuests.Find(5, 7));
        }

        [Fact]
        public void QuestChanges_AreDurable_AndDoNotWaitForTheWriteBehindDelay()
        {
            SeedCharacter(6);
            var service = CreateService(flushSeconds: 30);
            var progress = new PlayerProgress(6, service);

            progress.SetQuest(8, QuestState.Rewarded, new[] { 10 });

            Assert.True(service.Flush(6, TimeSpan.FromSeconds(5)));
            using var db = Db();
            Assert.Equal(QuestState.Rewarded, db.CharacterQuests.Find(6, 8)!.State);
        }

        [Fact]
        public void FailedFlush_KeepsTheProgress_AndRetries()
        {
            SeedCharacter(7);
            Interlocked.Exchange(ref _factoryCalls, 0);
            _failFirstCalls = 2;
            var service = CreateService(flushSeconds: 0.0);
            var progress = new PlayerProgress(7, service);

            progress.SetFlag("kept");
            progress.AddKill(101);
            progress.SetQuest(9, QuestState.Active, new[] { 4 });

            Assert.True(service.Flush(7, TimeSpan.FromSeconds(10)));

            using var db = Db();
            Assert.Equal(2, service.FailedFlushCount);
            Assert.NotNull(db.CharacterFlags.Find(7, "kept"));
            Assert.Equal(1, db.CharacterKillCounts.Find(7, 101)!.Count);
            Assert.Equal(new[] { 4 }, PlayerProgress.ParseObjectives(db.CharacterQuests.Find(7, 9)!.ProgressJson));
        }

        [Fact]
        public void ProgressAndCharacterState_ReachTheDatabaseInOneBatch()
        {
            SeedCharacter(8);
            var service = CreateService();
            var progress = new PlayerProgress(8, service);

            service.QueueCharacter(new CharacterState(8, 5, 0, 0, 100, 50, 10, 10, 10, 10, 250, 20, 1, 0f, 0f, 0f, 1, 0f, 0f, 0f));
            progress.AddKill(101);
            Assert.True(service.FlushAll(TimeSpan.FromSeconds(5)));

            using var db = Db();
            Assert.Equal(1, service.FlushedBatchCount);
            Assert.Equal(250, db.Characters.Find(8)!.Gold);
            Assert.Equal(1, db.CharacterKillCounts.Find(8, 101)!.Count);
        }

        // ---- Hydration at character select ----

        [Fact]
        public void SelectingACharacter_LoadsItsStoredProgress()
        {
            const int characterId = 5601;
            AppDbContext.Factory = Db;
            using (var db = Db())
            {
                db.Characters.Add(new Character { Id = characterId, AccountId = 1, Name = "Hydrated" });
                db.CharacterFlags.Add(new CharacterFlag { CharacterId = characterId, Flag = "met_augustos" });
                db.CharacterKillCounts.Add(new CharacterKillCount { CharacterId = characterId, NpcTemplateId = 101, Count = 12 });
                db.CharacterQuests.Add(new CharacterQuest { CharacterId = characterId, QuestId = 5, State = QuestState.ReadyToTurnIn, ProgressJson = "[10]" });
                // Rows of another character must not leak into this one
                db.Characters.Add(new Character { Id = characterId + 1, AccountId = 1, Name = "Other" });
                db.CharacterFlags.Add(new CharacterFlag { CharacterId = characterId + 1, Flag = "not_mine" });
                db.SaveChanges();
            }

            var client = new MockClientConnection { AccountId = 1 };
            using var write = new Packet(OpCode.CharacterSelectRequest);
            write.Write(characterId);
            using var read = new Packet(write.ToArray());

            try
            {
                CharacterHandler.HandleSelectRequest(client, read);
                GameLogic.Commands.DrainAll();

                var player = GameLogic.MapMgr.GetPlayer(characterId);
                Assert.NotNull(player);
                Assert.True(player!.Progress.HasFlag("met_augustos"));
                Assert.False(player.Progress.HasFlag("not_mine"));
                Assert.Equal(12, player.Progress.GetKillCount(101));
                Assert.Equal(QuestState.ReadyToTurnIn, player.Progress.GetQuestState(5));
                Assert.Equal(new[] { 10 }, player.Progress.GetQuest(5)!.Objectives);
            }
            finally
            {
                GameLogic.MapMgr.RemovePlayer(characterId, saveState: false);
            }
        }

        [Fact]
        public void DeletingACharacter_RemovesItsProgressRows_WhenTheDatabaseCascades()
        {
            // The relational cascade is enforced by PostgreSQL (see the migration); here the model must declare it.
            using var db = Db();
            var model = db.Model;
            foreach (var type in new[] { typeof(CharacterFlag), typeof(CharacterKillCount), typeof(CharacterQuest) })
            {
                var fk = Assert.Single(model.FindEntityType(type)!.GetForeignKeys());
                Assert.Equal(Microsoft.EntityFrameworkCore.DeleteBehavior.Cascade, fk.DeleteBehavior);
            }
        }
    }
}
