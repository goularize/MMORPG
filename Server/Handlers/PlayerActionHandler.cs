using System;
using System.Linq;
using Shared.Network;
using Server.Network;
using Server.World;
using Server.Database;

namespace Server.Handlers
{
    public static class PlayerActionHandler
    {
        public static void HandleRespawnRequest(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;

            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            
            // Only allow respawn if actually dead
            if (player == null || player.Health > 0) return;

            // A stale connection must not revive the character a newer session of it is playing
            if (!ReferenceEquals(player.Connection, client)) return;

            // 1. Remove from current map. Observers get their despawn from the regular AoI pass, and the player's
            // own AoI state is reset so nearby entities are re-sent. The position is persisted below (the bind
            // point), so skip the disconnect-style save that could overwrite it with the death position.
            GameLogic.MapMgr.RemovePlayer(player.Id, saveState: false);

            // 2. Restore state & teleport to bind point
            player.ReviveAtBindPoint();

            // 3. Queue the revived state (bind point, full vitals) so it persists
            player.QueueSave(urgent: true);

            // 4. Add back to the World Manager at the new map
            // This will naturally broadcast EntitySpawn to nearby players at the Inn
            GameLogic.MapMgr.AddPlayer(player);

            Console.WriteLine($"[Respawn] Player {player.Name} has revived at Map {player.MapId}, {player.Position}.");

            // 5. Tell the client they successfully respawned
            // The client will use this to hide the "You Died" UI and optionally fade in the screen.
            using Packet response = new Packet(OpCode.PlayerRespawnResponse);
            response.Write(true);
            response.Write(player.MapId);
            response.Write(player.Position);
            client.Send(response);
            
            // Force a vitals update to sync the UI health bars
            using Packet statsPacket = new Packet(OpCode.VitalsUpdate);
            statsPacket.Write(player.Id);
            statsPacket.Write(player.Health);
            statsPacket.Write(player.Mana);
            client.Send(statsPacket);
        }
    }
}
