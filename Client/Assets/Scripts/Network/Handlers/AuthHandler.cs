using UnityEngine;
using Shared.Network;

namespace Client.Network.Handlers
{
    public static class AuthHandler
    {
        public static void HandleAuthResponse(Packet packet)
        {
            bool isSuccess = packet.ReadBool();
            string message = packet.ReadString();

            if (isSuccess)
            {
                Debug.Log($"[Auth] SUCCESS: {message}");
                
                // Must run on main thread, which we are since this is invoked via Update()!
                UnityEngine.SceneManagement.SceneManager.LoadScene("CharacterSelectionScene");
            }
            else
            {
                Debug.LogError($"[Auth] FAILED: {message}");
                
                // Re-enable UI buttons if the sign in failed
                UI.AuthenticationUI authUI = Object.FindFirstObjectByType<UI.AuthenticationUI>();
                if (authUI != null)
                {
                    authUI.ReEnableButtons(message);
                }
            }
        }
    }
}
