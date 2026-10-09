using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Client.Network.Handlers;
using Shared.Enums;

namespace Client.UI
{
    /// <summary>
    /// NPC conversation window. It only renders what the server sends (DialogueOpen) and answers with the id of an
    /// offered option; the server decides what every option does and when the conversation ends. It also gives the
    /// short feedback for bind point and failed interactions as floating text above the local player.
    /// Put this on an always-active object (the Canvas): the panel itself starts hidden, so it cannot hold the script.
    /// </summary>
    public class DialogueUI : MonoBehaviour
    {
        public static DialogueUI Instance { get; private set; }

        [Serializable]
        public struct ActionIcon
        {
            public InteractAction action;
            public Sprite sprite;
        }

        [Tooltip("The window root. Hidden at start.")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text npcNameText;
        [SerializeField] private TMP_Text bodyText;
        [Tooltip("Parent (e.g. with a Vertical Layout Group) the option buttons are created under.")]
        [SerializeField] private RectTransform optionContainer;
        [SerializeField] private DialogueOptionButton optionButtonPrefab;
        [SerializeField] private Button closeButton;

        [Header("Optional")]
        [Tooltip("Icon shown on an option per action (shop, bind point, quest...). Actions without an entry show none.")]
        [SerializeField] private ActionIcon[] actionIcons;

        [Header("Behaviour")]
        [Tooltip("Seconds to wait for the server's answer to a picked option before the buttons work again.")]
        [SerializeField] private float responseTimeoutSeconds = 10f;

        private readonly List<DialogueOptionButton> _pool = new();
        private bool _pending;
        private float _pendingSince;
        private int _npcId;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            closeButton.onClick.AddListener(CloseByPlayer);
            panel.SetActive(false);

            DialogueHandler.OnDialogueOpened += OnOpened;
            DialogueHandler.OnDialogueClosed += OnClosed;
            DialogueHandler.OnInteractFailed += OnInteractFailed;
            DialogueHandler.OnBindPointSet += OnBindPointSet;

            // Scale With Screen Size keeps the window the same relative size at every resolution (#218)
            var scaler = GetComponentInParent<CanvasScaler>();
            if (scaler != null && scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                Debug.LogWarning("[DialogueUI] The Canvas should use UI Scale Mode = Scale With Screen Size.");
            }
        }

        private void OnDestroy()
        {
            DialogueHandler.OnDialogueOpened -= OnOpened;
            DialogueHandler.OnDialogueClosed -= OnClosed;
            DialogueHandler.OnInteractFailed -= OnInteractFailed;
            DialogueHandler.OnBindPointSet -= OnBindPointSet;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!panel.activeSelf) return;

            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                CloseByPlayer();
                return;
            }

            if (_pending && Time.unscaledTime - _pendingSince > responseTimeoutSeconds)
            {
                Debug.LogWarning("[DialogueUI] The server did not answer the picked option.");
                SetPending(false);
            }
        }

        private void OnOpened(DialogueData data)
        {
            _npcId = data.NpcId;
            npcNameText.text = data.NpcName;
            bodyText.text = data.Text;

            for (int i = 0; i < data.Options.Length; i++)
            {
                if (i == _pool.Count)
                {
                    var created = Instantiate(optionButtonPrefab, optionContainer);
                    created.Button.onClick.AddListener(() => OnOptionClicked(created));
                    _pool.Add(created);
                }

                var option = data.Options[i];
                _pool[i].Setup(option.Label, IconFor(option.Action));
                _pool[i].gameObject.SetActive(true);
            }

            for (int i = data.Options.Length; i < _pool.Count; i++)
            {
                _pool[i].gameObject.SetActive(false);
            }

            panel.SetActive(true);
            SetPending(false);
        }

        private void OnOptionClicked(DialogueOptionButton button)
        {
            var current = DialogueHandler.Current;
            if (_pending || current == null) return;

            int index = _pool.IndexOf(button);
            if (index < 0 || index >= current.Options.Length) return;

            // Wait for the next DialogueOpen/DialogueClose: a double click must not pick twice
            SetPending(true);
            DialogueHandler.Choose(current.NpcId, current.Options[index].OptionId);
        }

        private void OnClosed(int npcId, DialogueCloseReason reason)
        {
            Hide();

            string message = reason switch
            {
                DialogueCloseReason.TooFar => "You walked away",
                DialogueCloseReason.TargetGone => "They are gone",
                DialogueCloseReason.PlayerDied => null, // the death overlay says it
                DialogueCloseReason.Invalid => "That is no longer possible",
                DialogueCloseReason.InventoryFull => "Your backpack is full",
                DialogueCloseReason.RequirementsNotMet => "You cannot do that right now",
                _ => null
            };
            if (message != null) Say(message);
        }

        private void CloseByPlayer()
        {
            if (!panel.activeSelf) return;

            DialogueHandler.CloseFromClient(_npcId);
            Hide();
        }

        private void Hide()
        {
            panel.SetActive(false);
            SetPending(false);
        }

        private void OnInteractFailed(int targetId, InteractOutcome outcome, InteractAction action)
        {
            switch (outcome)
            {
                case InteractOutcome.TooFar:
                    Say("Too far away");
                    break;
                case InteractOutcome.NotAvailable when action == InteractAction.OpenShop:
                    Say("Shops are not available yet");
                    break;
                case InteractOutcome.NotAvailable:
                    Say("Not available yet");
                    break;
                case InteractOutcome.TargetDead:
                    Say("They cannot talk");
                    break;
                default:
                    // NotFound (left the area of interest) and NothingToDo need no message
                    Debug.Log($"[DialogueUI] Interaction with {targetId} answered {outcome}.");
                    break;
            }
        }

        private void OnBindPointSet(bool success, int mapId, Vector3 position)
        {
            Say(success ? "Bound to this inn" : "You cannot bind here");
        }

        private static void Say(string message)
        {
            var player = Client.World.GameManager.Instance?.GetEntityManager(CharacterHandler.LocalPlayerId);
            if (player != null) player.ShowMessage(message, Color.white);
            else Debug.Log($"[DialogueUI] {message}");
        }

        private Sprite IconFor(InteractAction action)
        {
            if (actionIcons == null) return null;
            foreach (var entry in actionIcons)
            {
                if (entry.action == action) return entry.sprite;
            }
            return null;
        }

        private void SetPending(bool pending)
        {
            _pending = pending;
            _pendingSince = Time.unscaledTime;
            foreach (var button in _pool)
            {
                button.Button.interactable = !pending;
            }
        }
    }
}
