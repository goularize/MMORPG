using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Data.Models;
using Server.Handlers;
using Server.Quests;
using Server.World;
using Server.World.Entities;
using Shared.Constants;
using Shared.Enums;
using Shared.Network;

namespace Server.Dialogue
{
    /// <summary>
    /// Runs NPC conversations. Opening one picks the NPC's greeting from the player's progress; every choice is
    /// re-validated against the session (offered option, range, alive, conditions) before its action runs. All on the
    /// game thread (World lane), see docs/npc-dialogue-and-quests.md.
    /// </summary>
    public static class DialogueService
    {
        private const string OfferNodePrefix = DialogueRules.GeneratedNodePrefix + "offer:";
        private const string ProgressNodePrefix = DialogueRules.GeneratedNodePrefix + "progress:";
        private const string CompleteNodePrefix = DialogueRules.GeneratedNodePrefix + "complete:";

        /// <summary>An open conversation is kept until the player is this many times further than the interact range.</summary>
        private const float KeepRangeMultiplier = 1.5f;

        /// <summary>The NPC's dialogue, or null when the template has none.</summary>
        public static DialogueTemplate? GetDialogue(NPC npc) =>
            DataManager.Npcs.TryGetValue(npc.TemplateId, out var template)
            && !string.IsNullOrEmpty(template.DialogueId)
            && DataManager.Dialogues.TryGetValue(template.DialogueId, out var dialogue)
                ? dialogue
                : null;

        /// <summary>Starts a conversation with the NPC (range, AoI and alive checks are done by the caller). False when it has no dialogue.</summary>
        public static bool Open(Player player, NPC npc)
        {
            var dialogue = GetDialogue(npc);
            if (dialogue == null) return false;

            // Quests react to who was talked to, and collect objectives must be current on the screen
            GameEvents.Instance.RaiseNpcTalked(player, npc.TemplateId);
            QuestService.OnInventoryChanged(player);

            player.Dialogue = new DialogueSession(npc.Id, npc.TemplateId, dialogue);
            Show(player, npc, DialogueRules.GreetingNode);
            return true;
        }

        public static void Choose(Player player, int npcId, int optionId)
        {
            var session = player.Dialogue;
            if (session == null || session.NpcId != npcId)
            {
                // Nothing is open with that NPC: tell the client so a stale window closes
                SendClose(player, npcId, DialogueCloseReason.Invalid);
                return;
            }

            if (!session.Offered.TryGetValue(optionId, out var option))
            {
                Close(player, DialogueCloseReason.Invalid);
                return;
            }

            var npc = FindNpc(player, session);
            var reason = CheckStillValid(player, npc);
            if (reason != null)
            {
                Close(player, reason.Value);
                return;
            }

            // Conditions may have changed since the option was shown
            if (!ConditionEvaluator.Evaluate(player, option.Conditions))
            {
                Close(player, DialogueCloseReason.RequirementsNotMet);
                return;
            }

            string? next = option.Node;

            switch (option.Action)
            {
                case InteractAction.Close:
                    Close(player, DialogueCloseReason.Finished);
                    return;

                case InteractAction.GotoNode:
                    break;

                case InteractAction.BindPoint:
                    BindHandler.Bind(player.Connection, player);
                    break;

                case InteractAction.OpenShop:
                    // The vendor system does not exist yet (#151): end the conversation and say so
                    Close(player, DialogueCloseReason.Finished);
                    SendInteractReply(player, npcId, InteractOutcome.NotAvailable, InteractAction.OpenShop);
                    return;

                case InteractAction.SetFlag:
                    player.Progress.SetFlag(option.Name!);
                    break;

                case InteractAction.ClearFlag:
                    player.Progress.ClearFlag(option.Name!);
                    break;

                case InteractAction.StartQuest:
                    if (QuestService.Accept(player, option.Value, session.NpcTemplateId) != QuestResult.Ok)
                    {
                        Close(player, DialogueCloseReason.RequirementsNotMet);
                        return;
                    }
                    break;

                case InteractAction.TurnInQuest:
                    var result = QuestService.TurnIn(player, option.Value, session.NpcTemplateId);
                    if (result != QuestResult.Ok)
                    {
                        Close(player, result == QuestResult.InventoryFull ? DialogueCloseReason.InventoryFull : DialogueCloseReason.RequirementsNotMet);
                        return;
                    }
                    break;

                default:
                    Close(player, DialogueCloseReason.Invalid);
                    return;
            }

            if (next == null)
            {
                Close(player, DialogueCloseReason.Finished);
                return;
            }

            Show(player, npc!, next);
        }

        /// <summary>Ends the player's conversation (if any) and tells the client why.</summary>
        public static void Close(Player player, DialogueCloseReason reason)
        {
            var session = player.Dialogue;
            if (session == null) return;

            player.Dialogue = null;
            SendClose(player, session.NpcId, reason);
        }

        /// <summary>The client closed its dialogue window: forget the session without answering.</summary>
        public static void ClientClosed(Player player, int npcId)
        {
            if (player.Dialogue?.NpcId == npcId) player.Dialogue = null;
        }

        /// <summary>Per tick: ends a conversation whose NPC is gone or far away, or whose player died.</summary>
        public static void Tick(Player player)
        {
            var session = player.Dialogue;
            if (session == null) return;

            var reason = CheckStillValid(player, FindNpc(player, session));
            if (reason != null) Close(player, reason.Value);
        }

        // ---- Screens ----

        private static void Show(Player player, NPC npc, string nodeKey)
        {
            var session = player.Dialogue!;
            var screen = BuildScreen(player, npc, session.Dialogue, nodeKey);
            if (screen == null)
            {
                Close(player, DialogueCloseReason.Invalid);
                return;
            }

            var options = screen.Options
                .Where(o => ConditionEvaluator.Evaluate(player, o.Conditions))
                .Take(DialogueRules.MaxOptionsPerScreen)
                .ToList();

            // A screen whose options are all hidden would trap the player
            if (options.Count == 0) options.Add(new DialogueOption { Label = "Goodbye", Action = InteractAction.Close });

            session.Node = nodeKey;
            session.Offered.Clear();

            using Packet packet = new Packet(OpCode.DialogueOpen);
            packet.Write(npc.Id);
            packet.Write(npc.Name);
            packet.Write(Render(screen.Text, player, npc));
            packet.Write((byte)options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                int optionId = i + 1;
                session.Offered[optionId] = options[i];
                packet.Write(optionId);
                packet.Write(Render(options[i].Label, player, npc));
                packet.Write((byte)options[i].Action);
            }
            player.Connection.Send(packet);
        }

        private static DialogueScreen? BuildScreen(Player player, NPC npc, DialogueTemplate dialogue, string nodeKey)
        {
            if (nodeKey == DialogueRules.GreetingNode) return BuildGreeting(player, npc, dialogue);
            if (nodeKey.StartsWith(DialogueRules.GeneratedNodePrefix, StringComparison.Ordinal)) return BuildQuestScreen(player, nodeKey);
            return dialogue.Nodes.TryGetValue(nodeKey, out var node) ? node : null;
        }

        /// <summary>The highest-priority greeting whose conditions hold, followed by the quest options this NPC has for the player.</summary>
        private static DialogueScreen? BuildGreeting(Player player, NPC npc, DialogueTemplate dialogue)
        {
            var greeting = dialogue.Greetings
                .Where(g => ConditionEvaluator.Evaluate(player, g.Conditions))
                .OrderByDescending(g => g.Priority)
                .FirstOrDefault();
            if (greeting == null) return null;

            var screen = new DialogueScreen { Text = greeting.Text, Options = new List<DialogueOption>(greeting.Options) };
            screen.Options.InsertRange(0, QuestOptions(player, npc.TemplateId));
            return screen;
        }

        private static IEnumerable<DialogueOption> QuestOptions(Player player, int npcTemplateId)
        {
            var quests = DataManager.Quests.Values.OrderBy(q => q.Id).ToList();

            foreach (var quest in quests.Where(q => q.TurnInNpcTemplateId == npcTemplateId))
            {
                var state = player.Progress.GetQuestState(quest.Id);
                if (state == QuestState.ReadyToTurnIn)
                    yield return GoTo($"[Complete] {quest.Name}", CompleteNodePrefix + quest.Id);
                else if (state == QuestState.Active)
                    yield return GoTo($"[In progress] {quest.Name}", ProgressNodePrefix + quest.Id);
            }

            foreach (var quest in quests.Where(q => q.GiverNpcTemplateId == npcTemplateId && QuestService.IsAvailable(player, q)))
                yield return GoTo($"[New] {quest.Name}", OfferNodePrefix + quest.Id);
        }

        private static DialogueOption GoTo(string label, string node) =>
            new() { Label = label, Action = InteractAction.GotoNode, Node = node };

        /// <summary>The offer, progress and complete screens of a quest, built by the server (authored nodes cannot use the "quest:" prefix).</summary>
        private static DialogueScreen? BuildQuestScreen(Player player, string nodeKey)
        {
            string? idText = nodeKey.Split(':').LastOrDefault();
            if (!int.TryParse(idText, out int questId) || !DataManager.Quests.TryGetValue(questId, out var quest)) return null;

            var back = GoTo("Back", DialogueRules.GreetingNode);

            if (nodeKey.StartsWith(OfferNodePrefix, StringComparison.Ordinal))
            {
                if (!QuestService.IsAvailable(player, quest)) return null;
                return new DialogueScreen
                {
                    Text = quest.Text.Offer + RewardLines(quest),
                    Options =
                    {
                        new DialogueOption { Label = "Accept", Action = InteractAction.StartQuest, Value = quest.Id, Node = DialogueRules.GreetingNode },
                        back
                    }
                };
            }

            var entry = player.Progress.GetQuest(questId);
            if (entry == null) return null;
            var objectives = string.Join("\n", QuestService.DescribeObjectives(quest, entry.Objectives));

            if (nodeKey.StartsWith(ProgressNodePrefix, StringComparison.Ordinal) && entry.State == QuestState.Active)
                return new DialogueScreen { Text = quest.Text.Progress + "\n\n" + objectives, Options = { back } };

            if (nodeKey.StartsWith(CompleteNodePrefix, StringComparison.Ordinal) && entry.State == QuestState.ReadyToTurnIn)
            {
                return new DialogueScreen
                {
                    Text = quest.Text.Complete + RewardLines(quest),
                    Options =
                    {
                        new DialogueOption { Label = "Complete quest", Action = InteractAction.TurnInQuest, Value = quest.Id, Node = DialogueRules.GreetingNode },
                        back
                    }
                };
            }

            return null;
        }

        private static string RewardLines(QuestTemplate quest)
        {
            var parts = new List<string>();
            if (quest.Rewards.Exp > 0) parts.Add($"{quest.Rewards.Exp} EXP");
            if (quest.Rewards.Gold > 0) parts.Add($"{quest.Rewards.Gold} gold");
            foreach (var reward in quest.Rewards.Items)
            {
                string name = DataManager.Items.TryGetValue(reward.ItemTemplateId, out var item) ? item.Name : "item";
                parts.Add(reward.Quantity > 1 ? $"{reward.Quantity}x {name}" : name);
            }
            return parts.Count == 0 ? string.Empty : "\n\nReward: " + string.Join(", ", parts);
        }

        private static string Render(string text, Player player, NPC npc) => text
            .Replace("{{" + DialogueRules.TokenPlayerName + "}}", player.Name)
            .Replace("{{" + DialogueRules.TokenPlayerLevel + "}}", player.Level.ToString())
            .Replace("{{" + DialogueRules.TokenNpcName + "}}", npc.Name);

        // ---- Validation and packets ----

        private static NPC? FindNpc(Player player, DialogueSession session) =>
            GameLogic.MapMgr.GetMap(player.MapId)?.GetEntity(session.NpcId) as NPC;

        /// <summary>Null while the conversation can go on, otherwise why it has to end.</summary>
        private static DialogueCloseReason? CheckStillValid(Player player, NPC? npc)
        {
            if (player.Health <= 0) return DialogueCloseReason.PlayerDied;
            if (npc == null || npc.Health <= 0) return DialogueCloseReason.TargetGone;

            float distance = Shared.Math.Vector3.Distance(player.Position, npc.Position);
            if (distance > GameRules.InteractRange * KeepRangeMultiplier) return DialogueCloseReason.TooFar;

            return null;
        }

        private static void SendClose(Player player, int npcId, DialogueCloseReason reason)
        {
            using Packet packet = new Packet(OpCode.DialogueClose);
            packet.Write(npcId);
            packet.Write((byte)reason);
            player.Connection.Send(packet);
        }

        private static void SendInteractReply(Player player, int targetId, InteractOutcome outcome, InteractAction action)
        {
            using Packet packet = new Packet(OpCode.EntityInteractResponse);
            packet.Write(targetId);
            packet.Write((byte)outcome);
            packet.Write((byte)action);
            player.Connection.Send(packet);
        }
    }
}
