using UnityEngine;
using TMPro;
using Client.Network.Handlers;

namespace Client.UI
{
    /// <summary>
    /// The item tooltip: a panel with one rich text that follows the mouse while it is shown. Put the panel under the
    /// Canvas (it should not block the mouse: no Raycast Target on its graphics) and give it a Content Size Fitter so it
    /// grows with the text. Hidden at start.
    /// </summary>
    public class ItemTooltipUI : MonoBehaviour
    {
        public static ItemTooltipUI Instance { get; private set; }

        [SerializeField] private RectTransform root;
        [SerializeField] private TMP_Text text;
        [Tooltip("Distance from the mouse pointer, in pixels.")]
        [SerializeField] private Vector2 offset = new Vector2(18f, -18f);

        private Canvas _canvas;
        private RectTransform _parent;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _canvas = root.GetComponentInParent<Canvas>().rootCanvas;
            _parent = (RectTransform)root.parent;
            root.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (root.gameObject.activeSelf) Follow();
        }

        public void Show(ItemView item)
        {
            text.text = ItemFormat.Tooltip(item, ProgressionHandler.Level);
            root.gameObject.SetActive(true);
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(root); // the size is only right after a rebuild
            Follow();
        }

        public void Hide()
        {
            root.gameObject.SetActive(false);
        }

        private void Follow()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;

            Vector2 screen = mouse.position.ReadValue() + offset;
            var camera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screen, camera, out var local)) return;

            // Keep it inside the parent: flip to the other side of the pointer when it would leave
            Rect bounds = _parent.rect;
            Vector2 size = root.rect.size;
            Vector2 pivot = root.pivot;

            float left = local.x - size.x * pivot.x;
            float top = local.y + size.y * (1f - pivot.y);
            if (left + size.x > bounds.xMax) local.x -= size.x + offset.x * 2f;
            if (top - size.y < bounds.yMin) local.y += size.y - offset.y * 2f;

            root.localPosition = local;
        }
    }
}
