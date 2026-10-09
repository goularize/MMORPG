using System;
using System.Linq;
using Shared.Enums;
using Shared.Network;
using Server.Data;
using Server.Network;
using Server.World;
using Server.World.Entities;

namespace Server.Handlers
{
    public static class BindHandler
    {
        /// <summary>True when a living NPC offering the BindPoint interaction is within interact range of the player.</summary>
        public static bool IsNearInnkeeper(Player player)
        {
            var map = GameLogic.MapMgr.GetMap(player.MapId);
            if (map == null) return false;

            return map.NPCs.Values.Any(npc =>
                npc.Health > 0
                && DataManager.Npcs.TryGetValue(npc.TemplateId, out var template)
                && template.OffersAction(InteractAction.BindPoint)
                && Shared.Math.Vector3.Distance(player.Position, npc.Position) <= Shared.Constants.GameRules.InteractRange);
        }

        public static void HandleSetBindPointRequest(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;

            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            if (!IsNearInnkeeper(player))
            {
                Console.WriteLine($"[Bind] Player {player.Name} tried to bind with no innkeeper in range.");
                SendResponse(client, player, success: false);
                return;
            }

            Bind(client, player);
        }

        /// <summary>Binds the player's soul to where they stand. The caller has already checked an innkeeper is in range.</summary>
        public static void Bind(IClientConnection client, Player player)
        {
            player.BindMapId = player.MapId;
            player.BindPosition = player.Position;

            // Persist through the write-behind queue (the game thread never waits for the database)
            player.QueueSave(urgent: true);

            Console.WriteLine($"[Bind] Player {player.Name} bound their soul to Map {player.BindMapId} at {player.BindPosition}.");
            SendResponse(client, player, success: true);
        }

        // Always carries the player's current bind point, so a rejected request tells the client what is still bound.
        private static void SendResponse(IClientConnection client, Player player, bool success)
        {
            using Packet response = new Packet(OpCode.SetBindPointResponse);
            response.Write(success);
            response.Write(player.BindMapId);
            response.Write(player.BindPosition);
            client.Send(response);
        }
    }
}
