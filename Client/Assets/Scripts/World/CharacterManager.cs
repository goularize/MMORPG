using UnityEngine;
using TMPro;

namespace Client.World
{
    public class CharacterManager : MonoBehaviour
    {
        [Header("Visuals")]
        public TextMeshPro nameText;

        public void SetName(string characterName)
        {
            if (nameText != null)
            {
                nameText.text = characterName;
            }
            else
            {
                Debug.LogWarning($"[{gameObject.name}] CharacterManager is missing the NameText reference! Please assign it in the Inspector.");
            }
        }
    }
}
