using System;
using System.Linq;
using Server.Data;
using Server.Dialogue;
using Shared.Enums;
using Shared.Network;
using Server.Network;
using Server.World;
using Server.World.Entities;

namespace Server.Handlers
{
    public static class InteractHandler
    {
        private const float INTERACT_RANGE = Shared.Constants.GameRules.InteractRange;

        public static void HandleInteractRequest(IClientConnection client, Packet packet)
        {
            int targetId = packet.ReadInt();

            // 1. Get the acting player
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            // 2. Existence Check
            var target = GameLogic.MapMgr.GetMap(player.MapId)?.GetEntity(targetId);
            if (target == null)
            {
                Console.WriteLine($"[Interact] Player {player.Name} tried to interact with non-existent entity {targetId}.");
                Reply(client, targetId, InteractOutcome.NotFound);
                return;
            }

            // 3. AoI Check
            if (!player.KnownEntities.Contains(targetId))
            {
                Console.WriteLine($"[Interact] Player {player.Name} tried to interact with entity {targetId} outside of their AoI.");
                Reply(client, targetId, InteractOutcome.NotFound);
                return;
            }

            // 4. Distance Check
            float distance = Shared.Math.Vector3.Distance(player.Position, target.Position);
            if (distance > INTERACT_RANGE)
            {
                Console.WriteLine($"[Interact] Player {player.Name} is too far ({distance:F2}) to interact with {target.Name}.");
                Reply(client, targetId, InteractOutcome.TooFar);
                return;
            }

            // 5. State Check
            if (target.Health <= 0)
            {
                Console.WriteLine($"[Interact] Player {player.Name} tried to interact with a dead target {target.Name}.");
                Reply(client, targetId, InteractOutcome.TargetDead);
                return;
            }

            // Execution: an NPC with a dialogue starts a conversation (DialogueOpen); the rest has nothing to offer
            if (target is not NPC npc || !DialogueService.Open(player, npc))
            {
                Reply(client, targetId, InteractOutcome.NothingToDo);
                return;
            }

            Console.WriteLine($"[Interact] {player.Name} started a conversation with {target.Name}.");
        }

        private static void Reply(IClientConnection client, int targetId, InteractOutcome outcome, InteractAction action = InteractAction.None)
        {
            using Packet response = new Packet(OpCode.EntityInteractResponse);
            response.Write(targetId);
            response.Write((byte)outcome);
            response.Write((byte)action);
            client.Send(response);
        }
    }
}
