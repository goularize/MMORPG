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

        [Tooltip("At most this many quests are listed; the rest are summed up in a \"+N more\" line.")]
        [SerializeField] private int maxQuests = 3;

        [Header("Opacity")]
        [Tooltip("Opacity while the mouse is not over the tracker.")]
        [Range(0f, 1f)] [SerializeField] private float idleOpacity = 0.7f;
        [Tooltip("Opacity while the mouse is over the tracker.")]
        [Range(0f, 1f)] [SerializeField] private float hoverOpacity = 1f;
        [Tooltip("How fast the opacity changes (per second). 0 changes it instantly.")]
        [SerializeField] private float fadeSpeed = 10f;

        private CanvasGroup _group;
        private RectTransform _rect;
        private Canvas _canvas;

        // Live values for the Inspector while playing; they are overwritten on every refresh, so editing them does nothing
        [Header("Runtime info (read-only)")]
        [SerializeField] private int questsInProgress;
        [SerializeField] private int questsTracked;
        [SerializeField] private int questsShown;
        [SerializeField] private int questsHidden;

        private void Start()
        {
            _rect = root.GetComponent<RectTransform>();
            _canvas = root.GetComponentInParent<Canvas>();
            _group = root.GetComponent<CanvasGroup>();
            if (_group == null) _group = root.AddComponent<CanvasGroup>();

            // The tracker must never catch clicks meant for the world
            _group.blocksRaycasts = false;
            _group.alpha = idleOpacity;

            QuestHandler.OnQuestChanged += OnQuestChanged;
            QuestHandler.OnLogSynced += Refresh;
            QuestHandler.OnTrackingChanged += Refresh;

            Refresh();
        }

        private void OnDestroy()
        {
            QuestHandler.OnQuestChanged -= OnQuestChanged;
            QuestHandler.OnLogSynced -= Refresh;
            QuestHandler.OnTrackingChanged -= Refresh;
        }

        private void Update()
        {
            if (_group == null || !root.activeSelf) return;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool hovered = false;
            if (mouse != null)
            {
                // Overlay canvases take no camera; checking the rectangle keeps working with Raycast Target off
                Camera camera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
                hovered = RectTransformUtility.RectangleContainsScreenPoint(_rect, mouse.position.ReadValue(), camera);
            }

            float target = hovered ? hoverOpacity : idleOpacity;
            _group.alpha = fadeSpeed <= 0f
                ? target
                : Mathf.MoveTowards(_group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
        }

        private void OnQuestChanged(int questId, QuestView quest)
        {
            Refresh();
        }

        private void Refresh()
        {
            questsInProgress = QuestHandler.Quests.Count(q => q.State is QuestState.Active or QuestState.ReadyToTurnIn);

            // Only the quests the player tracks (all of them by default), the latest tracked on top
            var tracked = QuestHandler.Quests
                .Where(q => (q.State is QuestState.Active or QuestState.ReadyToTurnIn) && QuestHandler.IsTracked(q.Id))
                .OrderBy(q => QuestHandler.TrackOrder(q.Id))
                .ToList();

            var shown = tracked.Take(maxQuests).ToList();

            questsTracked = tracked.Count;
            questsShown = shown.Count;
            questsHidden = tracked.Count - shown.Count;

            root.SetActive(tracked.Count > 0);
            if (tracked.Count == 0) return;

            var builder = new StringBuilder();
            foreach (var quest in shown)
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
            if (questsHidden > 0) builder.AppendLine($"<i>+{questsHidden} more</i>");

            text.text = builder.ToString().TrimEnd();
        }
    }
}
