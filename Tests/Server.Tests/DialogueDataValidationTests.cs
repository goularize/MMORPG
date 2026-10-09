using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Data.Models;
using Shared.Constants;
using Shared.Data;
using Shared.Enums;
using Xunit;

namespace Server.Tests
{
    /// <summary>The startup rules for Dialogues.json, Quests.json and the conditions in them.</summary>
    public class DialogueDataValidationTests
    {
        private static DialogueOption Close(string label = "Goodbye") => new() { Label = label, Action = InteractAction.Close };

        private static DialogueTemplate ValidDialogue(string id = "d1") => new()
        {
            Id = id,
            Greetings = { new DialogueGreeting { Priority = 0, Text = "Hello {{playerName}}.", Options = { Close() } } }
        };

        private static NpcTemplate Npc(int id, string dialogueId = "d1") => new()
        {
            TemplateId = id, Name = "Npc" + id, Type = "Friendly",
            LevelRange = new[] { 1, 1 }, StatVarianceRange = new[] { 1f, 1f }, DialogueId = dialogueId
        };

        private static ItemTemplate Item(int id, int maxStack = 10) => new() { TemplateId = id, Name = "Item" + id, Type = ItemType.Material, MaxStack = maxStack };

        private static QuestTemplate ValidQuest(int id = 1) => new()
        {
            Id = id, Name = "Quest" + id, MinLevel = 1, GiverNpcTemplateId = 10, TurnInNpcTemplateId = 10,
            Objectives = { new QuestObjective { Type = QuestObjectiveType.Kill, Id = 10, Count = 3 } },
            Rewards = { Exp = 10, Gold = 5, Items = { new QuestRewardItem { ItemTemplateId = 1, Quantity = 2 } } },
            Text = { Offer = "Please.", Progress = "Still?", Complete = "Thanks." }
        };

        private static DataValidator.Result Validate(
            IEnumerable<DialogueTemplate>? dialogues = null, IEnumerable<QuestTemplate>? quests = null, IEnumerable<NpcTemplate>? npcs = null)
        {
            return DataValidator.Validate(
                new List<ItemTemplate> { Item(1) }, new List<RecipeTemplate>(), new List<LootTableTemplate>(),
                (npcs ?? new[] { Npc(10) }).ToList(), new List<SpawnerTemplate>(), new CharacterCreationTemplate(),
                dialogues: (dialogues ?? new[] { ValidDialogue() }).ToList(),
                quests: (quests ?? new List<QuestTemplate>()).ToList());
        }

        private static void AssertError(DataValidator.Result result, string part) =>
            Assert.Contains(result.Errors, e => e.Contains(part));

        [Fact]
        public void AValidDialogueAndQuest_HaveNoErrors()
        {
            var result = Validate(quests: new[] { ValidQuest() });

            Assert.Empty(result.Errors);
        }

        [Fact]
        public void NpcWithUnknownDialogueId_IsAnError()
        {
            var result = Validate(npcs: new[] { Npc(10, "missing") });

            AssertError(result, "DialogueId 'missing'");
        }

        [Fact]
        public void DuplicateDialogueIds_AreReported()
        {
            var result = Validate(dialogues: new[] { ValidDialogue("d1"), ValidDialogue("d1") });

            AssertError(result, "'d1' is defined 2 times");
        }

        [Fact]
        public void UnusedDialogue_IsOnlyAWarning()
        {
            var result = Validate(dialogues: new[] { ValidDialogue("d1"), ValidDialogue("unused") });

            Assert.Empty(result.Errors);
            Assert.Contains(result.Warnings, w => w.Contains("unused"));
        }

        [Fact]
        public void DialogueWithoutAFallbackGreeting_IsAnError()
        {
            var dialogue = ValidDialogue();
            dialogue.Greetings[0].Conditions.Add(new Condition { Type = ConditionType.Level, Min = 5 });

            AssertError(Validate(dialogues: new[] { dialogue }), "fallback");
        }

        [Fact]
        public void DialogueWithoutGreetings_IsAnError()
        {
            var dialogue = new DialogueTemplate { Id = "d1" };

            AssertError(Validate(dialogues: new[] { dialogue }), "no greetings");
        }

        [Fact]
        public void GreetingsSharingAPriority_AreAnError()
        {
            var dialogue = ValidDialogue();
            dialogue.Greetings.Add(new DialogueGreeting
            {
                Priority = 0, Text = "Hi again.", Options = { Close() },
                Conditions = { new Condition { Type = ConditionType.Level, Min = 2 } }
            });

            AssertError(Validate(dialogues: new[] { dialogue }), "Priority 0 is used by 2 greetings");
        }

        [Theory]
        [InlineData("Hi {{nope}}", "unknown token 'nope'")]
        [InlineData("Hi {{playerName", "unclosed token")]
        [InlineData("Hi playerName}}", "stray closing")]
        [InlineData("", "text is empty")]
        public void BadText_IsReported(string text, string expected)
        {
            var dialogue = ValidDialogue();
            dialogue.Greetings[0].Text = text;

            AssertError(Validate(dialogues: new[] { dialogue }), expected);
        }

        [Fact]
        public void TooLongText_IsReported()
        {
            var dialogue = ValidDialogue();
            dialogue.Greetings[0].Text = new string('a', DialogueRules.MaxTextLength + 1);

            AssertError(Validate(dialogues: new[] { dialogue }), "longer than");
        }

        [Fact]
        public void KnownTokens_AreAccepted()
        {
            var dialogue = ValidDialogue();
            dialogue.Greetings[0].Text = "{{npcName}} greets {{playerName}} (level {{playerLevel}}).";

            Assert.Empty(Validate(dialogues: new[] { dialogue }).Errors);
        }

        [Fact]
        public void OptionProblems_AreReported()
        {
            var dialogue = ValidDialogue();
            dialogue.Greetings[0].Options.AddRange(new[]
            {
                new DialogueOption { Label = "Nothing", Action = InteractAction.None },
                new DialogueOption { Label = "Goto", Action = InteractAction.GotoNode },
                new DialogueOption { Label = "Lost", Action = InteractAction.GotoNode, Node = "nowhere" },
                new DialogueOption { Label = "Flag", Action = InteractAction.SetFlag, Name = "bad flag" },
                new DialogueOption { Label = "Quest", Action = InteractAction.StartQuest, Value = 99 },
                new DialogueOption { Label = "Close", Action = InteractAction.Close, Node = "$greeting" },
                new DialogueOption { Label = "", Action = InteractAction.Close }
            });
            var result = Validate(dialogues: new[] { dialogue });

            AssertError(result, "Action is missing or None");
            AssertError(result, "GotoNode needs a Node");
            AssertError(result, "Node 'nowhere' does not exist");
            AssertError(result, "may only contain letters");
            AssertError(result, "unknown quest 99");
            AssertError(result, "cannot have a Node");
            AssertError(result, "label: text is empty");
        }

        [Fact]
        public void ScreenWithoutOptions_AndTooManyOptions_AreReported()
        {
            var empty = ValidDialogue("empty");
            empty.Greetings[0].Options.Clear();
            var crowded = ValidDialogue("crowded");
            for (int i = 0; i < DialogueRules.MaxAuthoredOptions; i++) crowded.Greetings[0].Options.Add(Close("Bye " + i));

            var result = Validate(dialogues: new[] { empty, crowded });

            AssertError(result, "has no options");
            AssertError(result, $"maximum is {DialogueRules.MaxAuthoredOptions}");
        }

        [Fact]
        public void NodesThatCannotReachAnEnd_AreReported()
        {
            var dialogue = ValidDialogue();
            dialogue.Nodes["a"] = new DialogueScreen { Text = "A", Options = { new DialogueOption { Label = "to b", Action = InteractAction.GotoNode, Node = "b" } } };
            dialogue.Nodes["b"] = new DialogueScreen { Text = "B", Options = { new DialogueOption { Label = "to a", Action = InteractAction.GotoNode, Node = "a" } } };

            var result = Validate(dialogues: new[] { dialogue });

            AssertError(result, "node 'a': no option leads to an end");
            AssertError(result, "node 'b': no option leads to an end");
        }

        [Fact]
        public void ANodeThatLeadsBackToAGreetingWithAnExit_CanFinish()
        {
            var dialogue = ValidDialogue();
            dialogue.Nodes["a"] = new DialogueScreen { Text = "A", Options = { new DialogueOption { Label = "back", Action = InteractAction.GotoNode, Node = "$greeting" } } };
            dialogue.Greetings[0].Options.Add(new DialogueOption { Label = "a", Action = InteractAction.GotoNode, Node = "a" });

            Assert.Empty(Validate(dialogues: new[] { dialogue }).Errors);
        }

        [Fact]
        public void ReservedNodeNames_AreRejected()
        {
            var dialogue = ValidDialogue();
            dialogue.Nodes["quest:offer:1"] = new DialogueScreen { Text = "X", Options = { Close() } };
            dialogue.Nodes["$greeting"] = new DialogueScreen { Text = "Y", Options = { Close() } };

            var result = Validate(dialogues: new[] { dialogue });

            Assert.Equal(2, result.Errors.Count(e => e.Contains("node names cannot")));
        }

        [Fact]
        public void ConditionProblems_AreReported()
        {
            var dialogue = ValidDialogue();
            dialogue.Greetings[0].Options[0].Conditions.AddRange(new[]
            {
                new Condition { Type = ConditionType.Unknown },
                new Condition { Type = ConditionType.KillCount, Id = 999, Min = 1 },
                new Condition { Type = ConditionType.KillCount, Id = 10, Min = 0 },
                new Condition { Type = ConditionType.QuestState, Id = 999 },
                new Condition { Type = ConditionType.Flag, Name = "" },
                new Condition { Type = ConditionType.Level, Min = 10, Max = 5 },
                new Condition { Type = ConditionType.HasItem, Id = 999, Min = 1 },
                new Condition { Type = ConditionType.HasItem, Id = 1, Min = 0 }
            });

            var result = Validate(dialogues: new[] { dialogue });

            AssertError(result, "Type is missing or Unknown");
            AssertError(result, "unknown NPC template 999");
            AssertError(result, "Min must be at least 1");
            AssertError(result, "unknown quest 999");
            AssertError(result, "A flag must be");
            AssertError(result, "Max must be 0");
            AssertError(result, "unknown item 999");
        }

        // ---- Quests ----

        [Fact]
        public void QuestWithUnknownOrSilentNpcs_IsAnError()
        {
            var quest = ValidQuest();
            quest.GiverNpcTemplateId = 777;
            quest.TurnInNpcTemplateId = 11;

            var result = Validate(quests: new[] { quest }, npcs: new[] { Npc(10), Npc(11, dialogueId: "") });

            AssertError(result, "GiverNpcTemplateId references unknown NPC template 777");
            AssertError(result, "TurnInNpcTemplateId 11");
        }

        [Fact]
        public void QuestObjectiveProblems_AreReported()
        {
            var quest = ValidQuest();
            quest.Objectives.Clear();
            var empty = Validate(quests: new[] { quest });
            AssertError(empty, "has no objectives");

            quest.Objectives.AddRange(new[]
            {
                new QuestObjective { Type = QuestObjectiveType.Unknown, Id = 1, Count = 1 },
                new QuestObjective { Type = QuestObjectiveType.Kill, Id = 999, Count = 1 },
                new QuestObjective { Type = QuestObjectiveType.Collect, Id = 999, Count = 1 },
                new QuestObjective { Type = QuestObjectiveType.Talk, Id = 10, Count = 0 }
            });
            var result = Validate(quests: new[] { quest });

            AssertError(result, "Type is missing or Unknown");
            AssertError(result, "(Kill): references unknown NPC template 999");
            AssertError(result, "(Collect): references unknown item 999");
            AssertError(result, "Count must be at least 1");
        }

        [Fact]
        public void QuestRewardAndTextProblems_AreReported()
        {
            var quest = ValidQuest();
            quest.Rewards.Exp = -1;
            quest.Rewards.Gold = -1;
            quest.Rewards.Items.Add(new QuestRewardItem { ItemTemplateId = 999, Quantity = 1 });
            quest.Rewards.Items.Add(new QuestRewardItem { ItemTemplateId = 1, Quantity = 50 });
            quest.Text.Offer = "";
            quest.Text.Complete = "Bad {{token}}";
            quest.MinLevel = 0;

            var result = Validate(quests: new[] { quest });

            AssertError(result, "Rewards.Exp cannot be negative");
            AssertError(result, "Rewards.Gold cannot be negative");
            AssertError(result, "unknown item 999");
            AssertError(result, "quantity must be between 1 and its MaxStack");
            AssertError(result, "Text.Offer: text is empty");
            AssertError(result, "Text.Complete: text uses unknown token");
            AssertError(result, "MinLevel must be at least 1");
        }

        [Fact]
        public void QuestPrerequisites_AreValidated_AndCannotDependOnThemselves()
        {
            var quest = ValidQuest(1);
            quest.Prerequisites.Add(new Condition { Type = ConditionType.QuestState, Id = 1, State = QuestState.Rewarded });
            quest.Prerequisites.Add(new Condition { Type = ConditionType.QuestState, Id = 2, State = QuestState.Rewarded });

            var result = Validate(quests: new[] { quest });

            AssertError(result, "cannot depend on itself");
            AssertError(result, "unknown quest 2");
        }

        [Fact]
        public void DuplicateQuestIds_AreReported()
        {
            var result = Validate(quests: new[] { ValidQuest(1), ValidQuest(1) });

            AssertError(result, "Id 1 is defined 2 times");
        }

        [Fact]
        public void DialogueOptionsCanReferenceQuests()
        {
            var dialogue = ValidDialogue();
            dialogue.Greetings[0].Options.Add(new DialogueOption { Label = "Quest", Action = InteractAction.StartQuest, Value = 1 });

            Assert.Empty(Validate(dialogues: new[] { dialogue }, quests: new[] { ValidQuest(1) }).Errors);
        }

        // ---- Shipped data ----

        [Fact]
        public void ShippedDialoguesAndQuests_AreLoaded_AndTheNpcsPointAtThem()
        {
            DataManager.Initialize();

            Assert.True(DataManager.Dialogues.ContainsKey("town_blacksmith"));
            Assert.True(DataManager.Dialogues.ContainsKey("town_innkeeper"));
            Assert.True(DataManager.Quests.ContainsKey(1));
            Assert.True(DataManager.Npcs[200].OffersAction(InteractAction.OpenShop));
            Assert.True(DataManager.Npcs[201].OffersAction(InteractAction.BindPoint));
            Assert.False(DataManager.Npcs[101].OffersAction(InteractAction.BindPoint)); // the slime says nothing
        }
    }
}
