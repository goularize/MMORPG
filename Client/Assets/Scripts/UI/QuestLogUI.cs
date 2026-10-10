using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Client.Network.Handlers;
using Shared.Enums;

namespace Client.UI
{
    /// <summary>
    /// The quest log window: the character's quests on the left (in progress first, completed after), the selected
    /// quest's objectives on the right and an Abandon button. It only shows what QuestHandler mirrors from the server;
    /// abandoning asks the server, which answers with a QuestUpdate that removes the quest. Quests are accepted and
    /// turned in through NPC dialogue, never from here.
    /// Put this on an always-active object (the Canvas): the panel itself starts hidden.
    /// </summary>
    public class QuestLogUI : MonoBehaviour
    {
        public static QuestLogUI Instance { get; private set; }

        [Tooltip("The window root. Hidden at start.")]
        [SerializeField] private GameObject panel;
        [Tooltip("Parent (e.g. with a Vertical Layout Group) the quest buttons are created under.")]
        [SerializeField] private RectTransform listContainer;
        [Tooltip("Reuses the dialogue option button: a Button, a label and an optional icon (unused here).")]
        [SerializeField] private DialogueOptionButton questButtonPrefab;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text detailText;
        [Tooltip("Shown instead of the list when the log is empty. Optional.")]
        [SerializeField] private GameObject emptyLabel;
        [Tooltip("Tracks the quest (it goes to the top of the HUD tracker) or stops tracking it. Optional.")]
        [SerializeField] private Button trackButton;
        [SerializeField] private TMP_Text trackLabel;
        [SerializeField] private Button abandonButton;
        [SerializeField] private TMP_Text abandonLabel;
        [SerializeField] private Button closeButton;

        [Header("Input")]
        [SerializeField] private UnityEngine.InputSystem.Key toggleKey = UnityEngine.InputSystem.Key.L;

        private readonly List<DialogueOptionButton> _pool = new();
        private readonly List<int> _listedIds = new();
        private int _selectedId = -1;
        private bool _confirmingAbandon;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            closeButton.onClick.AddListener(Close);
            abandonButton.onClick.AddListener(OnAbandonClicked);
            if (trackButton != null) trackButton.onClick.AddListener(OnTrackClicked);
            panel.SetActive(false);

            QuestHandler.OnQuestChanged += OnQuestChanged;
            QuestHandler.OnLogSynced += Refresh;
            QuestHandler.OnTrackingChanged += ShowDetails;
        }

        private void OnDestroy()
        {
            QuestHandler.OnQuestChanged -= OnQuestChanged;
            QuestHandler.OnLogSynced -= Refresh;
            QuestHandler.OnTrackingChanged -= ShowDetails;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[toggleKey].wasPressedThisFrame && !InputFocus.IsTyping)
            {
                if (panel.activeSelf) Close(); else Open();
            }
            else if (panel.activeSelf && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        public void Open()
        {
            panel.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            panel.SetActive(false);
        }

        private void OnQuestChanged(int questId, QuestView quest)
        {
            if (panel.activeSelf) Refresh();
        }

        private void Refresh()
        {
            if (!panel.activeSelf) return;

            // In progress first (also the ones ready to turn in), completed after
            var quests = QuestHandler.Quests
                .OrderBy(q => q.State == QuestState.Rewarded ? 1 : 0)
                .ThenBy(q => q.Id)
                .ToList();

            _listedIds.Clear();
            for (int i = 0; i < quests.Count; i++)
            {
                if (i == _pool.Count)
                {
                    var created = Instantiate(questButtonPrefab, listContainer);
                    int index = i;
                    created.Button.onClick.AddListener(() => Select(index));
                    _pool.Add(created);
                }

                _pool[i].Setup(ListLabel(quests[i]), null);
                _pool[i].gameObject.SetActive(true);
                _listedIds.Add(quests[i].Id);
            }

            for (int i = quests.Count; i < _pool.Count; i++)
            {
                _pool[i].gameObject.SetActive(false);
            }

            if (emptyLabel != null) emptyLabel.SetActive(quests.Count == 0);

            // Keep the selection while the quest is still there, otherwise pick the first one
            if (!_listedIds.Contains(_selectedId)) _selectedId = _listedIds.Count > 0 ? _listedIds[0] : -1;
            _confirmingAbandon = false;
            ShowDetails();
        }

        private void Select(int index)
        {
            if (index < 0 || index >= _listedIds.Count) return;

            _selectedId = _listedIds[index];
            _confirmingAbandon = false;
            ShowDetails();
        }

        private void ShowDetails()
        {
            if (_selectedId < 0 || !QuestHandler.TryGet(_selectedId, out var quest))
            {
                titleText.text = string.Empty;
                detailText.text = string.Empty;
                abandonButton.gameObject.SetActive(false);
                if (trackButton != null) trackButton.gameObject.SetActive(false);
                return;
            }

            titleText.text = quest.Name;

            var text = new StringBuilder();
            text.AppendLine(StateLine(quest.State));
            text.AppendLine();
            foreach (var objective in quest.Objectives)
            {
                string line = $"{objective.Text}: {Mathf.Min(objective.Current, objective.Required)}/{objective.Required}";
                text.AppendLine(objective.IsDone ? $"<color=#7CFC7C>{line}</color>" : line);
            }
            detailText.text = text.ToString();

            // A rewarded quest is finished for good and cannot be abandoned
            bool canAbandon = quest.State is QuestState.Active or QuestState.ReadyToTurnIn;
            abandonButton.gameObject.SetActive(canAbandon);
            abandonLabel.text = _confirmingAbandon ? "Confirm abandon?" : "Abandon";

            // Only quests in progress can be shown in the tracker
            if (trackButton != null)
            {
                trackButton.gameObject.SetActive(canAbandon);
                if (trackLabel != null) trackLabel.text = QuestHandler.IsTracked(quest.Id) ? "Untrack" : "Track";
            }
        }

        private void OnTrackClicked()
        {
            if (_selectedId >= 0) QuestHandler.ToggleTracked(_selectedId);
        }

        private void OnAbandonClicked()
        {
            if (_selectedId < 0) return;

            // Two clicks: abandoning loses the progress
            if (!_confirmingAbandon)
            {
                _confirmingAbandon = true;
                abandonLabel.text = "Confirm abandon?";
                return;
            }

            _confirmingAbandon = false;
            QuestHandler.RequestAbandon(_selectedId);
        }

        private static string ListLabel(QuestView quest) => quest.State switch
        {
            QuestState.ReadyToTurnIn => $"{quest.Name} (Complete)",
            QuestState.Rewarded => $"{quest.Name} (Done)",
            _ => quest.Name
        };

        private static string StateLine(QuestState state) => state switch
        {
            QuestState.ReadyToTurnIn => "Ready to turn in",
            QuestState.Rewarded => "Completed",
            _ => "In progress"
        };
    }
}
