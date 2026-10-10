using UnityEngine;
using TMPro;
using Client.Network.Handlers;
using Shared.Enums;

namespace Client.World
{
    /// <summary>
    /// Draws the "!" / "?" over NPCs. It listens to what the server says (MarkerHandler) and puts a marker object
    /// above the NPC's head, removing it when the server clears it (the marker goes away with the NPC when it despawns).
    /// By default the markers are text; assign a sprite to use art instead. Place one in the game scene.
    /// </summary>
    public class QuestMarkerController : MonoBehaviour
    {
        public static QuestMarkerController Instance { get; private set; }

        [Header("Position")]
        [Tooltip("Height above the NPC's origin, in world units.")]
        [SerializeField] private float height = 2.1f;
        [SerializeField] private float bobHeight = 0.08f;
        [SerializeField] private float bobSpeed = 3f;
        [Tooltip("Above the NPC's sprite (5) and its name (10).")]
        [SerializeField] private int sortingOrder = 12;

        [Header("Quest available (!)")]
        [SerializeField] private Color availableColor = new Color(1f, 0.92f, 0.25f, 1f);
        [Tooltip("Optional. When set, this sprite is shown instead of the text.")]
        [SerializeField] private Sprite availableSprite;

        [Header("Quest ready to turn in (?)")]
        [SerializeField] private Color readyColor = new Color(1f, 0.78f, 0.1f, 1f);
        [Tooltip("Optional. When set, this sprite is shown instead of the text.")]
        [SerializeField] private Sprite readySprite;

        [Header("Text style (used when there is no sprite)")]
        [SerializeField] private float fontSize = 7f;
        [SerializeField] private Color outlineColor = new Color(0.1f, 0.05f, 0f, 1f);
        [Range(0f, 1f)] [SerializeField] private float outlineWidth = 0.25f;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            MarkerHandler.OnMarkerChanged += OnMarkerChanged;
        }

        private void OnDestroy()
        {
            MarkerHandler.OnMarkerChanged -= OnMarkerChanged;
            if (Instance == this) Instance = null;
        }

        private void OnMarkerChanged(int entityId, EntityMarker marker)
        {
            var entity = GameManager.Instance != null ? GameManager.Instance.GetEntity(entityId) : null;
            if (entity == null) return; // not spawned here (the server sends the marker right after the spawn)

            // Rebuilt on every change: markers are rare and this keeps text and sprite cases the same
            var existing = entity.transform.Find(QuestMarkerView.ObjectName);
            if (existing != null) Destroy(existing.gameObject);

            if (marker != EntityMarker.None) Create(entity.transform, marker);
        }

        private void Create(Transform parent, EntityMarker marker)
        {
            bool ready = marker == EntityMarker.QuestReady;
            Color color = ready ? readyColor : availableColor;
            Sprite sprite = ready ? readySprite : availableSprite;

            var go = new GameObject(QuestMarkerView.ObjectName);
            go.transform.SetParent(parent, false);

            if (sprite != null)
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = Color.white;
                renderer.sortingOrder = sortingOrder;
            }
            else
            {
                var text = go.AddComponent<TextMeshPro>();
                text.text = ready ? "?" : "!";
                text.fontSize = fontSize;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.color = color;
                text.outlineColor = outlineColor;
                text.outlineWidth = outlineWidth;
                text.sortingOrder = sortingOrder;
                text.rectTransform.sizeDelta = new Vector2(2f, 2f);
            }

            go.AddComponent<QuestMarkerView>().Setup(new Vector3(0f, height, 0f), bobHeight, bobSpeed);
        }
    }
}
