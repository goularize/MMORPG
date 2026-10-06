using System;
using Shared.Enums;
using Shared.Network;
using Server.Network;
using Server.World;

namespace Server.Handlers
{
    public static class ProgressionHandler
    {
        public static void HandleAllocateStatPoint(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;

            byte statByte = packet.ReadByte();
            int points = packet.ReadInt();

            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null) return;

            bool isSuccess = false;
            string errorMessage = string.Empty;
            int newStatValue = 0;

            if (points <= 0)
            {
                errorMessage = "Invalid points amount.";
            }
            else if (player.StatPoints < points)
            {
                errorMessage = "Not enough stat points available.";
            }
            else if (!Enum.IsDefined(typeof(StatType), statByte))
            {
                errorMessage = "Invalid stat type.";
            }
            else
            {
                StatType statType = (StatType)statByte;
                player.StatPoints -= points;

                switch (statType)
                {
                    case StatType.Strength:
                        player.Strength += points;
                        newStatValue = player.Strength;
                        break;
                    case StatType.Intelligence:
                        player.Intelligence += points;
                        newStatValue = player.Intelligence;
                        break;
                    case StatType.Constitution:
                        player.Constitution += points;
                        newStatValue = player.Constitution;
                        break;
                    case StatType.Knowledge:
                        player.Knowledge += points;
                        newStatValue = player.Knowledge;
                        break;
                }

                // Recalculate derived attributes
                player.CalculateDerivedStats();

                isSuccess = true;
                Console.WriteLine($"[Progression] {player.Name} allocated {points} points into {statType} (Now: {newStatValue}, Remaining: {player.StatPoints}).");

                // Asynchronously persist to database
                player.QueueSave();
            }

            // 1. Send AllocateStatPointResponse to client
            using Packet response = new Packet(OpCode.AllocateStatPointResponse);
            response.Write(isSuccess);
            response.Write(statByte);
            response.Write(newStatValue);
            response.Write(player.StatPoints);
            response.Write(errorMessage);
            client.Send(response);

            if (isSuccess)
            {
                // 2. Sync updated derived stats
                using Packet statsPacket = new Packet(OpCode.StatsUpdate);
                statsPacket.Write(player.MaxHealth);
                statsPacket.Write(player.MaxMana);
                statsPacket.Write(player.Attack);
                statsPacket.Write(player.MagicAttack);
                statsPacket.Write(player.Defense);
                statsPacket.Write(player.MagicDefense);
                client.Send(statsPacket);

                // 3. Sync vitals (MaxHealth or MaxMana might have increased)
                using Packet vitalsPacket = new Packet(OpCode.VitalsUpdate);
                vitalsPacket.Write(player.Id);
                vitalsPacket.Write(player.Health);
                vitalsPacket.Write(player.Mana);
                client.Send(vitalsPacket);
            }
        }
    }
}
