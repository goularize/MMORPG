using UnityEngine;
using UnityEngine.UI;
using Client.Network.Handlers;

namespace Client.UI
{
    /// <summary>
    /// "You died" overlay of the game HUD: a translucent panel with a Respawn button, shown while the local
    /// character's health is 0 (also when it logs in already dead). Respawning asks the server, which moves the
    /// character to its bind point; the panel closes once the vitals say it is alive again.
    /// </summary>
    public class DeathHUD : MonoBehaviour
    {
        public static DeathHUD Instance { get; private set; }

        [Tooltip("The overlay root (translucent panel with a CanvasGroup or Image that blocks clicks). Hidden at start.")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button respawnButton;

        [Header("Behaviour")]
        [Tooltip("Seconds to wait for the server's answer before the button works again.")]
        [SerializeField] private float responseTimeoutSeconds = 10f;

        private bool _pending;
        private float _pendingSince;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            respawnButton.onClick.AddListener(OnRespawnClicked);
            WorldHandler.OnStatsUpdated += Refresh;

            Refresh();
        }

        private void OnDestroy()
        {
            WorldHandler.OnStatsUpdated -= Refresh;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_pending && Time.unscaledTime - _pendingSince > responseTimeoutSeconds)
            {
                Debug.LogWarning("[DeathHUD] The server did not answer the respawn request.");
                SetPending(false);
            }
        }

        /// <summary>Called by RespawnHandler when the server moved the character to its bind point.</summary>
        public void OnRespawned()
        {
            SetPending(false);
            Refresh();
        }

        /// <summary>Called by RespawnHandler when the server refused the request.</summary>
        public void OnRespawnFailed()
        {
            SetPending(false);
        }

        // Dead means health 0 in the local vitals; any vitals update showing health again closes the panel
        private void Refresh()
        {
            bool dead = WorldHandler.LocalHealth <= 0;
            if (panel.activeSelf == dead) return;

            panel.SetActive(dead);
            if (dead) SetPending(false);
        }

        private void OnRespawnClicked()
        {
            if (_pending) return;

            SetPending(true);
            RespawnHandler.RequestRespawn();
        }

        private void SetPending(bool pending)
        {
            _pending = pending;
            _pendingSince = Time.unscaledTime;
            respawnButton.interactable = !pending;
        }
    }
}
