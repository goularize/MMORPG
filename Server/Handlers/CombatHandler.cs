using System;
using Shared.Network;
using Server.Network;
using Server.World;
using Server.World.Entities;

namespace Server.Handlers
{
    public static class CombatHandler
    {
        // Define max melee range
        private const float MELEE_RANGE = 2.5f;

        public static void HandleAttackRequest(IClientConnection client, Packet packet)
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
                Console.WriteLine($"[Combat] Player {player.Name} tried to attack non-existent entity {targetId}.");
                return;
            }

            // 3. AoI Check
            if (!player.KnownEntities.Contains(targetId))
            {
                Console.WriteLine($"[Combat] Player {player.Name} tried to attack entity {targetId} outside of their AoI.");
                return;
            }

            // 4. Distance Check
            float distance = Shared.Math.Vector3.Distance(player.Position, target.Position);
            if (distance > MELEE_RANGE)
            {
                Console.WriteLine($"[Combat] Player {player.Name} is too far ({distance:F2}) to attack {target.Name}.");
                return;
            }

            // 5. State Check
            if (target.Health <= 0)
            {
                Console.WriteLine($"[Combat] Player {player.Name} tried to attack a dead target {target.Name}.");
                return;
            }

            // TODO: Distinguish hostile NPCs. For now, we allow attacking anything that isn't the player themselves.
            if (target.Id == player.Id)
            {
                Console.WriteLine($"[Combat] Player {player.Name} tried to attack themselves.");
                return;
            }

            // Execution: Calculate Damage
            // A simple damage formula: Attack - (Defense / 2), minimum 1 damage
            int damage = Math.Max(1, player.Attack - (target.Defense / 2));
            target.Health -= damage;
            if (target.Health < 0) target.Health = 0;

            Console.WriteLine($"[Combat] {player.Name} hit {target.Name} for {damage} damage! ({target.Health}/{target.MaxHealth})");

            // Broadcast the Vitals update to everyone who knows about this target
            // For now, we force the VitalsChanged flag so the target (if player) syncs,
            // or we manually broadcast it to the attacker so they see the HP go down.
            using Packet vitalsPacket = new Packet(OpCode.VitalsUpdate);
            vitalsPacket.Write(target.Id);
            vitalsPacket.Write(target.Health);
            vitalsPacket.Write(target.Mana);

            // Send to attacker
            client.Send(vitalsPacket);

            // If target is a player, send to them as well
            if (target is Player targetPlayer)
            {
                targetPlayer.Connection.Send(vitalsPacket);
            }
        }
    }
}
