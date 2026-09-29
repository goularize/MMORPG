using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Client.Network;
using Shared.Network;

namespace Client.UI
{
    public class CharacterCreationUI : MonoBehaviour
    {
        public static CharacterCreationUI Instance;

        [Header("Panels")]
        public GameObject selectionPanel;
        public GameObject creationPanel;

        [Header("UI Elements")]
        public TMP_InputField nameInputField;
        public Button confirmButton;
        public Button backButton;
        public TextMeshProUGUI errorText;

        // Mock appearance ID (if you have dropdowns later, you bind them to this)
        private int _selectedAppearanceId = 1; 

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void OnEnable()
        {
            if (nameInputField != null) nameInputField.text = "";
            if (errorText != null) errorText.text = "";
            if (confirmButton != null) confirmButton.interactable = true;
        }

        public void OnConfirmClicked()
        {
            Debug.Log("[CreationUI] Confirm Clicked");
            if (nameInputField == null) return;

            string charName = nameInputField.text.Trim();
            if (string.IsNullOrEmpty(charName))
            {
                if (errorText != null) errorText.text = "Name cannot be empty.";
                return;
            }

            if (confirmButton != null) confirmButton.interactable = false;

            using (Packet packet = new Packet(OpCode.CharacterCreateRequest))
            {
                packet.Write(charName);
                packet.Write(_selectedAppearanceId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public void OnBackClicked()
        {
            Debug.Log("[CreationUI] Back Clicked");
            if (creationPanel != null) creationPanel.SetActive(false);
            if (selectionPanel != null) selectionPanel.SetActive(true);
        }

        public void OnCharacterCreateResponse(bool success, string message)
        {
            confirmButton.interactable = true;

            if (success)
            {
                Debug.Log($"Character Created: {message}");
                OnBackClicked(); // Go back to selection screen
                CharacterSelectionUI.Instance.RequestCharacterList(); // Refresh the list
            }
            else
            {
                if (errorText != null) errorText.text = message;
            }
        }
    }
}
