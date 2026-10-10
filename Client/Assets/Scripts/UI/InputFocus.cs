using TMPro;
using UnityEngine.EventSystems;

namespace Client.UI
{
    /// <summary>Whether the player is typing in a text field. Movement and window hotkeys must not react to the keys then.</summary>
    public static class InputFocus
    {
        public static bool IsTyping
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == null) return false;

                var field = selected.GetComponent<TMP_InputField>();
                return field != null && field.isFocused;
            }
        }
    }
}
