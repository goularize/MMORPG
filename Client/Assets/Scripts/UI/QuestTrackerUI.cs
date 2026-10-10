using System.Linq;
using System.Text;
using UnityEngine;
using TMPro;
using Client.Network.Handlers;
using Shared.Enums;

namespace Client.UI
{
    /// <summary>
    /// HUD list of the quests in progress with their objective counters. It redraws on every change the server
    /// sends and hides itself when there is nothing to track.
    /// </summary>
    public class QuestTrackerUI : MonoBehaviour
    {
        [Tooltip("The tracker root; hidden while no quest is in progress.")]
        [SerializeField] private GameObject root;
        [SerializeField] private TMP_Text text;

        [Tooltip("At most this many quests are listed.")]
        [SerializeField] private int maxQuests = 5;

        private void Start()
        {
            QuestHandler.OnQuestChanged += OnQuestChanged;
            QuestHandler.OnLogSynced += Refresh;

            Refresh();
        }

        private void OnDestroy()
        {
            QuestHandler.OnQuestChanged -= OnQuestChanged;
            QuestHandler.OnLogSynced -= Refresh;
        }

        private void OnQuestChanged(int questId, QuestView quest)
        {
            Refresh();
        }

        private void Refresh()
        {
            var tracked = QuestHandler.Quests
                .Where(q => q.State is QuestState.Active or QuestState.ReadyToTurnIn)
                .Take(maxQuests)
                .ToList();

            root.SetActive(tracked.Count > 0);
            if (tracked.Count == 0) return;

            var builder = new StringBuilder();
            foreach (var quest in tracked)
            {
                if (quest.State == QuestState.ReadyToTurnIn)
                {
                    builder.AppendLine($"<b>{quest.Name}</b> <color=#FFD750>(ready to turn in)</color>");
                    continue;
                }

                builder.AppendLine($"<b>{quest.Name}</b>");
                foreach (var objective in quest.Objectives)
                {
                    string line = $"{objective.Text}: {Mathf.Min(objective.Current, objective.Required)}/{objective.Required}";
                    builder.AppendLine(objective.IsDone ? $"<color=#7CFC7C>{line}</color>" : line);
                }
            }
            text.text = builder.ToString().TrimEnd();
        }
    }
}
