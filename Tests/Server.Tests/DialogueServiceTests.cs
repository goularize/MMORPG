using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Data.Models;
using Server.Dialogue;
using Server.Handlers;
using Server.World;
using Shared.Data;
using Shared.Enums;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class DialogueServiceTests : NpcWorldTestBase
    {
        private const int CustomNpcTemplate = 9001;

        /// <summary>Registers a dialogue and an NPC template that uses it.</summary>
        private void UseDialogue(DialogueTemplate dialogue)
        {
            DataManager.Dialogues[dialogue.Id] = dialogue;
            NewNpcTemplate(dialogue.Id, CustomNpcTemplate);
        }

        private static DialogueOption Opt(string label, InteractAction action, string? node = null, string? name = null, int value = 0) =>
            new() { Label = label, Action = action, Node = node, Name = name, Value = value };

        private static DialogueTemplate Simple(string id, params DialogueOption[] options)
        {
            var greeting = new DialogueGreeting { Text = "Hello {{playerName}}, I am {{npcName}} (you are level {{playerLevel}}).", Options = options.ToList() };
            return new DialogueTemplate { Id = id, Greetings = { greeting } };
        }

        // ---- Opening and greetings ----

        [Fact]
        public void Interact_OpensTheFallbackGreeting_WithTokensRendered()
        {
            var (client, player) = AddPlayer("Hero");
            var npc = AddNpc(200, "Augustos");

            Interact(client, player, npc);

            var view = client.LastDialogue();
            Assert.Equal(npc.Id, view.NpcId);
            Assert.Equal("Augustos", view.NpcName);
            Assert.Equal("Hi Hero, I can buy and sell equipment. Want to see my shop?", view.Text);
            Assert.True(view.Has("Show me your shop"));
            Assert.True(view.Has("Goodbye"));
            Assert.Equal(npc.Id, player.Dialogue!.NpcId);
        }

        [Fact]
        public void TokensInTextAreRendered_PlayerNameLevelAndNpcName()
        {
            UseDialogue(Simple("tokens", Opt("Bye", InteractAction.Close)));
            var (client, player) = AddPlayer("Hero", level: 7);
            var npc = AddNpc(CustomNpcTemplate, "Mira");

            Interact(client, player, npc);

            Assert.Equal("Hello Hero, I am Mira (you are level 7).", client.LastDialogue().Text);
        }

        [Fact]
        public void TheGreetingFollowsProgress_AndStopsOfferingTheStoryOnceHeard()
        {
            var (client, player) = AddPlayer("Hero");
            var npc = AddNpc(200, "Augustos");

            KillSlimes(player, 9);
            Interact(client, player, npc);
            Assert.DoesNotContain("killed 10 slimes", client.LastDialogue().Text);

            KillSlimes(player, 1);
            Interact(client, player, npc);
            Assert.Contains("killed 10 slimes", client.LastDialogue().Text);
            Assert.True(client.LastDialogue().Has("Tell me about the slimes"));

            Pick(client, npc, "Tell me about the slimes");
            Assert.Contains("Long ago", client.LastDialogue().Text);

            Pick(client, npc, "Interesting");
            Assert.True(player.Progress.HasFlag("heard_slime_history"));
            Assert.DoesNotContain("killed 10 slimes", client.LastDialogue().Text); // back at the plain greeting
            Assert.False(client.LastDialogue().Has("Tell me about the slimes"));
        }

        [Fact]
        public void TheHighestPriorityGreetingWhoseConditionsHoldIsChosen()
        {
            var dialogue = Simple("prio", Opt("Bye", InteractAction.Close));
            dialogue.Greetings.Add(new DialogueGreeting
            {
                Priority = 5, Text = "Veteran!", Options = { Opt("Bye", InteractAction.Close) },
                Conditions = { new Condition { Type = ConditionType.Level, Min = 10 } }
            });
            dialogue.Greetings.Add(new DialogueGreeting
            {
                Priority = 9, Text = "Hero of the realm!", Options = { Opt("Bye", InteractAction.Close) },
                Conditions = { new Condition { Type = ConditionType.Level, Min = 50 } }
            });
            UseDialogue(dialogue);
            var npc = AddNpc(CustomNpcTemplate);

            var (c1, novice) = AddPlayer("Novice", level: 1);
            var (c2, veteran) = AddPlayer("Vet", level: 10);
            var (c3, hero) = AddPlayer("Champ", level: 60);
            Interact(c1, novice, npc);
            Interact(c2, veteran, npc);
            Interact(c3, hero, npc);

            Assert.StartsWith("Hello Novice", c1.LastDialogue().Text);
            Assert.Equal("Veteran!", c2.LastDialogue().Text);
            Assert.Equal("Hero of the realm!", c3.LastDialogue().Text);
        }

        [Fact]
        public void OptionsWhoseConditionsFail_AreNotOffered()
        {
            var secret = Opt("Secret", InteractAction.Close);
            secret.Conditions.Add(new Condition { Type = ConditionType.Level, Min = 5 });
            UseDialogue(Simple("hidden", Opt("Bye", InteractAction.Close), secret));
            var npc = AddNpc(CustomNpcTemplate);
            var (c1, low) = AddPlayer("Low", level: 1);
            var (c2, high) = AddPlayer("High", level: 5);

            Interact(c1, low, npc);
            Interact(c2, high, npc);

            Assert.False(c1.LastDialogue().Has("Secret"));
            Assert.True(c2.LastDialogue().Has("Secret"));
        }

        [Fact]
        public void AScreenWhoseOptionsAreAllHidden_GetsAGoodbye_SoThePlayerIsNeverTrapped()
        {
            var only = Opt("Only", InteractAction.Close);
            only.Conditions.Add(new Condition { Type = ConditionType.Level, Min = 99 });
            UseDialogue(Simple("trap", only));
            var (client, player) = AddPlayer();
            var npc = AddNpc(CustomNpcTemplate);

            Interact(client, player, npc);

            var view = client.LastDialogue();
            Assert.Single(view.Options);
            Pick(client, npc, "Goodbye");
            Assert.Null(player.Dialogue);
        }

        [Fact]
        public void AnNpcWithoutADialogue_HasNothingToDo()
        {
            var (client, player) = AddPlayer();
            var slime = AddNpc(101, "Slime");

            Interact(client, player, slime);

            Assert.Equal(InteractOutcome.NothingToDo, client.LastInteractReply()!.Value.Outcome);
            Assert.Equal(0, client.Count(OpCode.DialogueOpen));
            Assert.Null(player.Dialogue);
        }

        [Fact]
        public void Opening_RaisesNpcTalked()
        {
            var talked = new List<int>();
            void OnTalked(Server.World.Entities.Player p, int templateId) => talked.Add(templateId);
            GameEvents.Instance.NpcTalked += OnTalked;
            try
            {
                var (client, player) = AddPlayer();
                Interact(client, player, AddNpc(200));
            }
            finally
            {
                GameEvents.Instance.NpcTalked -= OnTalked;
            }

            Assert.Equal(new[] { 200 }, talked);
        }

        // ---- Choosing ----

        [Fact]
        public void ChoosingAnOptionThatWasNotOffered_ClosesTheConversation()
        {
            var (client, player) = AddPlayer();
            var npc = AddNpc(200);
            Interact(client, player, npc);

            Choose(client, npc, 999);

            Assert.Equal(DialogueCloseReason.Invalid, client.LastClose());
            Assert.Null(player.Dialogue);
        }

        [Fact]
        public void ChoosingWithoutAConversation_IsAnsweredWithAClose()
        {
            var (client, player) = AddPlayer();
            var npc = AddNpc(200);

            Choose(client, npc, 1);

            Assert.Equal(DialogueCloseReason.Invalid, client.LastClose());
            Assert.Null(player.Dialogue);
        }

        [Fact]
        public void ChoosingForAnotherNpc_DoesNotTouchTheOpenConversation()
        {
            var (client, player) = AddPlayer();
            var talking = AddNpc(200);
            var other = AddNpc(201, x: 1);
            Interact(client, player, talking);

            Choose(client, other, 1);

            Assert.Equal(DialogueCloseReason.Invalid, client.LastClose());
            Assert.Equal(talking.Id, player.Dialogue!.NpcId);
        }

        [Fact]
        public void OptionIdsOfAnotherPlayersConversation_AreNotValid()
        {
            var secret = Opt("Hidden door", InteractAction.SetFlag, name: "door_open");
            UseDialogue(Simple("sessions", Opt("Bye", InteractAction.Close), secret));
            var npc = AddNpc(CustomNpcTemplate);
            var (c1, p1) = AddPlayer("One");
            var (c2, p2) = AddPlayer("Two");
            Interact(c1, p1, npc);
            // Player two never talked to the NPC, but guesses the option ids of player one's screen
            Choose(c2, npc, c1.LastDialogue().IdOf("Hidden door"));

            Assert.False(p2.Progress.HasFlag("door_open"));
            Assert.Equal(DialogueCloseReason.Invalid, c2.LastClose());
            Assert.NotNull(p1.Dialogue); // player one's conversation is untouched
        }

        [Fact]
        public void ChoosingAfterWalkingAway_ClosesAsTooFar()
        {
            var flag = Opt("Flag", InteractAction.SetFlag, name: "never");
            UseDialogue(Simple("far", flag, Opt("Bye", InteractAction.Close)));
            var (client, player) = AddPlayer();
            var npc = AddNpc(CustomNpcTemplate);
            Interact(client, player, npc);
            int optionId = client.LastDialogue().IdOf("Flag");

            player.Position = new Vector3(40, 0, 0);
            Choose(client, npc, optionId);

            Assert.Equal(DialogueCloseReason.TooFar, client.LastClose());
            Assert.False(player.Progress.HasFlag("never"));
            Assert.Null(player.Dialogue);
        }

        [Fact]
        public void ChoosingAfterTheOptionsConditionStoppedHolding_IsRefused()
        {
            var gated = Opt("Gated", InteractAction.SetFlag, name: "gated_done");
            gated.Conditions.Add(new Condition { Type = ConditionType.Flag, Name = "key" });
            UseDialogue(Simple("gate", gated, Opt("Bye", InteractAction.Close)));
            var (client, player) = AddPlayer();
            var npc = AddNpc(CustomNpcTemplate);
            player.Progress.SetFlag("key");
            Interact(client, player, npc);
            int optionId = client.LastDialogue().IdOf("Gated");

            player.Progress.ClearFlag("key"); // changed while the window was open
            Choose(client, npc, optionId);

            Assert.Equal(DialogueCloseReason.RequirementsNotMet, client.LastClose());
            Assert.False(player.Progress.HasFlag("gated_done"));
        }

        [Fact]
        public void TheClientClosingItsWindow_EndsTheSession()
        {
            var (client, player) = AddPlayer();
            var npc = AddNpc(200);
            Interact(client, player, npc);

            using var write = new Packet(OpCode.DialogueClose);
            write.Write(npc.Id);
            DialogueHandler.HandleClose(client, new Packet(write.ToArray()));

            Assert.Null(player.Dialogue);
        }

        [Fact]
        public void LeavingTheMap_EndsTheConversation()
        {
            var (client, player) = AddPlayer();
            Interact(client, player, AddNpc(200));
            Assert.NotNull(player.Dialogue);

            Map.RemovePlayer(player.Id, saveState: false);

            Assert.Null(player.Dialogue);
        }

        // ---- Actions ----

        [Fact]
        public void SetFlagAndClearFlag_ChangeTheProgress_AndContinueAtTheNode()
        {
            var set = Opt("Set it", InteractAction.SetFlag, node: "$greeting", name: "lever");
            var clear = Opt("Clear it", InteractAction.ClearFlag, name: "lever");
            UseDialogue(Simple("flags", set, clear));
            var (client, player) = AddPlayer();
            var npc = AddNpc(CustomNpcTemplate);
            Interact(client, player, npc);

            Pick(client, npc, "Set it");
            Assert.True(player.Progress.HasFlag("lever"));
            Assert.NotNull(player.Dialogue); // the option continued at the greeting

            Pick(client, npc, "Clear it");
            Assert.False(player.Progress.HasFlag("lever"));
            Assert.Null(player.Dialogue); // no Node: the conversation ended
            Assert.Equal(DialogueCloseReason.Finished, client.LastClose());
        }

        [Fact]
        public void GotoNode_ShowsTheNode_AndItsOptionsReplaceTheOldOnes()
        {
            var dialogue = Simple("nodes", Opt("Tell me more", InteractAction.GotoNode, node: "more"));
            dialogue.Greetings[0].Options.Add(Opt("Bye", InteractAction.Close));
            dialogue.Nodes["more"] = new DialogueScreen
            {
                Text = "More, for {{playerName}}.",
                Options = { Opt("Thanks", InteractAction.GotoNode, node: "$greeting") }
            };
            UseDialogue(dialogue);
            var (client, player) = AddPlayer("Hero");
            var npc = AddNpc(CustomNpcTemplate);
            Interact(client, player, npc);

            Pick(client, npc, "Tell me more");
            Assert.Equal("More, for Hero.", client.LastDialogue().Text);
            Assert.False(client.LastDialogue().Has("Bye"));

            Pick(client, npc, "Thanks");
            Assert.StartsWith("Hello Hero", client.LastDialogue().Text);
        }

        [Fact]
        public void TheInnkeepersBindOption_BindsAndEndsTheConversation()
        {
            var (client, player) = AddPlayer();
            player.BindMapId = 1;
            player.BindPosition = new Vector3(100, 0, 100);
            var npc = AddNpc(201, "Innkeeper");
            Interact(client, player, npc);

            Pick(client, npc, "bind point");

            Assert.Equal(player.Position, player.BindPosition);
            Assert.Equal(1, client.Count(OpCode.SetBindPointResponse));
            Assert.Null(player.Dialogue);
        }

        // ---- The session ends by itself ----

        [Fact]
        public void TheMapTick_ClosesTheConversation_WhenThePlayerWalksAway()
        {
            var (client, player) = AddPlayer();
            Interact(client, player, AddNpc(200));

            player.Position = new Vector3(40, 0, 0);
            Map.Update();

            Assert.Equal(DialogueCloseReason.TooFar, client.LastClose());
            Assert.Null(player.Dialogue);
        }

        [Fact]
        public void SmallMovementWhileTalking_KeepsTheConversationOpen()
        {
            var (client, player) = AddPlayer();
            Interact(client, player, AddNpc(200));

            player.Position = new Vector3(4, 0, 0); // a bit past the interact range of 3, within the keep range
            Map.Update();

            Assert.NotNull(player.Dialogue);
        }

        [Fact]
        public void TheConversation_EndsWhenTheNpcDies_OrThePlayerDies()
        {
            var (c1, p1) = AddPlayer("A");
            var (c2, p2) = AddPlayer("B");
            var mortal = AddNpc(200, "Mortal");
            Interact(c1, p1, mortal);
            Interact(c2, p2, mortal);

            mortal.Health = 0;
            DialogueService.Tick(p1);
            Assert.Equal(DialogueCloseReason.TargetGone, c1.LastClose());

            mortal.Health = 10;
            p2.Health = 0;
            DialogueService.Tick(p2);
            Assert.Equal(DialogueCloseReason.PlayerDied, c2.LastClose());
        }
    }
}
