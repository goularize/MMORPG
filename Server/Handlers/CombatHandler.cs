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
        private const float MELEE_RANGE = Shared.Constants.GameRules.DefaultMeleeRange;
        private static readonly Random _rng = new Random();

        public static void HandleAttackRequest(IClientConnection client, Packet packet)
        {
            int targetId = packet.ReadInt();

            // 1. Get the acting player
            if (!client.PlayerId.HasValue) return;
            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            // Cooldown Validation
            if ((DateTime.UtcNow - player.LastAttackTime).TotalSeconds < player.BaseAttackSpeed)
            {
                // Ignored - hitting too fast (possible packet spam/macro)
                return;
            }

            // 2. Existence Check
            var map = GameLogic.MapMgr.GetMap(player.MapId);
            var target = map?.GetEntity(targetId);
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

            if (target.Id == player.Id)
            {
                Console.WriteLine($"[Combat] Player {player.Name} tried to attack themselves.");
                return;
            }

            if (target is NPC friendlyNpc && friendlyNpc.BehaviorType == Shared.Enums.MobBehaviorType.Friendly)
            {
                Console.WriteLine($"[Combat] Player {player.Name} tried to attack friendly NPC {friendlyNpc.Name}.");
                return;
            }

            // Mark attack timestamp
            player.LastAttackTime = DateTime.UtcNow;

            // Both sides are in combat (resource nodes have no passive regen to block)
            if (target is Resource)
            {
                player.MarkRegenBlocked(); // harvesting is not combat: it must not make a disconnect a combat log
            }
            else
            {
                player.MarkInCombat();
                target.MarkInCombat();
            }

            // Execution: Combat Math
            int finalDamage = 0;
            bool isDodge = false;
            bool isCrit = false;

            if (target is Resource resourceTarget)
            {
                // Resources don't dodge or crit
                finalDamage = Math.Max(1, player.Attack);
                resourceTarget.TakeDamage(finalDamage, map, player);
            }
            else
            {
                // 1. Dodge Check
                if (_rng.NextDouble() < target.DodgeChance)
                {
                    isDodge = true;
                }
                else
                {
                    // 2. Base Mitigation (Diminishing-Returns MMORPG Curve)
                    float reduction = (float)target.Defense / (target.Defense + (40f * Math.Max(1, player.Level)));
                    int baseDamage = Math.Max(1, (int)Math.Round(player.Attack * (1.0f - reduction)));

                    // 3. Variance (+/- 10%)
                    double variance = 0.9 + (_rng.NextDouble() * 0.2);
                    baseDamage = (int)(baseDamage * variance);

                    // 4. Critical Strike Check
                    if (_rng.NextDouble() < player.CritChance)
                    {
                        isCrit = true;
                        baseDamage = (int)(baseDamage * player.CritMultiplier);
                    }

                    finalDamage = Math.Max(1, baseDamage);

                    // Apply Damage
                    target.Health -= finalDamage;
                    if (target.Health < 0) target.Health = 0;
                }

                if (target is NPC npc)
                {
                    if (npc.Health <= 0)
                    {
                        npc.Die(map, player);
                        player.AddExp(npc.ExpYield);
                    }
                    else
                    {
                        npc.OnAttacked(player, map);
                    }
                }
            }

            Console.WriteLine($"[Combat] {player.Name} hit {target.Name} for {finalDamage} damage! (Crit: {isCrit}, Dodge: {isDodge}) ({target.Health}/{target.MaxHealth})");

            // Broadcast the Combat Event to the Map (AoI)
            if (map != null)
            {
                using Packet combatPacket = new Packet(OpCode.EntityCombatEvent);
                combatPacket.Write(player.Id);
                combatPacket.Write(target.Id);
                combatPacket.Write(finalDamage);
                combatPacket.Write(isCrit);
                combatPacket.Write(isDodge);

                map.Broadcast(combatPacket, player.Position);
            }

            // If damage was dealt (and not dodged), target's vitals changed
            if (!isDodge)
            {
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
}
