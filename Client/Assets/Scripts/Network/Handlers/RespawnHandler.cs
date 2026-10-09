using UnityEngine;
using Shared.Network;

namespace Client.Network.Handlers
{
    /// <summary>
    /// Coming back to life at the bind point. The server only accepts the request from a dead character and answers
    /// with the map and position it was moved to, followed by a vitals update that refills the bars.
    /// </summary>
    public static class RespawnHandler
    {
        public static void RequestRespawn()
        {
            using (Packet packet = new Packet(OpCode.PlayerRespawnRequest))
            {
                NetworkManager.Instance.SendPacket(packet);
            }
        }

        public static void HandleRespawnResponse(Packet packet)
        {
            bool success = packet.ReadBool();
            if (!success)
            {
                Debug.LogWarning("[RespawnHandler] The server refused the respawn request.");
                UI.DeathHUD.Instance?.OnRespawnFailed();
                return;
            }

            int mapId = packet.ReadInt();
            var pos = packet.ReadVector3();
            var position = new Vector3(pos.X, pos.Y, pos.Z);

            if (mapId != CharacterHandler.CurrentMapId)
            {
                // The bind point is on another map: that needs a zone change, which does not exist yet (#121)
                Debug.LogError($"[RespawnHandler] Respawned on Map {mapId} but the client is on Map {CharacterHandler.CurrentMapId}; zone changes are not supported yet.");
            }

            Debug.Log($"[RespawnHandler] Respawned at {position}.");
            Client.World.GameManager.Instance?.TeleportLocalPlayer(position);
            UI.DeathHUD.Instance?.OnRespawned();
        }
    }
}
