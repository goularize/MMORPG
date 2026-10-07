using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Client.Network;
using Shared.Network;
using Client.Network.Handlers;

namespace Client.UI
{
    /// <summary>
    /// Character list screen: pick a character, then Play or Delete it, or open the creation panel.
    /// Every button is wired here in code (no OnClick entries in the scene).
    /// </summary>
    public class CharacterSelectionUI : MonoBehaviour
    {
        public static CharacterSelectionUI Instance { get; private set; }

        public static string SelectedCharacterName { get; private set; }
        public static int SelectedCharacterLevel { get; private set; } = 1;

        [Header("Panels")]
        [SerializeField] private GameObject selectionPanel;
        [SerializeField] private GameObject creationPanel;

        [Header("Character List")]
        [Tooltip("Parent object with the layout group the character buttons are spawned into.")]
        [SerializeField] private Transform characterListContainer;
        [Tooltip("Prefab with a Button and a TextMeshProUGUI.")]
        [SerializeField] private GameObject characterButtonPrefab;

        [Header("Buttons")]
        [SerializeField] private Button playButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Button createNewButton;

        [Header("Feedback")]
        [Tooltip("Shows errors and the empty-list hint.")]
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Behaviour")]
        [Tooltip("Seconds to wait for the server's answer before unlocking the screen again.")]
        [SerializeField] private float responseTimeoutSeconds = 10f;

        private List<CharacterData> _characters = new List<CharacterData>();
        private int _selectedCharacterId = -1;
        private bool _pending;
        private float _pendingSince;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            playButton.onClick.AddListener(OnPlayClicked);
            deleteButton.onClick.AddListener(OnDeleteClicked);
            createNewButton.onClick.AddListener(OnCreateNewClicked);

            ShowSelection();
            RequestCharacterList();
        }

        private void Update()
        {
            if (_pending && Time.unscaledTime - _pendingSince > responseTimeoutSeconds)
            {
                EndPending("The server did not respond. Please try again.");
            }
        }

        public void ShowSelection()
        {
            if (selectionPanel != null) selectionPanel.SetActive(true);
            if (creationPanel != null) creationPanel.SetActive(false);
        }

        public void RequestCharacterList()
        {
            BeginPending();
            using (Packet packet = new Packet(OpCode.CharacterListRequest))
            {
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public void OnCharacterListReceived(List<CharacterData> characters)
        {
            EndPending();
            _characters = characters;

            foreach (Transform child in characterListContainer)
            {
                Destroy(child.gameObject);
            }

            ClearSelection();

            foreach (var character in characters)
            {
                GameObject buttonObject = Instantiate(characterButtonPrefab, characterListContainer);
                var label = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = $"Lv.{character.Level} {character.Name}";

                int id = character.Id;
                buttonObject.GetComponent<Button>().onClick.AddListener(() => SelectCharacter(id));
            }

            SetStatus(characters.Count == 0 ? "You have no characters yet. Create one to start playing." : "");
        }

        private void SelectCharacter(int characterId)
        {
            if (_pending) return;

            var character = _characters.Find(c => c.Id == characterId);
            if (character == null) return;

            _selectedCharacterId = characterId;
            SelectedCharacterName = character.Name;
            SelectedCharacterLevel = character.Level;
            SetStatus("");
            RefreshButtons();
        }

        private void ClearSelection()
        {
            _selectedCharacterId = -1;
            SelectedCharacterName = string.Empty;
            SelectedCharacterLevel = 1;
            RefreshButtons();
        }

        private void OnPlayClicked()
        {
            if (_pending || _selectedCharacterId == -1) return;

            // The client needs to know which entity it owns once the world starts replicating
            CharacterHandler.LocalPlayerId = _selectedCharacterId;

            BeginPending();
            using (Packet packet = new Packet(OpCode.CharacterSelectRequest))
            {
                packet.Write(_selectedCharacterId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        private void OnDeleteClicked()
        {
            if (_pending || _selectedCharacterId == -1) return;

            BeginPending();
            using (Packet packet = new Packet(OpCode.CharacterDeleteRequest))
            {
                packet.Write(_selectedCharacterId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        private void OnCreateNewClicked()
        {
            if (_pending) return;

            SetStatus("");
            if (selectionPanel != null) selectionPanel.SetActive(false);
            if (creationPanel != null) creationPanel.SetActive(true);
            CharacterCreationUI.Instance?.ResetForm();
        }

        public void OnCharacterDeleteResponse(bool success)
        {
            if (success)
            {
                RequestCharacterList(); // refreshes the list and keeps the screen locked until it arrives
            }
            else
            {
                EndPending("Could not delete the character.");
            }
        }

        /// <summary>Called by CharacterHandler when the server refuses to enter the world with the chosen character.</summary>
        public void OnCharacterSelectFailed()
        {
            EndPending("Could not enter the world with this character. Please try again.");
        }

        private void BeginPending()
        {
            _pending = true;
            _pendingSince = Time.unscaledTime;
            RefreshButtons();
        }

        private void EndPending(string statusMessage = null)
        {
            _pending = false;
            RefreshButtons();
            if (statusMessage != null) SetStatus(statusMessage);
        }

        private void RefreshButtons()
        {
            bool hasSelection = _selectedCharacterId != -1;
            playButton.interactable = !_pending && hasSelection;
            deleteButton.interactable = !_pending && hasSelection;
            createNewButton.interactable = !_pending;
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }
    }
}
