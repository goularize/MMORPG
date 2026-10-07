using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Client.Network;
using Shared.Constants;
using Shared.Network;

namespace Client.UI
{
    /// <summary>
    /// Character creation panel. Validates the name with the shared <see cref="InputRules"/> before asking the server,
    /// which still has the final say (for example when the name is already taken).
    /// </summary>
    public class CharacterCreationUI : MonoBehaviour
    {
        public static CharacterCreationUI Instance { get; private set; }

        [Header("Panels")]
        [SerializeField] private GameObject selectionPanel;
        [SerializeField] private GameObject creationPanel;

        [Header("UI Elements")]
        [SerializeField] private TMP_InputField nameInputField;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI errorText;

        [Header("Behaviour")]
        [Tooltip("Seconds to wait for the server's answer before unlocking the form again.")]
        [SerializeField] private float responseTimeoutSeconds = 10f;

        // No appearance picker yet: every character uses the first appearance
        private const int DefaultAppearanceId = InputRules.MinAppearanceId;

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
            nameInputField.characterLimit = InputRules.CharacterNameMaxLength;

            confirmButton.onClick.AddListener(OnConfirmClicked);
            backButton.onClick.AddListener(OnBackClicked);
            nameInputField.onSubmit.AddListener(_ => OnConfirmClicked());
        }

        private void Update()
        {
            if (_pending && Time.unscaledTime - _pendingSince > responseTimeoutSeconds)
            {
                EndPending("The server did not respond. Please try again.");
            }
        }

        /// <summary>Called by CharacterSelectionUI each time the panel is opened (this script's object stays active).</summary>
        public void ResetForm()
        {
            _pending = false;
            nameInputField.text = "";
            SetError("");
            confirmButton.interactable = true;
            backButton.interactable = true;
            nameInputField.Select();
        }

        private void OnConfirmClicked()
        {
            if (_pending) return;

            string characterName = nameInputField.text.Trim();
            string error = InputRules.ValidateCharacterName(characterName);
            if (error != null)
            {
                SetError(error);
                return;
            }

            _pending = true;
            _pendingSince = Time.unscaledTime;
            SetError("");
            confirmButton.interactable = false;
            backButton.interactable = false;

            using (Packet packet = new Packet(OpCode.CharacterCreateRequest))
            {
                packet.Write(characterName);
                packet.Write(DefaultAppearanceId);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        private void OnBackClicked()
        {
            if (_pending) return;

            if (creationPanel != null) creationPanel.SetActive(false);
            if (selectionPanel != null) selectionPanel.SetActive(true);
        }

        public void OnCharacterCreateResponse(bool success, string message)
        {
            if (success)
            {
                EndPending();
                OnBackClicked();
                CharacterSelectionUI.Instance?.RequestCharacterList();
            }
            else
            {
                EndPending(message);
            }
        }

        private void EndPending(string errorMessage = null)
        {
            _pending = false;
            confirmButton.interactable = true;
            backButton.interactable = true;
            if (errorMessage != null) SetError(errorMessage);
        }

        private void SetError(string message)
        {
            if (errorText != null) errorText.text = message;
        }
    }
}
