using System;
using System.Collections.Generic;
using System.Linq;
using Shared.Enums;
using Shared.Network;

namespace Client.Network.Handlers
{
    /// <summary>One objective line of a quest as the server describes it, e.g. "Defeat Slime" 3/10.</summary>
    public readonly struct QuestObjectiveView
    {
        public readonly string Text;
        public readonly int Current;
        public readonly int Required;

        public QuestObjectiveView(string text, int current, int required)
        {
            Text = text;
            Current = current;
            Required = required;
        }

        public bool IsDone => Current >= Required;
    }

    /// <summary>A quest of the character's log. Accepting and turning in happen through dialogue options only.</summary>
    public sealed class QuestView
    {
        public int Id;
        public QuestState State;
        public string Name;
        public QuestObjectiveView[] Objectives;
    }

    /// <summary>
    /// The quest log mirror. The server sends the whole log when the character enters the world (QuestLogSync) and
    /// every change after that (QuestUpdate); the client keeps what it was told and holds no quest rules. The only
    /// request it can make is to abandon a quest.
    /// </summary>
    public static class QuestHandler
    {
        private static readonly Dictionary<int, QuestView> _quests = new();

        /// <summary>The quests in the log, ordered by id.</summary>
        public static IEnumerable<QuestView> Quests => _quests.Values.OrderBy(q => q.Id);

        /// <summary>The quest changed, was added, or left the log (the view is null then).</summary>
        public static event Action<int, QuestView> OnQuestChanged;

        /// <summary>The whole log was replaced (world entry).</summary>
        public static event Action OnLogSynced;

        public static bool TryGet(int questId, out QuestView quest) => _quests.TryGetValue(questId, out quest);

        // The quests shown in the HUD tracker, most recently tracked first. A quest is tracked automatically when it
        // enters the log; the player can untrack it (it stays in the log) and track it again. Only a display
        // preference of this client session: the server does not know about it.
        private static readonly List<int> _tracked = new();
        private static readonly HashSet<int> _untracked = new();

        /// <summary>The tracking changed (a quest was tracked, untracked, or left the log).</summary>
        public static event Action OnTrackingChanged;

        public static bool IsTracked(int questId) => _tracked.Contains(questId);

        /// <summary>Position among the tracked quests (0 = top of the tracker), or int.MaxValue when not tracked.</summary>
        public static int TrackOrder(int questId)
        {
            int index = _tracked.IndexOf(questId);
            return index < 0 ? int.MaxValue : index;
        }

        /// <summary>Stops tracking the quest, or tracks it again (it goes to the top of the tracker).</summary>
        public static void ToggleTracked(int questId)
        {
            if (_tracked.Remove(questId))
            {
                _untracked.Add(questId);
            }
            else
            {
                _untracked.Remove(questId);
                _tracked.Insert(0, questId);
            }
            OnTrackingChanged?.Invoke();
        }

        // A quest that is in progress and was not untracked by the player is tracked; anything else is not
        private static void UpdateTracking(QuestView quest)
        {
            bool inProgress = quest.State is QuestState.Active or QuestState.ReadyToTurnIn;
            if (!inProgress)
            {
                _tracked.Remove(quest.Id);
            }
            else if (!_tracked.Contains(quest.Id) && !_untracked.Contains(quest.Id))
            {
                _tracked.Insert(0, quest.Id);
            }
        }

        /// <summary>Back to an empty log (called when returning to character select).</summary>
        public static void ResetSession()
        {
            _quests.Clear();
            _tracked.Clear();
            _untracked.Clear();
        }

        public static void RequestAbandon(int questId)
        {
            using (Packet packet = new Packet(OpCode.QuestAbandonRequest))
            {
                packet.Write(questId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public static void HandleQuestLogSync(Packet packet)
        {
            _quests.Clear();

            int count = packet.ReadInt();
            for (int i = 0; i < count; i++)
            {
                int questId = packet.ReadInt();
                var quest = ReadEntry(packet, questId);
                if (quest != null) _quests[questId] = quest;
            }

            // Every quest in progress starts tracked (the lowest id on top)
            _tracked.Clear();
            _untracked.Clear();
            foreach (var quest in _quests.Values.OrderByDescending(q => q.Id)) UpdateTracking(quest);

            OnLogSynced?.Invoke();
        }

        public static void HandleQuestUpdate(Packet packet)
        {
            int questId = packet.ReadInt();
            var quest = ReadEntry(packet, questId);

            if (quest == null)
            {
                _quests.Remove(questId);
                _tracked.Remove(questId);
                _untracked.Remove(questId);
            }
            else
            {
                _quests[questId] = quest;
                UpdateTracking(quest);
            }

            OnQuestChanged?.Invoke(questId, quest);
        }

        // State, name, objectives (text, current, required). Available means the quest is not in the log (null).
        private static QuestView ReadEntry(Packet packet, int questId)
        {
            var state = (QuestState)packet.ReadByte();
            string name = packet.ReadString();

            int count = packet.ReadByte();
            var objectives = new QuestObjectiveView[count];
            for (int i = 0; i < count; i++)
            {
                string text = packet.ReadString();
                int current = packet.ReadInt();
                int required = packet.ReadInt();
                objectives[i] = new QuestObjectiveView(text, current, required);
            }

            if (state == QuestState.Available) return null;

            return new QuestView { Id = questId, State = state, Name = name, Objectives = objectives };
        }
    }
}
