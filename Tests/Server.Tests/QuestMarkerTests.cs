using System.Linq;
using Server.Data;
using Server.Data.Models;
using Server.Quests;
using Shared.Data;
using Shared.Enums;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    /// <summary>The "!" and "?" over NPCs: computed per player and sent only when they change.</summary>
    public class QuestMarkerTests : NpcWorldTestBase
    {
        private const int BlacksmithTemplate = 200;   // gives and takes quest 1 (The Slime Menace)
        private const int OtherGiverTemplate = 9101;

        /// <summary>A quest template 9101 gives and takes, with the given requirements.</summary>
        private QuestTemplate AddGatedQuest(int id, int minLevel, params Condition[] prerequisites)
        {
            NewNpcTemplate("quest_giver", OtherGiverTemplate);
            var quest = new QuestTemplate
            {
                Id = id, Name = "Gated " + id, MinLevel = minLevel,
                GiverNpcTemplateId = OtherGiverTemplate, TurnInNpcTemplateId = OtherGiverTemplate,
                Prerequisites = prerequisites.ToList(),
                Objectives = { new QuestObjective { Type = QuestObjectiveType.Kill, Id = 101, Count = 1 } },
                Text = { Offer = "Offer", Progress = "Progress", Complete = "Complete" }
            };
            DataManager.Quests[id] = quest;
            return quest;
        }

        [Fact]
        public void AnNpcThatOffersAQuest_ShowsAnExclamationMark_WhenItComesIntoView()
        {
            var (client, _) = AddPlayer();
            var npc = AddNpc(BlacksmithTemplate, "Augustos");

            Map.Update();

            Assert.Equal(EntityMarker.QuestAvailable, client.MarkerOf(npc.Id));
        }

        [Fact]
        public void TheMarker_FollowsAcceptCompleteAndTurnIn()
        {
            var (client, player) = AddPlayer();
            var npc = AddNpc(BlacksmithTemplate, "Augustos");
            Map.Update();
            Assert.Equal(EntityMarker.QuestAvailable, client.MarkerOf(npc.Id));

            QuestService.Accept(player, 1, BlacksmithTemplate);
            Map.Update();
            Assert.Equal(EntityMarker.None, client.MarkerOf(npc.Id));

            KillSlimes(player, 10);
            Map.Update();
            Assert.Equal(EntityMarker.QuestReady, client.MarkerOf(npc.Id));

            Assert.Equal(QuestResult.Ok, QuestService.TurnIn(player, 1, BlacksmithTemplate));
            Map.Update();
            Assert.Equal(EntityMarker.None, client.MarkerOf(npc.Id));
        }

        [Fact]
        public void AMarkerIsOnlySentWhenItChanges()
        {
            var (client, _) = AddPlayer();
            AddNpc(BlacksmithTemplate, "Augustos");

            Map.Update();
            int sent = client.Count(OpCode.EntityMarker);
            Map.Update();
            Map.Update();

            Assert.Equal(1, sent);
            Assert.Equal(sent, client.Count(OpCode.EntityMarker));
        }

        [Fact]
        public void AnNpcWithNothingToOffer_SendsNoMarker()
        {
            var (client, player) = AddPlayer();
            player.Progress.SetQuest(1, QuestState.Rewarded, new[] { 10 });
            AddNpc(BlacksmithTemplate, "Augustos");

            Map.Update();

            Assert.Equal(0, client.Count(OpCode.EntityMarker));
        }

        [Fact]
        public void ALevelRequirement_ShowsTheMarkerWhenThePlayerReachesIt()
        {
            AddGatedQuest(71, minLevel: 3);
            var (client, player) = AddPlayer();
            var npc = AddNpc(OtherGiverTemplate, "Elder");

            Map.Update();
            Assert.Equal(EntityMarker.None, client.MarkerOf(npc.Id));

            player.AddExp(1_000_000);
            Map.Update();
            Assert.Equal(EntityMarker.QuestAvailable, client.MarkerOf(npc.Id));
        }

        [Fact]
        public void AFlagRequirement_ShowsTheMarkerWhenTheFlagIsSet()
        {
            AddGatedQuest(72, minLevel: 1, new Condition { Type = ConditionType.Flag, Name = "met_the_elder" });
            var (client, player) = AddPlayer();
            var npc = AddNpc(OtherGiverTemplate, "Elder");

            Map.Update();
            Assert.Equal(EntityMarker.None, client.MarkerOf(npc.Id));

            player.Progress.SetFlag("met_the_elder");
            Map.Update();
            Assert.Equal(EntityMarker.QuestAvailable, client.MarkerOf(npc.Id));
        }

        [Fact]
        public void MarkersArePerPlayer()
        {
            var (clientA, playerA) = AddPlayer("Alice");
            var (clientB, _) = AddPlayer("Bob");
            var npc = AddNpc(BlacksmithTemplate, "Augustos");
            Map.Update();

            QuestService.Accept(playerA, 1, BlacksmithTemplate);
            Map.Update();

            Assert.Equal(EntityMarker.None, clientA.MarkerOf(npc.Id));
            Assert.Equal(EntityMarker.QuestAvailable, clientB.MarkerOf(npc.Id));
        }

        [Fact]
        public void AnNpcOutOfView_IsNotMarked_UntilItComesIntoView()
        {
            var (client, player) = AddPlayer();
            var npc = AddNpc(BlacksmithTemplate, "Augustos", x: 500f);

            Map.Update();
            Assert.Equal(0, client.Count(OpCode.EntityMarker));

            player.Position = new Shared.Math.Vector3(490f, 0, 0);
            Map.Update();
            Assert.Equal(EntityMarker.QuestAvailable, client.MarkerOf(npc.Id));
        }

        [Fact]
        public void LeavingAndReturningToView_SendsTheMarkerAgain()
        {
            var (client, player) = AddPlayer();
            var npc = AddNpc(BlacksmithTemplate, "Augustos");
            Map.Update();
            Assert.Equal(1, client.Count(OpCode.EntityMarker));

            player.Position = new Shared.Math.Vector3(500f, 0, 0);
            Map.Update();
            player.Position = new Shared.Math.Vector3(1f, 0, 0);
            Map.Update();

            Assert.Equal(2, client.Count(OpCode.EntityMarker));
            Assert.Equal(EntityMarker.QuestAvailable, client.MarkerOf(npc.Id));
        }

        [Fact]
        public void AnEnemy_NeverHasAMarker()
        {
            var (client, _) = AddPlayer();
            AddNpc(101, "Slime");

            Map.Update();

            Assert.Equal(0, client.Count(OpCode.EntityMarker));
        }
    }
}
