using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Data.Models;
using Server.Handlers;
using Server.Persistence;
using Server.World;
using Server.World.Entities;
using Shared.Constants;
using Shared.Enums;

namespace Server.Quests
{
    public enum QuestResult
    {
        Ok,
        UnknownQuest,
        WrongNpc,
        NotAvailable,
        LogFull,
        NotInLog,
        NotComplete,
        InventoryFull
    }

    /// <summary>
    /// The quest state machine. A quest not in the log is Available (when the level and prerequisites allow);
    /// accepting puts it in the log as Active; completing every objective makes it ReadyToTurnIn; turning it in grants
    /// the rewards and makes it Rewarded for good. Progress comes from <see cref="GameEvents"/>; everything runs on
    /// the game thread and persists through the write-behind queue (see <see cref="PlayerProgress"/>).
    /// </summary>
    public static class QuestService
    {
        // ---- Queries ----

        /// <summary>True when the player could accept the quest now (ignoring who offers it).</summary>
        public static bool IsAvailable(Player player, QuestTemplate quest) =>
            player.Progress.GetQuest(quest.Id) == null
            && player.Progress.GetQuestState(quest.Id) == QuestState.Available
            && player.Level >= quest.MinLevel
            && ConditionEvaluator.Evaluate(player, quest.Prerequisites);

        public static int ActiveCount(Player player) =>
            player.Progress.Quests.Count(q => q.Value.State is QuestState.Active or QuestState.ReadyToTurnIn);

        /// <summary>The objective lines of a quest for its screen, e.g. "Defeat Slime: 3/10".</summary>
        public static IEnumerable<string> DescribeObjectives(QuestTemplate quest, IReadOnlyList<int> counters)
        {
            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                int done = i < counters.Count ? Math.Min(counters[i], objective.Count) : 0;
                yield return $"{ObjectiveText(objective)}: {done}/{objective.Count}";
            }
        }

        /// <summary>The objective without its counter, e.g. "Defeat Slime"; sent to the client for the quest log.</summary>
        public static string ObjectiveText(QuestObjective objective)
        {
            if (!string.IsNullOrWhiteSpace(objective.Description)) return objective.Description!;

            switch (objective.Type)
            {
                case QuestObjectiveType.Kill:
                    return "Defeat " + (DataManager.Npcs.TryGetValue(objective.Id, out var killed) ? killed.Name : "enemies");
                case QuestObjectiveType.Collect:
                    return "Collect " + (DataManager.Items.TryGetValue(objective.Id, out var item) ? item.Name : "items");
                default:
                    return "Speak with " + (DataManager.Npcs.TryGetValue(objective.Id, out var npc) ? npc.Name : "someone");
            }
        }

        // ---- Commands ----

        public static QuestResult Accept(Player player, int questId, int npcTemplateId)
        {
            if (!DataManager.Quests.TryGetValue(questId, out var quest)) return QuestResult.UnknownQuest;
            if (quest.GiverNpcTemplateId != npcTemplateId) return QuestResult.WrongNpc;
            if (!IsAvailable(player, quest)) return QuestResult.NotAvailable;
            if (ActiveCount(player) >= ProgressRules.MaxActiveQuests) return QuestResult.LogFull;

            // Kills and talks count from now on; items already in the backpack count immediately
            var counters = new int[quest.Objectives.Count];
            RefreshCollectCounters(player, quest, counters);

            Commit(player, quest, counters);
            Console.WriteLine($"[Quest] {player.Name} accepted '{quest.Name}' ({quest.Id}).");
            return QuestResult.Ok;
        }

        public static QuestResult TurnIn(Player player, int questId, int npcTemplateId)
        {
            if (!DataManager.Quests.TryGetValue(questId, out var quest)) return QuestResult.UnknownQuest;
            if (quest.TurnInNpcTemplateId != npcTemplateId) return QuestResult.WrongNpc;

            var entry = player.Progress.GetQuest(questId);
            if (entry == null || entry.State is not (QuestState.Active or QuestState.ReadyToTurnIn)) return QuestResult.NotInLog;

            // The held items are the truth: re-count them instead of trusting the stored counters
            var counters = NormalizedCounters(quest, entry.Objectives);
            RefreshCollectCounters(player, quest, counters);
            if (!AllDone(quest, counters))
            {
                Commit(player, quest, counters);
                return QuestResult.NotComplete;
            }

            var handedIn = quest.Objectives
                .Where(o => o.Type == QuestObjectiveType.Collect)
                .GroupBy(o => o.Id)
                .ToDictionary(g => g.Key, g => g.Sum(o => o.Count));
            var rewardItems = quest.Rewards.Items.Select(r => (r.ItemTemplateId, r.Quantity)).ToList();

            // Nothing is taken or given unless the whole turn-in fits
            if (!InventoryOps.CanFit(player, handedIn, rewardItems)) return QuestResult.InventoryFull;

            // The state change is queued first (without flushing) so the quest can never be rewarded twice, and the
            // hand-in, the rewards and the state reach the database in one batch: the final save below is urgent.
            player.Progress.SetQuest(questId, QuestState.Rewarded, counters, urgent: false);

            foreach (var (itemId, count) in handedIn) InventoryOps.RemoveFromBackpack(player, itemId, count);
            foreach (var (itemId, quantity) in rewardItems) InventoryOps.AddToBackpack(player, itemId, quantity);

            if (quest.Rewards.Gold > 0)
            {
                player.Gold += quest.Rewards.Gold;
                InventoryHandler.SendInventorySync(player); // carries the gold total
            }

            QuestHandler.SendQuestUpdate(player, questId);
            GameEvents.Instance.RaiseQuestChanged(player, questId);
            Console.WriteLine($"[Quest] {player.Name} turned in '{quest.Name}' ({quest.Id}).");

            // AddExp queues an urgent save of the character, which flushes everything queued above together
            if (quest.Rewards.Exp > 0) player.AddExp(quest.Rewards.Exp);
            else player.QueueSave(urgent: true);
            PersistenceService.Instance.Expedite(player.Id);

            return QuestResult.Ok;
        }

        /// <summary>Removes an Active or ReadyToTurnIn quest from the log. A rewarded quest cannot be abandoned.</summary>
        public static bool Abandon(Player player, int questId)
        {
            var entry = player.Progress.GetQuest(questId);
            if (entry == null || entry.State is not (QuestState.Active or QuestState.ReadyToTurnIn)) return false;

            player.Progress.RemoveQuest(questId);
            QuestHandler.SendQuestUpdate(player, questId);
            GameEvents.Instance.RaiseQuestChanged(player, questId);
            return true;
        }

        // ---- Event subscribers (see GameEvents) ----

        public static void OnNpcKilled(Player player, int npcTemplateId) => Advance(player, QuestObjectiveType.Kill, npcTemplateId);

        public static void OnNpcTalked(Player player, int npcTemplateId) => Advance(player, QuestObjectiveType.Talk, npcTemplateId);

        /// <summary>Re-counts collect objectives after the backpack changed (ItemGained / ItemLost, and when a conversation opens).</summary>
        public static void OnInventoryChanged(Player player)
        {
            foreach (var (questId, entry) in player.Progress.Quests.ToList())
            {
                if (entry.State is not (QuestState.Active or QuestState.ReadyToTurnIn)) continue;
                if (!DataManager.Quests.TryGetValue(questId, out var quest)) continue;
                if (!quest.Objectives.Any(o => o.Type == QuestObjectiveType.Collect)) continue;

                var counters = NormalizedCounters(quest, entry.Objectives);
                var before = counters.ToArray();
                RefreshCollectCounters(player, quest, counters);

                if (!counters.SequenceEqual(before) || StateFor(quest, counters) != entry.State)
                    Commit(player, quest, counters);
            }
        }

        private static void Advance(Player player, QuestObjectiveType type, int targetId)
        {
            foreach (var (questId, entry) in player.Progress.Quests.ToList())
            {
                if (entry.State != QuestState.Active) continue;
                if (!DataManager.Quests.TryGetValue(questId, out var quest)) continue;

                var counters = NormalizedCounters(quest, entry.Objectives);
                bool changed = false;
                for (int i = 0; i < quest.Objectives.Count; i++)
                {
                    var objective = quest.Objectives[i];
                    if (objective.Type == type && objective.Id == targetId && counters[i] < objective.Count)
                    {
                        counters[i]++;
                        changed = true;
                    }
                }

                if (changed) Commit(player, quest, counters);
            }
        }

        // ---- Internals ----

        /// <summary>The stored counters, resized to the quest's current objectives (quest data may have changed since they were saved).</summary>
        internal static int[] NormalizedCounters(QuestTemplate quest, IReadOnlyList<int> stored)
        {
            var counters = new int[quest.Objectives.Count];
            for (int i = 0; i < counters.Length && i < stored.Count; i++)
                counters[i] = Math.Clamp(stored[i], 0, quest.Objectives[i].Count);
            return counters;
        }

        private static void RefreshCollectCounters(Player player, QuestTemplate quest, int[] counters)
        {
            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                var objective = quest.Objectives[i];
                if (objective.Type == QuestObjectiveType.Collect)
                    counters[i] = Math.Min(InventoryOps.CountInBackpack(player, objective.Id), objective.Count);
            }
        }

        private static bool AllDone(QuestTemplate quest, int[] counters)
        {
            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                if (counters[i] < quest.Objectives[i].Count) return false;
            }
            return true;
        }

        private static QuestState StateFor(QuestTemplate quest, int[] counters) =>
            AllDone(quest, counters) ? QuestState.ReadyToTurnIn : QuestState.Active;

        /// <summary>Stores the counters with the state they imply, tells the client and raises QuestChanged.</summary>
        private static void Commit(Player player, QuestTemplate quest, int[] counters)
        {
            player.Progress.SetQuest(quest.Id, StateFor(quest, counters), counters);
            QuestHandler.SendQuestUpdate(player, quest.Id);
            GameEvents.Instance.RaiseQuestChanged(player, quest.Id);
        }
    }
}
