using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client.UI
{
    /// <summary>One pooled option line of the dialogue window: a button, its label and an optional action icon.</summary>
    [RequireComponent(typeof(Button))]
    public class DialogueOptionButton : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [Tooltip("Optional. Hidden when there is no sprite for the option's action.")]
        [SerializeField] private Image icon;

        private Button _button;

        /// <summary>
        /// Fetched on demand: a button created under a hidden window has not had its Awake yet when the window code
        /// first uses it.
        /// </summary>
        public Button Button => _button != null ? _button : (_button = GetComponent<Button>());

        public void Setup(string text, Sprite sprite)
        {
            label.text = text;
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.gameObject.SetActive(sprite != null);
            }
        }
    }
}
