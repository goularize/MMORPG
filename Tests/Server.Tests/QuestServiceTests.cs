using System.Linq;
using Server.Data;
using Server.Data.Models;
using Server.Dialogue;
using Server.Handlers;
using Server.Quests;
using Server.World;
using Server.World.Entities;
using Shared.Constants;
using Shared.Data;
using Shared.Enums;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class QuestServiceTests : NpcWorldTestBase
    {
        private const int BlacksmithTemplate = 200;   // gives and takes quest 1 (The Slime Menace)
        private const int QuestNpcTemplate = 9001;

        private static QuestObjective Kill(int npc, int count) => new() { Type = QuestObjectiveType.Kill, Id = npc, Count = count };
        private static QuestObjective Collect(int item, int count) => new() { Type = QuestObjectiveType.Collect, Id = item, Count = count };
        private static QuestObjective Talk(int npc, int count = 1) => new() { Type = QuestObjectiveType.Talk, Id = npc, Count = count };

        /// <summary>Adds a quest that template 9001 gives and takes, plus a dialogue so players can talk to that NPC.</summary>
        private QuestTemplate AddQuest(int id, params QuestObjective[] objectives)
        {
            DataManager.Dialogues["quest_giver"] = new DialogueTemplate
            {
                Id = "quest_giver",
                Greetings = { new DialogueGreeting { Text = "Hello.", Options = { new DialogueOption { Label = "Bye", Action = InteractAction.Close } } } }
            };
            NewNpcTemplate("quest_giver", QuestNpcTemplate);

            var quest = new QuestTemplate
            {
                Id = id, Name = "Quest " + id, GiverNpcTemplateId = QuestNpcTemplate, TurnInNpcTemplateId = QuestNpcTemplate,
                Objectives = objectives.ToList(),
                Rewards = { Exp = 0, Gold = 0 },
                Text = { Offer = "Offer", Progress = "Progress", Complete = "Complete" }
            };
            DataManager.Quests[id] = quest;
            return quest;
        }

        private static QuestProgress Entry(Player player, int questId) => player.Progress.GetQuest(questId)!;

        // ---- Accepting ----

        [Fact]
        public void Accept_PutsTheQuestInTheLog_AndTellsTheClient()
        {
            var (client, player) = AddPlayer();

            Assert.Equal(QuestResult.Ok, QuestService.Accept(player, 1, BlacksmithTemplate));

            Assert.Equal(QuestState.Active, Entry(player, 1).State);
            Assert.Equal(new[] { 0 }, Entry(player, 1).Objectives);
            Assert.Equal((QuestState.Active, new[] { 0 }), client.LastQuestUpdate(1));
        }

        [Fact]
        public void Accept_IsRefused_ForTheWrongNpc_UnknownQuests_AndQuestsAlreadyInTheLog()
        {
            var (_, player) = AddPlayer();

            Assert.Equal(QuestResult.WrongNpc, QuestService.Accept(player, 1, 201));
            Assert.Equal(QuestResult.UnknownQuest, QuestService.Accept(player, 12345, BlacksmithTemplate));

            Assert.Equal(QuestResult.Ok, QuestService.Accept(player, 1, BlacksmithTemplate));
            Assert.Equal(QuestResult.NotAvailable, QuestService.Accept(player, 1, BlacksmithTemplate));
        }

        [Fact]
        public void Accept_RespectsMinLevelAndPrerequisites()
        {
            var first = AddQuest(50, Kill(101, 1));
            var gated = AddQuest(51, Kill(101, 1));
            gated.MinLevel = 5;
            gated.Prerequisites.Add(new Condition { Type = ConditionType.QuestState, Id = 50, State = QuestState.Rewarded });
            var (_, player) = AddPlayer(level: 1);

            Assert.Equal(QuestResult.NotAvailable, QuestService.Accept(player, 51, QuestNpcTemplate)); // level and prerequisite

            player.Level = 5;
            Assert.Equal(QuestResult.NotAvailable, QuestService.Accept(player, 51, QuestNpcTemplate)); // prerequisite only

            player.Progress.SetQuest(50, QuestState.Rewarded, new[] { 1 });
            Assert.Equal(QuestResult.Ok, QuestService.Accept(player, 51, QuestNpcTemplate));
        }

        [Fact]
        public void Accept_IsRefused_WhenTheQuestLogIsFull()
        {
            for (int i = 0; i < ProgressRules.MaxActiveQuests; i++) AddQuest(100 + i, Kill(101, 99));
            AddQuest(200, Kill(101, 1));
            var (_, player) = AddPlayer();

            for (int i = 0; i < ProgressRules.MaxActiveQuests; i++)
                Assert.Equal(QuestResult.Ok, QuestService.Accept(player, 100 + i, QuestNpcTemplate));

            Assert.Equal(QuestResult.LogFull, QuestService.Accept(player, 200, QuestNpcTemplate));
        }

        // ---- Objectives ----

        [Fact]
        public void KillObjectives_CountOnlyAfterAccepting_AndOnlyForTheKiller()
        {
            var (client, player) = AddPlayer();
            var (_, other) = AddPlayer("Other");
            KillSlimes(player, 5); // before accepting: not part of the quest
            QuestService.Accept(player, 1, BlacksmithTemplate);
            Assert.Equal(new[] { 0 }, Entry(player, 1).Objectives);

            KillSlimes(player, 4);
            KillSlimes(other, 3);

            Assert.Equal(QuestState.Active, Entry(player, 1).State);
            Assert.Equal(new[] { 4 }, Entry(player, 1).Objectives);
            Assert.Equal((QuestState.Active, new[] { 4 }), client.LastQuestUpdate(1));
            Assert.Equal(9, player.Progress.GetKillCount(101)); // kill counts keep all kills
            Assert.Null(other.Progress.GetQuest(1));
        }

        [Fact]
        public void CompletingTheObjectives_MakesTheQuestReadyToTurnIn_AndCapsTheCounter()
        {
            var (client, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);

            KillSlimes(player, 12);

            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 1).State);
            Assert.Equal(new[] { 10 }, Entry(player, 1).Objectives);
            Assert.Equal((QuestState.ReadyToTurnIn, new[] { 10 }), client.LastQuestUpdate(1));
        }

        [Fact]
        public void TalkObjectives_CountEachConversation()
        {
            AddQuest(60, Talk(QuestNpcTemplate, 2));
            var (client, player) = AddPlayer();
            var npc = AddNpc(QuestNpcTemplate, "Talker");
            QuestService.Accept(player, 60, QuestNpcTemplate);

            Interact(client, player, npc);
            Assert.Equal(new[] { 1 }, Entry(player, 60).Objectives);
            Assert.Equal(QuestState.Active, Entry(player, 60).State);

            Interact(client, player, npc);
            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 60).State);
        }

        [Fact]
        public void CollectObjectives_FollowTheBackpack()
        {
            AddQuest(70, Collect(3001, 5));
            var (_, player) = AddPlayer();
            Give(player, 3001, 2);

            QuestService.Accept(player, 70, QuestNpcTemplate);
            Assert.Equal(new[] { 2 }, Entry(player, 70).Objectives); // items already held count

            Give(player, 3001, 3);
            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 70).State);

            InventoryOps.RemoveFromBackpack(player, 3001, 1);
            Assert.Equal(QuestState.Active, Entry(player, 70).State);
            Assert.Equal(new[] { 4 }, Entry(player, 70).Objectives);
        }

        [Fact]
        public void CollectObjectives_AlreadyMetAtAcceptance_StartReadyToTurnIn()
        {
            AddQuest(71, Collect(3001, 3));
            var (_, player) = AddPlayer();
            Give(player, 3001, 10);

            QuestService.Accept(player, 71, QuestNpcTemplate);

            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 71).State);
            Assert.Equal(new[] { 3 }, Entry(player, 71).Objectives); // capped at the required count
        }

        [Fact]
        public void AQuestWithSeveralObjectives_NeedsAllOfThem()
        {
            AddQuest(72, Kill(101, 2), Collect(3001, 1));
            var (_, player) = AddPlayer();
            QuestService.Accept(player, 72, QuestNpcTemplate);

            KillSlimes(player, 2);
            Assert.Equal(QuestState.Active, Entry(player, 72).State);

            Give(player, 3001, 1);
            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 72).State);
        }

        // ---- Turning in ----

        [Fact]
        public void TurnIn_IsRefused_WhileTheObjectivesAreNotDone_ForTheWrongNpc_AndForQuestsNotInTheLog()
        {
            var (_, player) = AddPlayer();
            Assert.Equal(QuestResult.NotInLog, QuestService.TurnIn(player, 1, BlacksmithTemplate));

            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 3);
            Assert.Equal(QuestResult.NotComplete, QuestService.TurnIn(player, 1, BlacksmithTemplate));

            KillSlimes(player, 7);
            Assert.Equal(QuestResult.WrongNpc, QuestService.TurnIn(player, 1, 201));
            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 1).State);
        }

        [Fact]
        public void TurnIn_GrantsTheRewards_AndTheQuestIsRewardedForGood()
        {
            var (client, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 10);
            long expBefore = player.Exp;

            Assert.Equal(QuestResult.Ok, QuestService.TurnIn(player, 1, BlacksmithTemplate));

            Assert.Equal(QuestState.Rewarded, player.Progress.GetQuestState(1));
            Assert.Equal(50, player.Gold);
            Assert.Equal(3, Held(player, 2001));
            Assert.True(player.Exp > expBefore || player.Level > 1);
            Assert.Equal(QuestState.Rewarded, client.LastQuestUpdate(1)!.Value.State);
        }

        [Fact]
        public void TurnIn_PersistsTheStateAndRewardsTogether()
        {
            var (_, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 10);

            QuestService.TurnIn(player, 1, BlacksmithTemplate);

            using var db = Db();
            Assert.Equal(QuestState.Rewarded, db.CharacterQuests.Find(player.Id, 1)!.State);
            Assert.Equal(50, db.Characters.Find(player.Id)!.Gold);
            Assert.Equal(3, db.CharacterItems.Where(i => i.CharacterId == player.Id && i.TemplateId == 2001).Sum(i => i.Quantity));
            Assert.Equal(10, db.CharacterKillCounts.Find(player.Id, 101)!.Count);
        }

        [Fact]
        public void TurnIn_TwiceNeverRewardsTwice()
        {
            var (_, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 10);

            QuestService.TurnIn(player, 1, BlacksmithTemplate);
            Assert.Equal(QuestResult.NotInLog, QuestService.TurnIn(player, 1, BlacksmithTemplate));

            Assert.Equal(50, player.Gold);
            Assert.Equal(3, Held(player, 2001));
        }

        [Fact]
        public void TurnIn_TakesTheCollectedItems_AndKeepsTheSurplus()
        {
            var quest = AddQuest(80, Collect(3001, 5));
            quest.Rewards.Gold = 10;
            var (_, player) = AddPlayer();
            Give(player, 3001, 8);
            QuestService.Accept(player, 80, QuestNpcTemplate);

            Assert.Equal(QuestResult.Ok, QuestService.TurnIn(player, 80, QuestNpcTemplate));

            Assert.Equal(3, Held(player, 3001));
            Assert.Equal(10, player.Gold);
        }

        [Fact]
        public void TurnIn_WithAFullBackpack_ChangesNothing()
        {
            var (_, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 10);
            FillBackpack(player);
            int itemsBefore = player.Inventory.Count;

            Assert.Equal(QuestResult.InventoryFull, QuestService.TurnIn(player, 1, BlacksmithTemplate));

            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 1).State);
            Assert.Equal(0, player.Gold);
            Assert.Equal(itemsBefore, player.Inventory.Count);
            Assert.Equal(0, Held(player, 2001));

            // Making room is enough: the same quest can then be turned in
            InventoryOps.RemoveFromBackpack(player, 1001, 1);
            Assert.Equal(QuestResult.Ok, QuestService.TurnIn(player, 1, BlacksmithTemplate));
            Assert.Equal(3, Held(player, 2001));
        }

        [Fact]
        public void TurnIn_CountsTheSlotsFreedByTheItemsHandedIn()
        {
            var quest = AddQuest(81, Collect(1001, 1)); // one sword, which never stacks
            quest.Rewards.Items.Add(new QuestRewardItem { ItemTemplateId = 2001, Quantity = 1 });
            var (_, player) = AddPlayer();
            FillBackpack(player); // every slot holds a sword
            QuestService.Accept(player, 81, QuestNpcTemplate);

            Assert.Equal(QuestResult.Ok, QuestService.TurnIn(player, 81, QuestNpcTemplate));

            Assert.Equal(1, Held(player, 2001));
            Assert.Equal(player.InventorySlots - 1, Held(player, 1001));
        }

        [Fact]
        public void TurnIn_RecountsCollectItems_SoDroppedOnesCannotBeHandedIn()
        {
            AddQuest(82, Collect(3001, 5));
            var (_, player) = AddPlayer();
            Give(player, 3001, 5);
            QuestService.Accept(player, 82, QuestNpcTemplate);
            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 82).State);

            // The stored state is stale (as if an item moved without an event): the backpack is the truth
            player.Inventory.Clear();

            Assert.Equal(QuestResult.NotComplete, QuestService.TurnIn(player, 82, QuestNpcTemplate));
            Assert.Equal(QuestState.Active, Entry(player, 82).State);
        }

        // ---- Abandoning ----

        [Fact]
        public void Abandon_RemovesTheQuest_AndItCanBeAcceptedAgainFromScratch()
        {
            var (client, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 6);

            Assert.True(QuestService.Abandon(player, 1));

            Assert.Null(player.Progress.GetQuest(1));
            Assert.Equal(QuestState.Available, client.LastQuestUpdate(1)!.Value.State);
            using (var db = Db()) Assert.Null(db.CharacterQuests.Find(player.Id, 1));

            QuestService.Accept(player, 1, BlacksmithTemplate);
            Assert.Equal(new[] { 0 }, Entry(player, 1).Objectives);
        }

        [Fact]
        public void Abandon_CannotRemoveARewardedQuest_OrOneThatIsNotInTheLog()
        {
            var (_, player) = AddPlayer();
            Assert.False(QuestService.Abandon(player, 1));

            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 10);
            QuestService.TurnIn(player, 1, BlacksmithTemplate);

            Assert.False(QuestService.Abandon(player, 1));
            Assert.Equal(QuestState.Rewarded, player.Progress.GetQuestState(1));
        }

        [Fact]
        public void TheAbandonPacket_RemovesTheQuest()
        {
            var (client, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);

            using var write = new Packet(OpCode.QuestAbandonRequest);
            write.Write(1);
            QuestHandler.HandleAbandon(client, new Packet(write.ToArray()));

            Assert.Null(player.Progress.GetQuest(1));
        }

        // ---- The quest log on the wire ----

        [Fact]
        public void EnteringTheWorld_SendsTheWholeQuestLog()
        {
            var (client, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 4);
            player.Progress.SetQuest(9, QuestState.Rewarded, new[] { 1, 1 });
            client.Raw.Clear();

            WorldEntryHandler.SendInitialState(client, player);

            var packet = client.Of(OpCode.QuestLogSync).Single();
            Assert.Equal(2, packet.ReadInt());

            Assert.Equal(1, packet.ReadInt());
            var slimes = RecordingClient.ReadQuestEntry(packet);
            Assert.Equal(QuestState.Active, slimes.State);
            Assert.Equal(DataManager.Quests[1].Name, slimes.Name);
            var objective = Assert.Single(slimes.Objectives);
            Assert.Equal(QuestService.ObjectiveText(DataManager.Quests[1].Objectives[0]), objective.Text);
            Assert.Equal(4, objective.Current);
            Assert.Equal(DataManager.Quests[1].Objectives[0].Count, objective.Required);

            // Quest 9 has no template: it still gets a valid entry from its saved counters
            Assert.Equal(9, packet.ReadInt());
            var unknown = RecordingClient.ReadQuestEntry(packet);
            Assert.Equal(QuestState.Rewarded, unknown.State);
            Assert.Equal(2, unknown.Objectives.Count);
        }

        [Fact]
        public void QuestUpdate_CarriesTheNameAndObjectiveTexts()
        {
            var (client, player) = AddPlayer();
            var quest = AddQuest(70, Kill(101, 3), Talk(QuestNpcTemplate));
            quest.Objectives[1].Description = "Report back";

            QuestService.Accept(player, 70, QuestNpcTemplate);

            var entry = client.LastQuestEntry(70)!;
            Assert.Equal("Quest 70", entry.Name);
            Assert.Equal(QuestState.Active, entry.State);
            Assert.Equal(2, entry.Objectives.Count);
            Assert.Equal(QuestService.ObjectiveText(quest.Objectives[0]), entry.Objectives[0].Text);
            Assert.Equal((0, 3), (entry.Objectives[0].Current, entry.Objectives[0].Required));
            Assert.Equal(("Report back", 0, 1), entry.Objectives[1]);
        }

        [Fact]
        public void QuestUpdate_ForAQuestThatLeftTheLog_HasNoNameAndNoObjectives()
        {
            var (client, player) = AddPlayer();
            QuestService.Accept(player, 1, BlacksmithTemplate);

            QuestService.Abandon(player, 1);

            var entry = client.LastQuestEntry(1)!;
            Assert.Equal(QuestState.Available, entry.State);
            Assert.Equal(string.Empty, entry.Name);
            Assert.Empty(entry.Objectives);
        }

        [Fact]
        public void QuestUpdate_ForAQuestWhoseTemplateWasRemoved_StillHasAValidEntry()
        {
            var (client, player) = AddPlayer();
            player.Progress.SetQuest(88, QuestState.Active, new[] { 2, 5 });
            client.Raw.Clear();

            QuestHandler.SendQuestUpdate(player, 88);

            var entry = client.LastQuestEntry(88)!;
            Assert.Equal(QuestState.Active, entry.State);
            Assert.Equal("Unknown quest", entry.Name);
            Assert.Equal(new[] { 2, 5 }, entry.Objectives.Select(o => o.Current).ToArray());
        }

        // ---- Through the NPC's dialogue (the whole loop) ----

        [Fact]
        public void TheSlimeMenace_CanBePlayedThroughTheBlacksmithsDialogue()
        {
            var (client, player) = AddPlayer("Hero");
            var npc = AddNpc(BlacksmithTemplate, "Augustos");

            // Offered
            Interact(client, player, npc);
            Assert.True(client.LastDialogue().Has("[New] The Slime Menace"));
            Pick(client, npc, "[New]");
            Assert.Contains("Slimes are creeping closer", client.LastDialogue().Text);
            Assert.Contains("120 EXP", client.LastDialogue().Text);
            Assert.Contains("50 gold", client.LastDialogue().Text);

            // Accepted: the greeting now shows it as in progress, with the objective counter on its screen
            Pick(client, npc, "Accept");
            Assert.Equal(QuestState.Active, player.Progress.GetQuestState(1));
            Assert.False(client.LastDialogue().Has("[New]"));
            KillSlimes(player, 3);
            Interact(client, player, npc);
            Pick(client, npc, "[In progress]");
            Assert.Contains("Defeat Slime: 3/10", client.LastDialogue().Text);
            Pick(client, npc, "Back");

            // Done: completing it from the same NPC pays out
            KillSlimes(player, 7);
            Interact(client, player, npc);
            Pick(client, npc, "[Complete]");
            Assert.Contains("The town sleeps easier", client.LastDialogue().Text);
            Pick(client, npc, "Complete quest");

            Assert.Equal(QuestState.Rewarded, player.Progress.GetQuestState(1));
            Assert.Equal(50, player.Gold);
            Assert.Equal(3, Held(player, 2001));
            Assert.False(client.LastDialogue().Has("The Slime Menace")); // nothing left to offer or complete
        }

        [Fact]
        public void QuestsAreOnlyOfferedByTheirGiver_AndOnceTheLevelAllows()
        {
            var quest = AddQuest(90, Kill(101, 1));
            quest.MinLevel = 3;
            var (c1, low) = AddPlayer("Low", level: 1);
            var (c2, ready) = AddPlayer("Ready", level: 3);
            var giver = AddNpc(QuestNpcTemplate, "Giver");
            var smith = AddNpc(BlacksmithTemplate, "Smith");

            Interact(c1, low, giver);
            Interact(c2, ready, giver);
            Interact(c2, ready, smith);

            Assert.False(c1.LastDialogue().Has("Quest 90"));
            Interact(c2, ready, giver);
            Assert.True(c2.LastDialogue().Has("[New] Quest 90"));
            Interact(c2, ready, smith);
            Assert.False(c2.LastDialogue().Has("Quest 90"));
        }

        [Fact]
        public void AcceptingFromTheWrongNpc_ThroughADoctoredOption_IsRefused()
        {
            // A dialogue of another NPC that (wrongly) offers quest 1: the quest engine still checks who the giver is
            var dialogue = new DialogueTemplate
            {
                Id = "sneaky",
                Greetings = { new DialogueGreeting { Text = "Psst.", Options =
                {
                    new DialogueOption { Label = "Take the quest", Action = InteractAction.StartQuest, Value = 1 },
                    new DialogueOption { Label = "Bye", Action = InteractAction.Close }
                } } }
            };
            DataManager.Dialogues["sneaky"] = dialogue;
            NewNpcTemplate("sneaky", QuestNpcTemplate);
            var (client, player) = AddPlayer();
            var npc = AddNpc(QuestNpcTemplate);
            Interact(client, player, npc);

            Pick(client, npc, "Take the quest");

            Assert.Equal(DialogueCloseReason.RequirementsNotMet, client.LastClose());
            Assert.Null(player.Progress.GetQuest(1));
        }

        [Fact]
        public void TurningInWithAFullBackpack_ThroughTheDialogue_ClosesWithInventoryFull()
        {
            var (client, player) = AddPlayer();
            var npc = AddNpc(BlacksmithTemplate);
            QuestService.Accept(player, 1, BlacksmithTemplate);
            KillSlimes(player, 10);
            FillBackpack(player);
            Interact(client, player, npc);
            Pick(client, npc, "[Complete]");

            Pick(client, npc, "Complete quest");

            Assert.Equal(DialogueCloseReason.InventoryFull, client.LastClose());
            Assert.Equal(QuestState.ReadyToTurnIn, Entry(player, 1).State);
        }
    }
}
