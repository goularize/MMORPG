using UnityEngine;
using TMPro;
using Client.Network.Handlers;
using Client.UI;
using Shared.Network;

namespace Client.World
{
    /// <summary>
    /// Shows Local chat as a speech bubble over the sender's head (the server tells which entity spoke). A new
    /// message replaces the sender's previous bubble. Global and whispers have no bubble. Place one in the game scene.
    /// </summary>
    public class ChatBubbleController : MonoBehaviour
    {
        [Header("Position")]
        [Tooltip("Where the bottom of the bubble is, above the character's origin, in world units.")]
        [SerializeField] private float height = 1.7f;
        [Tooltip("Above the character's sprite and name.")]
        [SerializeField] private int sortingOrder = 20;

        [Header("Look")]
        [SerializeField] private float fontSize = 2.2f;
        [Tooltip("Wrapping width in world units.")]
        [SerializeField] private float maxWidth = 5f;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);
        [Tooltip("Longer messages are cut with an ellipsis.")]
        [SerializeField] private int maxCharacters = 80;

        [Header("Duration")]
        [SerializeField] private float baseSeconds = 3f;
        [Tooltip("Extra time per character, so longer messages stay longer.")]
        [SerializeField] private float secondsPerCharacter = 0.05f;
        [SerializeField] private float maxSeconds = 8f;
        [SerializeField] private float fadeSeconds = 0.6f;

        private void Start()
        {
            ChatHandler.OnMessage += OnMessage;
        }

        private void OnDestroy()
        {
            ChatHandler.OnMessage -= OnMessage;
        }

        private void OnMessage(ChatMessage message)
        {
            if (message.Channel != ChatChannel.Local || message.SenderId == 0) return;

            var entity = GameManager.Instance != null ? GameManager.Instance.GetEntity(message.SenderId) : null;
            if (entity == null) return; // not spawned here

            string text = message.Text;
            if (text.Length > maxCharacters) text = text.Substring(0, maxCharacters).TrimEnd() + "...";

            var existing = entity.transform.Find(ChatBubbleView.ObjectName);
            if (existing != null) Destroy(existing.gameObject);

            Create(entity.transform, text);
        }

        private void Create(Transform parent, string message)
        {
            var go = new GameObject(ChatBubbleView.ObjectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);

            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Bottom;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.color = textColor;
            text.sortingOrder = sortingOrder;

            // Grows upwards from its bottom edge, so a long message never covers the character
            text.rectTransform.pivot = new Vector2(0.5f, 0f);
            text.rectTransform.sizeDelta = new Vector2(maxWidth, 1f);

            // The highlight is the bubble's background; padding spaces keep the text off its edge
            string background = ColorUtility.ToHtmlStringRGBA(backgroundColor);
            text.text = $"<mark=#{background}> {ChatUI.Escape(message)} </mark>";

            float lifetime = Mathf.Min(maxSeconds, baseSeconds + message.Length * secondsPerCharacter);
            go.AddComponent<ChatBubbleView>().Setup(text, lifetime, fadeSeconds);
        }
    }
}
