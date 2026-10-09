using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Client.Network.Handlers;

namespace Client.UI
{
    /// <summary>
    /// "Character select" button of the game HUD plus the confirmation modal shown when the server says the player
    /// is in combat (leaving then keeps the character in the world for a while). Buttons are wired here in code.
    /// </summary>
    public class LogoutUI : MonoBehaviour
    {
        public static LogoutUI Instance { get; private set; }

        [Header("Button")]
        [SerializeField] private Button logoutButton;

        [Header("Combat confirmation modal")]
        [Tooltip("The modal root. It is hidden at start and shown when the server asks for a confirmation.")]
        [SerializeField] private GameObject confirmPanel;
        [SerializeField] private TextMeshProUGUI confirmText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [Tooltip("{0} is replaced with the number of seconds the character stays in the world.")]
        [SerializeField, TextArea] private string combatMessage =
            "You are in combat. If you leave now, your character stays in the world for {0} seconds and can be attacked. Leave anyway?";

        [Header("Behaviour")]
        [Tooltip("Seconds to wait for the server's answer before the button works again.")]
        [SerializeField] private float responseTimeoutSeconds = 10f;

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
            logoutButton.onClick.AddListener(OnLogoutClicked);
            confirmButton.onClick.AddListener(OnConfirmClicked);
            cancelButton.onClick.AddListener(OnCancelClicked);

            confirmPanel.SetActive(false);
        }

        private void Update()
        {
            if (_pending && Time.unscaledTime - _pendingSince > responseTimeoutSeconds)
            {
                Debug.LogWarning("[LogoutUI] The server did not answer the logout request.");
                SetPending(false);
            }
        }

        /// <summary>Called by LogoutHandler when the server wants the player to accept the combat-log penalty first.</summary>
        public void ShowCombatConfirmation(float lingerSeconds)
        {
            SetPending(false);
            if (confirmText != null) confirmText.text = string.Format(combatMessage, Mathf.CeilToInt(lingerSeconds));
            confirmPanel.SetActive(true);
        }

        private void OnLogoutClicked()
        {
            if (_pending) return;

            SetPending(true);
            LogoutHandler.RequestLogout(false);
        }

        private void OnConfirmClicked()
        {
            confirmPanel.SetActive(false);
            SetPending(true);
            LogoutHandler.RequestLogout(true);
        }

        private void OnCancelClicked()
        {
            confirmPanel.SetActive(false);
            SetPending(false);
        }

        private void SetPending(bool pending)
        {
            _pending = pending;
            _pendingSince = Time.unscaledTime;
            logoutButton.interactable = !pending;
        }
    }
}
