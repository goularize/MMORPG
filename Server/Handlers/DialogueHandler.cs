using Server.Dialogue;
using Server.Network;
using Server.World;
using Shared.Network;

namespace Server.Handlers
{
    /// <summary>Dialogue packets: the client picks one of the options it was offered, or closes its window. Opening is done by InteractHandler.</summary>
    public static class DialogueHandler
    {
        public static void HandleChoose(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;

            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || !ReferenceEquals(player.Connection, client)) return;

            int npcId = packet.ReadInt();
            int optionId = packet.ReadInt();
            DialogueService.Choose(player, npcId, optionId);
        }

        public static void HandleClose(IClientConnection client, Packet packet)
        {
            if (!client.PlayerId.HasValue) return;

            var player = GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            if (player == null || !ReferenceEquals(player.Connection, client)) return;

            DialogueService.ClientClosed(player, packet.ReadInt());
        }
    }
}
