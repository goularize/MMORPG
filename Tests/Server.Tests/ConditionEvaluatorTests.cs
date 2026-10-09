using System.Collections.Generic;
using Server.Database.Models;
using Server.World;
using Shared.Data;
using Shared.Enums;
using Xunit;

namespace Server.Tests
{
    public class ConditionEvaluatorTests : NpcWorldTestBase
    {
        private (RecordingClient client, Server.World.Entities.Player player) NewPlayer(int level = 1) => AddPlayer(level: level);

        [Fact]
        public void NullOrEmptyConditions_AlwaysHold()
        {
            var (_, player) = NewPlayer();

            Assert.True(ConditionEvaluator.Evaluate(player, (IReadOnlyList<Condition>?)null));
            Assert.True(ConditionEvaluator.Evaluate(player, new List<Condition>()));
        }

        [Fact]
        public void KillCount_RequiresAtLeastMinKillsOfThatTemplate()
        {
            var (_, player) = NewPlayer();
            var condition = new Condition { Type = ConditionType.KillCount, Id = 101, Min = 3 };

            player.Progress.AddKill(101);
            player.Progress.AddKill(101);
            player.Progress.AddKill(100); // another template does not count
            Assert.False(ConditionEvaluator.Evaluate(player, condition));

            player.Progress.AddKill(101);
            Assert.True(ConditionEvaluator.Evaluate(player, condition));
        }

        [Fact]
        public void QuestState_MatchesTheLogEntry_AndAvailableWhenAbsent()
        {
            var (_, player) = NewPlayer();
            var available = new Condition { Type = ConditionType.QuestState, Id = 5, State = QuestState.Available };
            var rewarded = new Condition { Type = ConditionType.QuestState, Id = 5, State = QuestState.Rewarded };

            Assert.True(ConditionEvaluator.Evaluate(player, available));
            Assert.False(ConditionEvaluator.Evaluate(player, rewarded));

            player.Progress.SetQuest(5, QuestState.Active, new[] { 1 });
            Assert.False(ConditionEvaluator.Evaluate(player, available));
            Assert.False(ConditionEvaluator.Evaluate(player, rewarded));

            player.Progress.SetQuest(5, QuestState.Rewarded, new[] { 1 });
            Assert.True(ConditionEvaluator.Evaluate(player, rewarded));
        }

        [Fact]
        public void Flag_HoldsOnlyWhileTheFlagIsSet()
        {
            var (_, player) = NewPlayer();
            var condition = new Condition { Type = ConditionType.Flag, Name = "met_augustos" };

            Assert.False(ConditionEvaluator.Evaluate(player, condition));
            player.Progress.SetFlag("met_augustos");
            Assert.True(ConditionEvaluator.Evaluate(player, condition));
            player.Progress.ClearFlag("met_augustos");
            Assert.False(ConditionEvaluator.Evaluate(player, condition));
        }

        [Fact]
        public void Flag_WithoutAName_NeverHolds()
        {
            var (_, player) = NewPlayer();

            Assert.False(ConditionEvaluator.Evaluate(player, new Condition { Type = ConditionType.Flag, Name = null }));
        }

        [Theory]
        [InlineData(4, 5, 0, false)]  // below Min
        [InlineData(5, 5, 0, true)]   // at Min, no upper bound
        [InlineData(60, 5, 0, true)]  // Max 0 means no upper bound
        [InlineData(10, 5, 10, true)] // at Max
        [InlineData(11, 5, 10, false)] // above Max
        public void Level_RespectsMinAndOptionalMax(int level, int min, int max, bool expected)
        {
            var (_, player) = NewPlayer(level);

            Assert.Equal(expected, ConditionEvaluator.Evaluate(player, new Condition { Type = ConditionType.Level, Min = min, Max = max }));
        }

        [Fact]
        public void HasItem_CountsTheBackpackAndEquippedItems()
        {
            var (_, player) = NewPlayer();
            var condition = new Condition { Type = ConditionType.HasItem, Id = 3001, Min = 5 };

            Give(player, 3001, 3);
            Assert.False(ConditionEvaluator.Evaluate(player, condition));

            player.EquippedItems[EquipmentSlot.MainHand] = new CharacterItem { TemplateId = 3001, Quantity = 2 };
            Assert.True(ConditionEvaluator.Evaluate(player, condition));
        }

        [Fact]
        public void Negate_InvertsTheResult()
        {
            var (_, player) = NewPlayer();
            var condition = new Condition { Type = ConditionType.Flag, Name = "seen", Negate = true };

            Assert.True(ConditionEvaluator.Evaluate(player, condition));
            player.Progress.SetFlag("seen");
            Assert.False(ConditionEvaluator.Evaluate(player, condition));
        }

        [Fact]
        public void AListIsAnAnd_EveryConditionMustHold()
        {
            var (_, player) = NewPlayer(level: 10);
            var conditions = new List<Condition>
            {
                new() { Type = ConditionType.Level, Min = 5 },
                new() { Type = ConditionType.KillCount, Id = 101, Min = 1 }
            };

            Assert.False(ConditionEvaluator.Evaluate(player, conditions));
            player.Progress.AddKill(101);
            Assert.True(ConditionEvaluator.Evaluate(player, conditions));
        }

        [Fact]
        public void UnknownType_FailsClosed()
        {
            var (_, player) = NewPlayer();

            Assert.False(ConditionEvaluator.Evaluate(player, new Condition { Type = ConditionType.Unknown }));
            Assert.True(ConditionEvaluator.Evaluate(player, new Condition { Type = ConditionType.Unknown, Negate = true }));
        }
    }
}
