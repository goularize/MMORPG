using System;
using Shared.Enums;
using Shared.Network;
using Server.Network;
using Server.World;

namespace Server.Handlers
{
    /// <summary>
    /// Leaves the world and goes back to character select without closing the connection. Leaving in combat is
    /// allowed but costs the combat-log penalty (the character stays in the world for COMBAT_LOGOUT_LINGER_SECONDS),
    /// so the first request is answered with ConfirmRequired and nothing changes until the client repeats it confirmed.
    /// </summary>
    public static class LogoutHandler
    {
        public static void HandleLogoutRequest(IClientConnection client, Packet packet)
        {
            bool confirmed = packet.ReadBool();

            var player = client.PlayerId.HasValue ? GameLogic.MapMgr.GetPlayer(client.PlayerId.Value) : null;

            // Not in the world (never entered, or this connection lost the character to a newer session)
            if (player == null || !ReferenceEquals(player.Connection, client))
            {
                SendResponse(client, LogoutResult.NotInWorld);
                return;
            }

            if (!confirmed && MapManager.WouldLinger(player))
            {
                SendResponse(client, LogoutResult.ConfirmRequired, (float)ServerConfig.CombatLogoutLingerSeconds);
                return;
            }

            // Same path as a dropped connection: removes the character (saving it) or leaves it lingering when in combat
            GameLogic.MapMgr.RemovePlayerOwnedBy(client);
            client.PlayerId = null;
            Console.WriteLine($"[Client {client.Id}] '{player.Name}' logged out to character select.");

            SendResponse(client, LogoutResult.Success);
        }

        private static void SendResponse(IClientConnection client, LogoutResult result, float lingerSeconds = 0f)
        {
            using Packet response = new Packet(OpCode.LogoutResponse);
            response.Write((byte)result);
            response.Write(lingerSeconds);
            client.Send(response);
        }
    }
}
