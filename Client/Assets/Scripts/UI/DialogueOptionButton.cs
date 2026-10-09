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

        public Button Button { get; private set; }

        private void Awake()
        {
            Button = GetComponent<Button>();
        }

        public void Setup(string text, Sprite sprite)
        {
            if (Button == null) Button = GetComponent<Button>();

            label.text = text;
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.gameObject.SetActive(sprite != null);
            }
        }
    }
}
