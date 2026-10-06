using System;
using System.Linq;
using Shared.Network;
using Server.Network;
using Server.World;
using Server.Database;

namespace Server.Handlers
{
    public static class BindHandler
    {
        public static void HandleSetBindPointRequest(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;

            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null) return;

            // In a real game, you would validate that the player is near an Innkeeper or Campfire.
            // For now, we trust the request (or just bind them to their current location).
            
            player.BindMapId = player.MapId;
            player.BindPosition = player.Position;

            // Persist through the write-behind queue (the game thread never waits for the database)
            player.QueueSave(urgent: true);

            Console.WriteLine($"[Bind] Player {player.Name} bound their soul to Map {player.BindMapId} at {player.BindPosition}.");

            // Send confirmation back to client
            using Packet response = new Packet(OpCode.SetBindPointResponse);
            response.Write(true);
            response.Write(player.BindMapId);
            response.Write(player.BindPosition);
            client.Send(response);
        }
    }
}
