using System.Linq;
using Server.Data;
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
            WriteEntry(packet, questId, player.Progress.GetQuest(questId));
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
                WriteEntry(packet, questId, entry);
            }
            player.Connection.Send(packet);
        }

        /// <summary>
        /// State, quest name and the objectives (text, current, required). The texts come from the quest data, so the
        /// client never needs it. A quest that left the log has no name and no objectives; a quest whose template was
        /// removed from the data still gets a valid entry from its saved counters.
        /// </summary>
        private static void WriteEntry(Packet packet, int questId, QuestProgress? entry)
        {
            packet.Write((byte)(entry?.State ?? QuestState.Available));

            if (entry == null)
            {
                packet.Write(string.Empty);
                packet.Write((byte)0);
                return;
            }

            if (DataManager.Quests.TryGetValue(questId, out var quest))
            {
                var counters = QuestService.NormalizedCounters(quest, entry.Objectives);
                packet.Write(quest.Name);
                packet.Write((byte)quest.Objectives.Count);
                for (int i = 0; i < quest.Objectives.Count; i++)
                {
                    packet.Write(QuestService.ObjectiveText(quest.Objectives[i]));
                    packet.Write(counters[i]);
                    packet.Write(quest.Objectives[i].Count);
                }
                return;
            }

            packet.Write("Unknown quest");
            packet.Write((byte)entry.Objectives.Count);
            for (int i = 0; i < entry.Objectives.Count; i++)
            {
                packet.Write($"Objective {i + 1}");
                packet.Write(entry.Objectives[i]);
                packet.Write(entry.Objectives[i]);
            }
        }
    }
}
