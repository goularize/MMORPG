using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Shared.Network;

namespace Client.UI
{
    public class LoginUI : MonoBehaviour
    {
        [Header("Inputs")]
        public TMP_InputField usernameInput;
        public TMP_InputField passwordInput;
        
        [Header("Buttons")]
        public Button signInButton;
        public Button signUpButton;

        private void Start()
        {
            signInButton.onClick.AddListener(OnSignInClicked);
            signUpButton.onClick.AddListener(OnSignUpClicked);
        }

        private void OnSignInClicked()
        {
            string username = usernameInput.text;
            string password = passwordInput.text;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Debug.LogWarning("[LoginUI] Username and Password cannot be empty!");
                return;
            }

            Debug.Log($"[LoginUI] Sending Sign In Request for '{username}'...");

            using (Packet packet = new Packet(OpCode.SignInRequest))
            {
                packet.Write(username);
                packet.Write(password);
                Network.NetworkManager.Instance.SendPacket(packet);
            }
            
            // Optionally disable buttons to prevent spamming
            signInButton.interactable = false;
            signUpButton.interactable = false;
        }

        private void OnSignUpClicked()
        {
            string username = usernameInput.text;
            string password = passwordInput.text;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Debug.LogWarning("[LoginUI] Username and Password cannot be empty!");
                return;
            }

            Debug.Log($"[LoginUI] Sending Sign Up Request for '{username}'...");

            using (Packet packet = new Packet(OpCode.SignUpRequest))
            {
                packet.Write(username);
                packet.Write(password);
                Network.NetworkManager.Instance.SendPacket(packet);
            }

            signInButton.interactable = false;
            signUpButton.interactable = false;
        }

        // Called by AuthHandler when a response is received
        public void ReEnableButtons()
        {
            signInButton.interactable = true;
            signUpButton.interactable = true;
        }
    }
}
