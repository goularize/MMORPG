using System;
using Shared.Network;
using Server.Network;
using Server.World;
using Server.World.Entities;

namespace Server.Handlers
{
    /// <summary>
    /// Second half of entering the world. Selecting a character only tells the client which map to load; once the
    /// client has loaded it and spawned its local player it sends WorldReadyRequest, and only then does the server
    /// send the character's state and start replicating the world to it (area of interest).
    /// </summary>
    public static class WorldEntryHandler
    {
        public static void HandleWorldReady(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;

            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);

            // Only the connection that owns the character may ready it, and only once per world entry
            if (player == null || !ReferenceEquals(player.Connection, client) || !player.AwaitingWorldReady) return;

            player.MarkWorldReady();
            Console.WriteLine($"[Client {client.Id}] '{player.Name}' is ready in the world.");

            SendInitialState(client, player);
        }

        /// <summary>Sends everything the client needs about its own character. Nearby entities follow from the next AoI pass.</summary>
        public static void SendInitialState(IClientConnection client, Player player)
        {
            using Packet statsPacket = new Packet(OpCode.StatsUpdate);
            statsPacket.Write(player.MaxHealth);
            statsPacket.Write(player.MaxMana);
            statsPacket.Write(player.Attack);
            statsPacket.Write(player.MagicAttack);
            statsPacket.Write(player.Defense);
            statsPacket.Write(player.MagicDefense);
            client.Send(statsPacket);

            using Packet vitalsPacket = new Packet(OpCode.VitalsUpdate);
            vitalsPacket.Write(player.Id);
            vitalsPacket.Write(player.Health);
            vitalsPacket.Write(player.Mana);
            client.Send(vitalsPacket);

            // Sync Progression & Base Attributes
            long expToNextLevel = ServerConfig.GetExpForNextLevel(player.Level);
            using Packet progPacket = new Packet(OpCode.PlayerProgressionSync);
            progPacket.Write(player.Level);
            progPacket.Write(player.Exp);
            progPacket.Write(expToNextLevel);
            progPacket.Write(player.StatPoints);
            progPacket.Write(player.Strength);
            progPacket.Write(player.Intelligence);
            progPacket.Write(player.Constitution);
            progPacket.Write(player.Knowledge);
            client.Send(progPacket);

            // Sync Exp Bar specifically
            using Packet expPacket = new Packet(OpCode.PlayerExpUpdate);
            expPacket.Write(player.Id);
            expPacket.Write(player.Exp);
            expPacket.Write(expToNextLevel);
            client.Send(expPacket);

            // Sync Inventory & Equipment
            InventoryHandler.SendInventorySync(player);
            EquipmentHandler.SendEquippedItemsSync(player);

            // Sync the quest log
            QuestHandler.SendQuestLog(player);
        }
    }
}
