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

            // 1. Remove from current map. Observers get their despawn from the regular AoI pass, and the player's
            // own AoI state is reset so nearby entities are re-sent. The position is persisted below (the bind
            // point), so skip the disconnect-style save that could overwrite it with the death position.
            GameLogic.MapMgr.RemovePlayer(player.Id, savePosition: false);

            // 2. Restore state & teleport to bind point
            player.Health = player.MaxHealth;
            player.Mana = player.MaxMana;
            player.MapId = player.BindMapId;
            player.Position = player.BindPosition;

            // 3. Save to Database so state persists
            using var db = AppDbContext.Factory();
            var charData = db.Characters.FirstOrDefault(c => c.Id == player.Id);
            if (charData != null)
            {
                charData.Health = player.Health;
                charData.Mana = player.Mana;
                charData.MapId = player.MapId;
                charData.X = player.Position.X;
                charData.Y = player.Position.Y;
                charData.Z = player.Position.Z;
                db.SaveChanges();
            }

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
