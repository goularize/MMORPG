using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Server.Persistence;
using Shared.Constants;
using Shared.Enums;

namespace Server.World.Entities
{
    /// <summary>A quest log entry: its state and the objective counters (in the order of the quest's objectives).</summary>
    public sealed record QuestProgress(QuestState State, IReadOnlyList<int> Objectives);

    /// <summary>
    /// What a character has done, as far as NPC dialogue and quests care: story flags, kills per NPC template and the
    /// quest log. Owned by <see cref="Player"/> and, like its inventory, only touched by the game thread (no locks).
    /// Every change is queued to <see cref="PersistenceService"/> as a snapshot; <see cref="Load"/> fills it from the
    /// database before the player is posted to the game thread.
    /// </summary>
    public sealed class PlayerProgress
    {
        private readonly int _characterId;
        private readonly PersistenceService? _persistence;

        private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
        private readonly Dictionary<int, int> _killCounts = new();
        private readonly Dictionary<int, QuestProgress> _quests = new();

        /// <param name="persistence">Where changes are queued; null keeps the progress in memory only.</param>
        public PlayerProgress(int characterId, PersistenceService? persistence)
        {
            _characterId = characterId;
            _persistence = persistence;
        }

        // ---- Flags ----

        public IReadOnlyCollection<string> Flags => _flags;

        public bool HasFlag(string flag) => _flags.Contains(flag);

        /// <summary>Sets a flag. Returns true when it was not set before.</summary>
        /// <exception cref="ArgumentException">The name is not a valid flag (see <see cref="ProgressRules.ValidateFlag"/>).</exception>
        public bool SetFlag(string flag)
        {
            RequireValidFlag(flag);
            if (!_flags.Add(flag)) return false;

            _persistence?.QueueFlag(_characterId, flag, isSet: true);
            return true;
        }

        /// <summary>Clears a flag. Returns true when it had been set.</summary>
        public bool ClearFlag(string flag)
        {
            RequireValidFlag(flag);
            if (!_flags.Remove(flag)) return false;

            _persistence?.QueueFlag(_characterId, flag, isSet: false);
            return true;
        }

        private static void RequireValidFlag(string flag)
        {
            string? error = ProgressRules.ValidateFlag(flag);
            if (error != null) throw new ArgumentException(error, nameof(flag));
        }

        // ---- Kill counts ----

        public int GetKillCount(int npcTemplateId) => _killCounts.TryGetValue(npcTemplateId, out int count) ? count : 0;

        /// <summary>Counts one more kill of the template and returns the new total.</summary>
        public int AddKill(int npcTemplateId)
        {
            int count = GetKillCount(npcTemplateId);
            if (count < int.MaxValue) count++;

            _killCounts[npcTemplateId] = count;
            _persistence?.QueueKillCount(_characterId, npcTemplateId, count);
            return count;
        }

        // ---- Quest log ----

        public IReadOnlyDictionary<int, QuestProgress> Quests => _quests;

        public QuestProgress? GetQuest(int questId) => _quests.TryGetValue(questId, out var quest) ? quest : null;

        /// <summary>The quest's state; <see cref="QuestState.Available"/> when the quest is not in the log.</summary>
        public QuestState GetQuestState(int questId) => _quests.TryGetValue(questId, out var quest) ? quest.State : QuestState.Available;

        /// <summary>
        /// Writes (or replaces) a quest log entry. Quest changes are durable events, so they are flushed promptly.
        /// Setting <see cref="QuestState.Available"/> removes the entry.
        /// </summary>
        public void SetQuest(int questId, QuestState state, IEnumerable<int>? objectives = null)
        {
            if (state == QuestState.Available)
            {
                RemoveQuest(questId);
                return;
            }

            var counters = (objectives ?? Array.Empty<int>()).ToArray();
            _quests[questId] = new QuestProgress(state, counters);
            _persistence?.QueueQuest(_characterId, questId, state, SerializeObjectives(counters), urgent: true);
        }

        /// <summary>Removes a quest from the log (abandon). Returns true when it was in the log.</summary>
        public bool RemoveQuest(int questId)
        {
            if (!_quests.Remove(questId)) return false;

            _persistence?.QueueQuestDelete(_characterId, questId, urgent: true);
            return true;
        }

        // ---- Hydration (not persisted again) ----

        /// <summary>Fills the progress from stored rows. Call before the player is visible to the game thread.</summary>
        public void Load(IEnumerable<string> flags, IEnumerable<KeyValuePair<int, int>> killCounts, IEnumerable<(int QuestId, QuestState State, string ProgressJson)> quests)
        {
            foreach (string flag in flags) _flags.Add(flag);
            foreach (var kill in killCounts) _killCounts[kill.Key] = Math.Max(0, kill.Value);
            foreach (var quest in quests)
            {
                if (quest.State == QuestState.Available) continue; // never stored; ignore corrupt rows
                _quests[quest.QuestId] = new QuestProgress(quest.State, ParseObjectives(quest.ProgressJson));
            }
        }

        public static string SerializeObjectives(IReadOnlyCollection<int> objectives) => JsonSerializer.Serialize(objectives);

        /// <summary>Reads stored objective counters; a malformed value yields no counters instead of failing the character load.</summary>
        public static int[] ParseObjectives(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<int>();

            try
            {
                return JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>();
            }
            catch (JsonException)
            {
                return Array.Empty<int>();
            }
        }
    }
}
