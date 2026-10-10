using UnityEngine;
using Shared.Enums;
using Shared.Network;

namespace Client.Network.Handlers
{
    /// <summary>
    /// Leaving the world back to character select. The server decides whether the player may simply leave or is in
    /// combat (then it asks for a confirmation first, because the character stays in the world for a while).
    /// </summary>
    public static class LogoutHandler
    {
        public const string CharacterSelectionScene = "CharacterSelectionScene";

        /// <param name="confirmed">True once the player accepted the combat-log penalty.</param>
        public static void RequestLogout(bool confirmed)
        {
            using (Packet packet = new Packet(OpCode.LogoutRequest))
            {
                packet.Write(confirmed);
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public static void HandleLogoutResponse(Packet packet)
        {
            var result = (LogoutResult)packet.ReadByte();
            float lingerSeconds = packet.ReadFloat();

            switch (result)
            {
                case LogoutResult.ConfirmRequired:
                    Debug.Log($"[LogoutHandler] In combat: leaving keeps the character in the world for {lingerSeconds}s. Asking the player.");
                    UI.LogoutUI.Instance?.ShowCombatConfirmation(lingerSeconds);
                    break;

                case LogoutResult.Success:
                case LogoutResult.NotInWorld:
                    // NotInWorld: the server has no character for this connection, so there is nothing left to leave
                    Debug.Log($"[LogoutHandler] Left the world ({result}). Returning to character select.");
                    ReturnToCharacterSelect();
                    break;
            }
        }

        /// <summary>
        /// Forgets everything about the character that was in the world and loads the character select scene. The
        /// game scene and the map scene are unloaded by loading it, which destroys every spawned entity.
        /// </summary>
        public static void ReturnToCharacterSelect()
        {
            ResetSessionState();
            UI.LoadingScreenUI.Instance?.Hide();
            UnityEngine.SceneManagement.SceneManager.LoadScene(CharacterSelectionScene);
        }

        /// <summary>The static client state of the last character; without this the next one briefly shows its values.</summary>
        public static void ResetSessionState()
        {
            CharacterHandler.ResetSession();
            WorldHandler.ResetSession();
            ProgressionHandler.ResetSession();
            DialogueHandler.ResetSession();
            QuestHandler.ResetSession();
            MarkerHandler.ResetSession();
            ChatHandler.ResetSession();
        }
    }
}
