using UnityEngine;
using UnityEngine.UI;

namespace Client.UI
{
    /// <summary>
    /// The buttons of the game HUD: the inventory and quest log windows (the same as the I and L keys) and the
    /// "character select" button. Buttons are wired here in code; the windows keep their own logic.
    /// </summary>
    public class HudButtonsUI : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button inventoryButton;
        [SerializeField] private Button questLogButton;
        [SerializeField] private Button logoutButton;

        private void Start()
        {
            if (inventoryButton != null) inventoryButton.onClick.AddListener(OnInventoryClicked);
            if (questLogButton != null) questLogButton.onClick.AddListener(OnQuestLogClicked);
            if (logoutButton != null) logoutButton.onClick.AddListener(OnLogoutClicked);

            if (LogoutUI.Instance != null) LogoutUI.Instance.OnPendingChanged += OnLogoutPendingChanged;
        }

        private void OnDestroy()
        {
            if (inventoryButton != null) inventoryButton.onClick.RemoveListener(OnInventoryClicked);
            if (questLogButton != null) questLogButton.onClick.RemoveListener(OnQuestLogClicked);
            if (logoutButton != null) logoutButton.onClick.RemoveListener(OnLogoutClicked);

            if (LogoutUI.Instance != null) LogoutUI.Instance.OnPendingChanged -= OnLogoutPendingChanged;
        }

        private static void OnInventoryClicked() => InventoryUI.Instance?.Toggle();

        private static void OnQuestLogClicked() => QuestLogUI.Instance?.Toggle();

        private static void OnLogoutClicked() => LogoutUI.Instance?.RequestLogout();

        // The button stays disabled while the server has not answered the logout request
        private void OnLogoutPendingChanged(bool pending)
        {
            if (logoutButton != null) logoutButton.interactable = !pending;
        }
    }
}
