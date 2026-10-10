using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Client.Network.Handlers;
using Shared.Constants;
using Shared.Network;

namespace Client.UI
{
    /// <summary>
    /// The chat window: one mixed history (colours tell the channels apart) and an input line. Enter focuses the
    /// input, Enter again sends and gives the keyboard back to the game, Esc cancels. Plain text is Local chat;
    /// "/g text" is Global and "/w name text" a whisper. The server filters, limits and routes every message.
    /// Put this on an always-active object (the Canvas).
    /// </summary>
    public class ChatUI : MonoBehaviour
    {
        public static ChatUI Instance { get; private set; }

        [SerializeField] private TMP_Text historyText;
        [SerializeField] private TMP_InputField inputField;
        [Tooltip("Makes the history scrollable (mouse wheel). Optional: without it only the latest 'Max Lines' lines are shown.")]
        [SerializeField] private ScrollRect scrollRect;

        [Header("History")]
        [Tooltip("How many of the latest messages are shown when there is no Scroll Rect. With one, the whole history (200 messages) is kept.")]
        [SerializeField] private int maxLines = 10;

        [Header("Colours")]
        [SerializeField] private Color localColor = Color.white;
        [SerializeField] private Color globalColor = new Color(1f, 0.65f, 0.25f, 1f);
        [SerializeField] private Color whisperColor = new Color(0.9f, 0.5f, 1f, 1f);
        [SerializeField] private Color systemColor = new Color(1f, 0.9f, 0.4f, 1f);

        private readonly StringBuilder _builder = new();
        private int _submitFrame = -1;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            inputField.characterLimit = InputRules.ChatMaxMessageLength;
            inputField.onSubmit.AddListener(OnSubmit);

            ChatHandler.OnMessage += OnMessage;
            Redraw();
        }

        private void OnDestroy()
        {
            ChatHandler.OnMessage -= OnMessage;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return;

            bool typing = InputFocus.IsTyping;

            // The Enter that sent a message must not open the input again in the same frame
            bool enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            if (enter && !typing && Time.frameCount != _submitFrame)
            {
                inputField.ActivateInputField();
            }
            else if (typing && keyboard.escapeKey.wasPressedThisFrame)
            {
                inputField.text = string.Empty;
                Unfocus();
            }
        }

        private void OnSubmit(string text)
        {
            _submitFrame = Time.frameCount;

            if (!ChatHandler.TrySend(text, out string error) && error != null)
            {
                ChatHandler.AddNotice(error);
            }

            inputField.text = string.Empty;
            Unfocus();
        }

        // Back to the game: movement and hotkeys work again
        private void Unfocus()
        {
            inputField.DeactivateInputField();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void OnMessage(ChatMessage message)
        {
            Redraw();
        }

        private void Redraw()
        {
            // Follow the newest message, unless the player scrolled up to read older ones
            bool atBottom = scrollRect == null || IsAtBottom();

            var history = ChatHandler.History;
            int first = scrollRect != null ? 0 : Mathf.Max(0, history.Count - Mathf.Max(1, maxLines));

            _builder.Clear();
            for (int i = first; i < history.Count; i++)
            {
                if (i > first) _builder.Append('\n');
                Append(history[i]);
            }
            historyText.text = _builder.ToString();

            if (scrollRect != null && atBottom)
            {
                // The text's size is only known after a layout pass
                historyText.ForceMeshUpdate();
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private bool IsAtBottom()
        {
            // Everything fits (nothing to scroll), or the view is at its lower end
            bool fits = scrollRect.content.rect.height <= scrollRect.viewport.rect.height + 1f;
            return fits || scrollRect.verticalNormalizedPosition <= 0.01f;
        }

        private void Append(ChatMessage message)
        {
            string line = message.Channel switch
            {
                ChatChannel.Global => $"[Global] {Escape(message.SenderName)}: {Escape(message.Text)}",
                // The echo of a whisper you sent arrives named "To <target>"
                ChatChannel.Whisper => message.SenderName.StartsWith("To ")
                    ? $"{Escape(message.SenderName)}: {Escape(message.Text)}"
                    : $"{Escape(message.SenderName)} whispers: {Escape(message.Text)}",
                ChatChannel.System => $"[System] {Escape(message.Text)}",
                _ => $"{Escape(message.SenderName)}: {Escape(message.Text)}"
            };

            _builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ColorOf(message.Channel))).Append('>')
                .Append(line).Append("</color>");
        }

        private Color ColorOf(ChatChannel channel) => channel switch
        {
            ChatChannel.Global => globalColor,
            ChatChannel.Whisper => whisperColor,
            ChatChannel.System => systemColor,
            _ => localColor
        };

        /// <summary>
        /// Other players' text is shown, never interpreted: a "&lt;" followed by an invisible character is not a rich
        /// text tag, so nobody can inject colours, sizes or links into the history.
        /// </summary>
        public static string Escape(string text) => text.Replace("<", "<​");
    }
}
