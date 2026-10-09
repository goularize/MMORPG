using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data.Models;
using Shared.Constants;
using Shared.Data;
using Shared.Enums;

namespace Server.Data
{
    // Dialogues.json, Quests.json and the conditions they share
    public static partial class DataValidator
    {
        private sealed class Refs
        {
            public required HashSet<int> NpcIds { get; init; }
            public required HashSet<int> ItemIds { get; init; }
            public required HashSet<int> QuestIds { get; init; }
            public required Dictionary<int, ItemTemplate> ItemsById { get; init; }
        }

        private static void ValidateDialoguesAndQuests(
            List<string> errors,
            List<string> warnings,
            IReadOnlyList<DialogueTemplate> dialogues,
            IReadOnlyList<QuestTemplate> quests,
            IReadOnlyList<NpcTemplate> npcs,
            IReadOnlyList<ItemTemplate> items)
        {
            ReportDuplicates(errors, "Quests.json", "Id", quests.Select(q => q.Id));
            foreach (var group in dialogues.GroupBy(d => d.Id).Where(g => g.Count() > 1))
                errors.Add($"Dialogues.json: Id '{group.Key}' is defined {group.Count()} times.");

            var refs = new Refs
            {
                NpcIds = npcs.Select(n => n.TemplateId).ToHashSet(),
                ItemIds = items.Select(i => i.TemplateId).ToHashSet(),
                QuestIds = quests.Select(q => q.Id).ToHashSet(),
                ItemsById = items.GroupBy(i => i.TemplateId).ToDictionary(g => g.Key, g => g.First())
            };
            var dialogueIds = dialogues.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            var npcsById = npcs.GroupBy(n => n.TemplateId).ToDictionary(g => g.Key, g => g.First());

            foreach (var npc in npcs)
            {
                if (!string.IsNullOrEmpty(npc.DialogueId) && !dialogueIds.Contains(npc.DialogueId))
                    errors.Add($"Npcs.json npc {npc.TemplateId}: DialogueId '{npc.DialogueId}' is not in Dialogues.json.");
            }

            var usedDialogues = npcs.Where(n => !string.IsNullOrEmpty(n.DialogueId)).Select(n => n.DialogueId).ToHashSet(StringComparer.Ordinal);
            foreach (var dialogue in dialogues)
            {
                ValidateDialogue(errors, dialogue, refs);
                if (!string.IsNullOrEmpty(dialogue.Id) && !usedDialogues.Contains(dialogue.Id))
                    warnings.Add($"Dialogues.json dialogue '{dialogue.Id}': no NPC uses it.");
            }

            foreach (var quest in quests) ValidateQuest(errors, quest, refs, npcsById);
        }

        // ---- Conditions ----

        private static void ValidateConditions(List<string> errors, string at, IReadOnlyList<Condition>? conditions, Refs refs, int? ownQuestId = null)
        {
            if (conditions == null) return;

            for (int i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i];
                string where = $"{at}, condition #{i + 1}";
                if (c == null) { errors.Add($"{where}: is null."); continue; }

                switch (c.Type)
                {
                    case ConditionType.KillCount:
                        if (!refs.NpcIds.Contains(c.Id)) errors.Add($"{where} (KillCount): references unknown NPC template {c.Id}.");
                        if (c.Min < 1) errors.Add($"{where} (KillCount): Min must be at least 1.");
                        break;
                    case ConditionType.QuestState:
                        if (!refs.QuestIds.Contains(c.Id)) errors.Add($"{where} (QuestState): references unknown quest {c.Id}.");
                        if (ownQuestId == c.Id) errors.Add($"{where} (QuestState): a quest cannot depend on itself.");
                        if (!Enum.IsDefined(c.State)) errors.Add($"{where} (QuestState): State {(int)c.State} is not a quest state.");
                        break;
                    case ConditionType.Flag:
                        var flagError = ProgressRules.ValidateFlag(c.Name);
                        if (flagError != null) errors.Add($"{where} (Flag): {flagError}");
                        break;
                    case ConditionType.Level:
                        if (c.Min < 1) errors.Add($"{where} (Level): Min must be at least 1.");
                        if (c.Max < 0 || (c.Max > 0 && c.Max < c.Min)) errors.Add($"{where} (Level): Max must be 0 (no limit) or at least Min.");
                        break;
                    case ConditionType.HasItem:
                        if (!refs.ItemIds.Contains(c.Id)) errors.Add($"{where} (HasItem): references unknown item {c.Id}.");
                        if (c.Min < 1) errors.Add($"{where} (HasItem): Min must be at least 1.");
                        break;
                    default:
                        errors.Add($"{where}: Type is missing or Unknown.");
                        break;
                }
            }
        }

        // ---- Dialogues ----

        private static void ValidateDialogue(List<string> errors, DialogueTemplate d, Refs refs)
        {
            string at = $"Dialogues.json dialogue '{d.Id}'";

            if (string.IsNullOrEmpty(d.Id) || d.Id.Length > DialogueRules.MaxIdLength)
                errors.Add($"Dialogues.json: a dialogue Id must be 1-{DialogueRules.MaxIdLength} characters (got '{d.Id}').");
            else if (ProgressRules.ValidateFlag(d.Id) != null)
                errors.Add($"{at}: Id may only contain letters, digits, '_', '-' and '.'.");

            d.Greetings ??= new List<DialogueGreeting>();
            d.Nodes ??= new Dictionary<string, DialogueScreen>();

            if (d.Greetings.Count == 0)
                errors.Add($"{at}: has no greetings.");
            else if (!d.Greetings.Any(g => g.Conditions == null || g.Conditions.Count == 0))
                errors.Add($"{at}: needs a greeting without conditions as the fallback, otherwise some players would get no greeting.");

            foreach (var group in d.Greetings.GroupBy(g => g.Priority).Where(g => g.Count() > 1))
                errors.Add($"{at}: Priority {group.Key} is used by {group.Count()} greetings; priorities must be unique.");

            for (int i = 0; i < d.Greetings.Count; i++)
            {
                var greeting = d.Greetings[i];
                string where = $"{at} greeting #{i + 1} (Priority {greeting.Priority})";
                ValidateConditions(errors, where, greeting.Conditions, refs);
                ValidateScreen(errors, where, greeting, d, refs);
            }

            foreach (var (name, node) in d.Nodes)
            {
                string where = $"{at} node '{name}'";
                if (string.IsNullOrEmpty(name) || name.StartsWith('$') || name.StartsWith(DialogueRules.GeneratedNodePrefix, StringComparison.Ordinal))
                    errors.Add($"{where}: node names cannot be empty or start with '$' or '{DialogueRules.GeneratedNodePrefix}'.");
                if (node == null) { errors.Add($"{where}: is null."); continue; }
                ValidateScreen(errors, where, node, d, refs);
            }

            ValidateCanFinish(errors, at, d);
        }

        private static void ValidateScreen(List<string> errors, string at, DialogueScreen screen, DialogueTemplate d, Refs refs)
        {
            var textError = DialogueRules.ValidateText(screen.Text, DialogueRules.MaxTextLength);
            if (textError != null) errors.Add($"{at}: {textError}");

            screen.Options ??= new List<DialogueOption>();
            if (screen.Options.Count == 0) errors.Add($"{at}: has no options.");
            if (screen.Options.Count > DialogueRules.MaxAuthoredOptions)
                errors.Add($"{at}: has {screen.Options.Count} options, the maximum is {DialogueRules.MaxAuthoredOptions}.");

            for (int i = 0; i < screen.Options.Count; i++)
            {
                var option = screen.Options[i];
                string where = $"{at} option #{i + 1}";
                if (option == null) { errors.Add($"{where}: is null."); continue; }

                var labelError = DialogueRules.ValidateText(option.Label, DialogueRules.MaxLabelLength);
                if (labelError != null) errors.Add($"{where} label: {labelError}");

                ValidateConditions(errors, where, option.Conditions, refs);
                ValidateOptionAction(errors, where, option, d, refs);
            }
        }

        private static void ValidateOptionAction(List<string> errors, string at, DialogueOption option, DialogueTemplate d, Refs refs)
        {
            switch (option.Action)
            {
                case InteractAction.None:
                    errors.Add($"{at}: Action is missing or None.");
                    break;
                case InteractAction.Close:
                    if (option.Node != null) errors.Add($"{at}: a Close option ends the conversation and cannot have a Node.");
                    break;
                case InteractAction.GotoNode:
                    if (option.Node == null) errors.Add($"{at}: GotoNode needs a Node.");
                    break;
                case InteractAction.SetFlag:
                case InteractAction.ClearFlag:
                    var flagError = ProgressRules.ValidateFlag(option.Name);
                    if (flagError != null) errors.Add($"{at} ({option.Action}): {flagError}");
                    break;
                case InteractAction.StartQuest:
                case InteractAction.TurnInQuest:
                    if (!refs.QuestIds.Contains(option.Value)) errors.Add($"{at} ({option.Action}): references unknown quest {option.Value}.");
                    break;
                case InteractAction.BindPoint:
                case InteractAction.OpenShop:
                    break;
                default:
                    errors.Add($"{at}: Action {(int)option.Action} is not a known action.");
                    break;
            }

            if (option.Node != null && option.Node != DialogueRules.GreetingNode && !d.Nodes.ContainsKey(option.Node))
                errors.Add($"{at}: Node '{option.Node}' does not exist (use a node of this dialogue or '{DialogueRules.GreetingNode}').");
        }

        /// <summary>Every screen must be able to reach an option that ends the conversation, or the player would be stuck in it.</summary>
        private static void ValidateCanFinish(List<string> errors, string at, DialogueTemplate d)
        {
            // A screen can finish when an option ends the conversation, or leads to a screen that can finish.
            var finishing = new HashSet<string>(StringComparer.Ordinal);
            bool greetingFinishes = false;

            bool Ends(DialogueOption? o) => o != null && (o.Action == InteractAction.Close || (o.Action != InteractAction.GotoNode && o.Node == null));
            bool CanFinish(DialogueScreen s) => s.Options.Any(o => Ends(o)
                || (o != null && o.Node != null && (o.Node == DialogueRules.GreetingNode ? greetingFinishes : finishing.Contains(o.Node))));

            bool changed = true;
            while (changed)
            {
                changed = false;
                if (!greetingFinishes && d.Greetings.Any(g => g.Options != null && CanFinish(g)))
                {
                    greetingFinishes = true;
                    changed = true;
                }
                foreach (var (name, node) in d.Nodes)
                {
                    if (node?.Options != null && !finishing.Contains(name) && CanFinish(node))
                    {
                        finishing.Add(name);
                        changed = true;
                    }
                }
            }

            for (int i = 0; i < d.Greetings.Count; i++)
            {
                var g = d.Greetings[i];
                if (g.Options != null && g.Options.Count > 0 && !CanFinish(g))
                    errors.Add($"{at} greeting #{i + 1}: no option leads to an end of the conversation (add a Close option).");
            }
            foreach (var (name, node) in d.Nodes)
            {
                if (node?.Options != null && node.Options.Count > 0 && !finishing.Contains(name))
                    errors.Add($"{at} node '{name}': no option leads to an end of the conversation (add a Close option).");
            }
        }

        // ---- Quests ----

        private static void ValidateQuest(List<string> errors, QuestTemplate q, Refs refs, Dictionary<int, NpcTemplate> npcsById)
        {
            string at = $"Quests.json quest {q.Id}";
            if (q.Id <= 0) errors.Add($"{at}: Id must be positive.");
            if (string.IsNullOrWhiteSpace(q.Name)) errors.Add($"{at}: Name is empty.");
            if (q.MinLevel < 1) errors.Add($"{at}: MinLevel must be at least 1.");

            foreach (var (role, npcId) in new[] { ("GiverNpcTemplateId", q.GiverNpcTemplateId), ("TurnInNpcTemplateId", q.TurnInNpcTemplateId) })
            {
                if (!npcsById.TryGetValue(npcId, out var npc))
                    errors.Add($"{at}: {role} references unknown NPC template {npcId}.");
                else if (string.IsNullOrEmpty(npc.DialogueId))
                    errors.Add($"{at}: {role} {npcId} ('{npc.Name}') has no DialogueId, so players could not talk to it about the quest.");
            }

            ValidateConditions(errors, at + " prerequisites", q.Prerequisites, refs, q.Id);

            q.Objectives ??= new List<QuestObjective>();
            if (q.Objectives.Count == 0) errors.Add($"{at}: has no objectives.");
            if (q.Objectives.Count > 6) errors.Add($"{at}: has {q.Objectives.Count} objectives, the maximum is 6.");
            for (int i = 0; i < q.Objectives.Count; i++)
            {
                var o = q.Objectives[i];
                string where = $"{at} objective #{i + 1}";
                if (o == null) { errors.Add($"{where}: is null."); continue; }
                if (o.Count < 1) errors.Add($"{where}: Count must be at least 1.");
                switch (o.Type)
                {
                    case QuestObjectiveType.Kill:
                    case QuestObjectiveType.Talk:
                        if (!refs.NpcIds.Contains(o.Id)) errors.Add($"{where} ({o.Type}): references unknown NPC template {o.Id}.");
                        break;
                    case QuestObjectiveType.Collect:
                        if (!refs.ItemIds.Contains(o.Id)) errors.Add($"{where} (Collect): references unknown item {o.Id}.");
                        break;
                    default:
                        errors.Add($"{where}: Type is missing or Unknown.");
                        break;
                }
                if (o.Description != null && o.Description.Length > DialogueRules.MaxLabelLength)
                    errors.Add($"{where}: Description is longer than {DialogueRules.MaxLabelLength} characters.");
            }

            q.Rewards ??= new QuestRewards();
            if (q.Rewards.Exp < 0) errors.Add($"{at}: Rewards.Exp cannot be negative.");
            if (q.Rewards.Gold < 0) errors.Add($"{at}: Rewards.Gold cannot be negative.");
            foreach (var reward in q.Rewards.Items ?? new List<QuestRewardItem>())
            {
                if (!refs.ItemsById.TryGetValue(reward.ItemTemplateId, out var item))
                    errors.Add($"{at}: reward references unknown item {reward.ItemTemplateId}.");
                else if (reward.Quantity < 1 || reward.Quantity > Math.Max(1, item.MaxStack))
                    errors.Add($"{at}: reward item {reward.ItemTemplateId} quantity must be between 1 and its MaxStack ({item.MaxStack}).");
            }

            q.Text ??= new QuestText();
            foreach (var (name, text) in new[] { ("Offer", q.Text.Offer), ("Progress", q.Text.Progress), ("Complete", q.Text.Complete) })
            {
                var textError = DialogueRules.ValidateText(text, DialogueRules.MaxTextLength);
                if (textError != null) errors.Add($"{at} Text.{name}: {textError}");
            }
        }
    }
}
