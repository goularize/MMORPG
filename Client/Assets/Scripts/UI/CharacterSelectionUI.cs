using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Client.Network;
using Shared.Network;
using Client.Network.Handlers;

namespace Client.UI
{
    public class CharacterSelectionUI : MonoBehaviour
    {
        public static CharacterSelectionUI Instance;

        [Header("Panels")]
        public GameObject selectionPanel;
        public GameObject creationPanel;

        [Header("UI Elements")]
        public Transform characterListContainer; // Parent object with VerticalLayoutGroup
        public GameObject characterButtonPrefab; // Prefab with TextMeshProUGUI and Button
        public Button playButton;
        public Button deleteButton;
        
        private int _selectedCharacterId = -1;

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void Start()
        {
            // Force correct panel states
            if (selectionPanel != null) selectionPanel.SetActive(true);
            if (creationPanel != null) creationPanel.SetActive(false);

            // Lock buttons until a character is selected
            playButton.interactable = false;
            deleteButton.interactable = false;

            RequestCharacterList();
        }

        public void RequestCharacterList()
        {
            using (Packet packet = new Packet(OpCode.CharacterListRequest))
            {
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public void OnCharacterListReceived(List<CharacterData> characters)
        {
            // Clear old list
            foreach (Transform child in characterListContainer)
            {
                Destroy(child.gameObject);
            }

            _selectedCharacterId = -1;
            playButton.interactable = false;
            deleteButton.interactable = false;

            // Spawn new buttons
            foreach (var character in characters)
            {
                GameObject btnObj = Instantiate(characterButtonPrefab, characterListContainer);
                var textComponent = btnObj.GetComponentInChildren<TextMeshProUGUI>();
                if (textComponent != null)
                {
                    textComponent.text = $"Lv.{character.Level} {character.Name}";
                }

                var btn = btnObj.GetComponent<Button>();
                btn.onClick.AddListener(() => SelectCharacter(character.Id));
            }
        }

        private void SelectCharacter(int characterId)
        {
            _selectedCharacterId = characterId;
            playButton.interactable = true;
            deleteButton.interactable = true;
            Debug.Log($"Selected character ID: {characterId}");
        }

        public void OnPlayClicked()
        {
            if (_selectedCharacterId == -1) return;

            using (Packet packet = new Packet(OpCode.CharacterSelectRequest))
            {
                packet.Write(_selectedCharacterId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public void OnDeleteClicked()
        {
            if (_selectedCharacterId == -1) return;

            using (Packet packet = new Packet(OpCode.CharacterDeleteRequest))
            {
                packet.Write(_selectedCharacterId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public void OnCreateNewClicked()
        {
            Debug.Log("[SelectionUI] Create New Clicked");
            if (selectionPanel != null) selectionPanel.SetActive(false);
            if (creationPanel != null) creationPanel.SetActive(true);
        }

        public void OnCharacterDeleteResponse(bool success)
        {
            if (success)
            {
                RequestCharacterList(); // Refresh the list
            }
            else
            {
                Debug.LogError("Failed to delete character.");
            }
        }
    }
}
