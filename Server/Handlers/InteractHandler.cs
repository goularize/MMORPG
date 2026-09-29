using System;
using Shared.Network;
using Server.Network;
using Server.World;
using Server.World.Entities;

namespace Server.Handlers
{
    public static class InteractHandler
    {
        private const float INTERACT_RANGE = 3.0f;

        public static void HandleInteractRequest(IClientConnection client, Packet packet)
        {
            int targetId = packet.ReadInt();

            // 1. Get the acting player
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.EntityMgr.GetEntity(client.PlayerId.Value) as Player;
            if (player == null || player.Health <= 0) return;

            // 2. Existence Check
            var target = GameLogic.EntityMgr.GetEntity(targetId);
            if (target == null)
            {
                Console.WriteLine($"[Interact] Player {player.Name} tried to interact with non-existent entity {targetId}.");
                return;
            }

            // 3. AoI Check
            if (!player.KnownEntities.Contains(targetId))
            {
                Console.WriteLine($"[Interact] Player {player.Name} tried to interact with entity {targetId} outside of their AoI.");
                return;
            }

            // 4. Distance Check
            float distance = Shared.Math.Vector3.Distance(player.Position, target.Position);
            if (distance > INTERACT_RANGE)
            {
                Console.WriteLine($"[Interact] Player {player.Name} is too far ({distance:F2}) to interact with {target.Name}.");
                return;
            }

            // 5. State Check
            if (target.Health <= 0)
            {
                Console.WriteLine($"[Interact] Player {player.Name} tried to interact with a dead target {target.Name}.");
                return;
            }

            // Execution
            Console.WriteLine($"[Interact] {player.Name} interacted with {target.Name}.");
            // TODO: Send VendorWindowOpen or QuestDialog packets back to the client.
        }
    }
}
