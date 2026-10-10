using UnityEngine;

namespace Client.UI
{
    /// <summary>
    /// Fades a UI object: partly transparent until the mouse is over it, fully opaque while it is. Add it to the root
    /// of a HUD window (it adds a CanvasGroup). The hover test uses the object's screen rectangle, so it also works
    /// when its graphics have Raycast Target off. The values are read every frame and can be tuned while playing.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class HoverFade : MonoBehaviour
    {
        [Tooltip("Opacity while the mouse is not over the object.")]
        [Range(0f, 1f)] [SerializeField] private float idleOpacity = 0.7f;
        [Tooltip("Opacity while the mouse is over the object.")]
        [Range(0f, 1f)] [SerializeField] private float hoverOpacity = 1f;
        [Tooltip("How fast the opacity changes (per second). 0 changes it instantly.")]
        [SerializeField] private float fadeSpeed = 10f;
        [Tooltip("Stay fully opaque while the player is typing in a text field (the chat input, for example).")]
        [SerializeField] private bool opaqueWhileTyping = true;

        private CanvasGroup _group;
        private RectTransform _rect;
        private Canvas _canvas;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _rect = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            _group.alpha = idleOpacity;
        }

        private void Update()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool hovered = false;
            if (mouse != null)
            {
                // Overlay canvases take no camera
                Camera camera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;
                hovered = RectTransformUtility.RectangleContainsScreenPoint(_rect, mouse.position.ReadValue(), camera);
            }

            bool opaque = hovered || (opaqueWhileTyping && InputFocus.IsTyping);
            float target = opaque ? hoverOpacity : idleOpacity;

            _group.alpha = fadeSpeed <= 0f
                ? target
                : Mathf.MoveTowards(_group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
        }
    }
}
