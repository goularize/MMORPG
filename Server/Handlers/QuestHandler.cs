using System.Linq;
using Server.Network;
using Server.Quests;
using Server.World;
using Server.World.Entities;
using Shared.Enums;
using Shared.Network;

namespace Server.Handlers
{
    /// <summary>Quest packets. Accepting and turning in happen through dialogue options (see DialogueService).</summary>
    public static class QuestHandler
    {
        public static void HandleAbandon(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;

            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || player.Health <= 0) return;

            QuestService.Abandon(player, packet.ReadInt());
        }

        /// <summary>Sends one quest's state and counters; a quest that is not in the log is sent as Available (it left the log).</summary>
        public static void SendQuestUpdate(Player player, int questId)
        {
            using Packet packet = new Packet(OpCode.QuestUpdate);
            packet.Write(questId);
            WriteEntry(packet, player.Progress.GetQuest(questId));
            player.Connection.Send(packet);
        }

        /// <summary>Sends the whole quest log; part of the state sent when a character enters the world.</summary>
        public static void SendQuestLog(Player player)
        {
            var entries = player.Progress.Quests.OrderBy(q => q.Key).ToList();

            using Packet packet = new Packet(OpCode.QuestLogSync);
            packet.Write(entries.Count);
            foreach (var (questId, entry) in entries)
            {
                packet.Write(questId);
                WriteEntry(packet, entry);
            }
            player.Connection.Send(packet);
        }

        private static void WriteEntry(Packet packet, QuestProgress? entry)
        {
            packet.Write((byte)(entry?.State ?? QuestState.Available));

            int count = entry?.Objectives.Count ?? 0;
            packet.Write((byte)count);
            for (int i = 0; i < count; i++) packet.Write(entry!.Objectives[i]);
        }
    }
}
