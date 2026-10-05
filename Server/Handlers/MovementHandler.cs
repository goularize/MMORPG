using System;
using Shared.Network;
using Shared.Math;
using Server.Network;
using Server.World;

namespace Server.Handlers
{
    public static class MovementHandler
    {
        public static void HandleMoveRequest(IClientConnection client, Packet packet)
        {
            if (client.PlayerId == null) return;

            // 1. Read the new requested position from the packet
            float targetX = packet.ReadFloat();
            float targetY = packet.ReadFloat();
            float targetZ = packet.ReadFloat();

            // NaN/Infinity make every comparison below false (speed check and collision both pass), so drop them here.
            if (!float.IsFinite(targetX) || !float.IsFinite(targetY) || !float.IsFinite(targetZ)) return;

            Vector3 targetPosition = new Vector3(targetX, targetY, targetZ);

            // 2. Look up the active Player entity from the World
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player != null && player.Health <= 0) return;
            if (player != null)
            {
                // Basic Speed Hack Validation
                float distance = Vector3.Distance(player.Position, targetPosition);
                double elapsedSeconds = (DateTime.UtcNow - player.LastMoveTime).TotalSeconds;
                
                // If elapsedSeconds is 0 (multiple packets in the exact same millisecond), assume an instant move to prevent div-by-zero
                if (elapsedSeconds < 0.01) elapsedSeconds = 0.01;

                float speed = distance / (float)elapsedSeconds;

                // Let's assume the absolute maximum walking speed is 10.0 units per second.
                // We add a little buffer (e.g., 15.0f) for network latency/jitter.
                float MAX_ALLOWED_SPEED = 15.0f;

                if (speed > MAX_ALLOWED_SPEED)
                {
                    Console.WriteLine($"[Anti-Cheat] Player {player.Name} moved too fast! (Speed: {speed:F2} u/s). Rubberbanding...");
                    
                    // Rubberband: Send the player's OLD/true position back to them so their client snaps back
                    using Packet rubberbandPacket = new Packet(OpCode.EntityPositionUpdate);
                    rubberbandPacket.Write(player.Id);
                    rubberbandPacket.Write(player.Position); // The old, valid position
                    client.Send(rubberbandPacket);
                    return;
                }

                // Map Collision Validation (Phase 5)
                var map = GameLogic.MapMgr.GetMap(player.MapId);
                if (map != null && !map.IsWalkable(targetPosition))
                {
                    Console.WriteLine($"[Anti-Cheat] Player {player.Name} tried to walk into a solid object at {targetPosition}. Rubberbanding...");
                    
                    using Packet rubberbandPacket = new Packet(OpCode.EntityPositionUpdate);
                    rubberbandPacket.Write(player.Id);
                    rubberbandPacket.Write(player.Position); // The old, valid position
                    client.Send(rubberbandPacket);
                    return;
                }

                // If valid, apply the move and update the timestamp
                player.Position = targetPosition;
                player.LastMoveTime = DateTime.UtcNow;

                // The GameLogic.Update() / ProcessAreaOfInterest() will automatically 
                // handle broadcasting this new position to nearby players!
            }
        }
    }
}
