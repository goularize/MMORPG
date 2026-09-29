using System;
using System.Linq;
using Shared.Network;
using Shared.Math;
using Server.Network;
using Server.World;

namespace Server.Handlers
{
    public static class ChatHandler
    {
        public static void HandleChatMessage(IClientConnection client, Packet packet)
        {
            if (client.PlayerId == null) return;

            // Resolve the sender
            if (!GameLogic.EntityMgr.Players.TryGetValue(client.PlayerId.Value, out var sender))
            {
                return;
            }

            ChatChannel channel = (ChatChannel)packet.ReadByte();
            string message = packet.ReadString();

            string targetName = "";
            if (channel == ChatChannel.Whisper)
            {
                targetName = packet.ReadString();
            }

            // Clean/Sanitize message here if needed
            if (string.IsNullOrWhiteSpace(message)) return;

            switch (channel)
            {
                case ChatChannel.Global:
                    BroadcastGlobal(sender.Name, message);
                    break;
                case ChatChannel.Local:
                    BroadcastLocal(sender, message);
                    break;
                case ChatChannel.Whisper:
                    SendWhisper(sender, targetName, message);
                    break;
            }
        }

        private static void BroadcastGlobal(string senderName, string message)
        {
            using Packet broadcast = new Packet(OpCode.ChatMessageBroadcast);
            broadcast.Write((byte)ChatChannel.Global);
            broadcast.Write(senderName);
            broadcast.Write(message);

            foreach (var player in GameLogic.EntityMgr.Players.Values)
            {
                player.Connection.Send(broadcast);
            }
            
            Console.WriteLine($"[Global] {senderName}: {message}");
        }

        private static void BroadcastLocal(Server.World.Entities.Player sender, string message)
        {
            using Packet broadcast = new Packet(OpCode.ChatMessageBroadcast);
            broadcast.Write((byte)ChatChannel.Local);
            broadcast.Write(sender.Name);
            broadcast.Write(message);

            float chatRadius = 50.0f; // Could be larger or smaller than AoI

            foreach (var target in GameLogic.EntityMgr.Players.Values)
            {
                if (Vector3.Distance(sender.Position, target.Position) <= chatRadius)
                {
                    target.Connection.Send(broadcast);
                }
            }
            
            Console.WriteLine($"[Local] {sender.Name}: {message}");
        }

        private static void SendWhisper(Server.World.Entities.Player sender, string targetName, string message)
        {
            var target = GameLogic.EntityMgr.Players.Values.FirstOrDefault(p => p.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

            if (target != null)
            {
                // Send to Target
                using Packet broadcast = new Packet(OpCode.ChatMessageBroadcast);
                broadcast.Write((byte)ChatChannel.Whisper);
                broadcast.Write(sender.Name);
                broadcast.Write(message);
                target.Connection.Send(broadcast);

                // Echo back to Sender so they see their own message
                using Packet echo = new Packet(OpCode.ChatMessageBroadcast);
                echo.Write((byte)ChatChannel.Whisper);
                echo.Write($"To {target.Name}");
                echo.Write(message);
                sender.Connection.Send(echo);
                
                Console.WriteLine($"[Whisper] {sender.Name} -> {target.Name}: {message}");
            }
            else
            {
                // Target not found
                using Packet error = new Packet(OpCode.ChatMessageBroadcast);
                error.Write((byte)ChatChannel.System);
                error.Write("System");
                error.Write($"Player '{targetName}' is not online.");
                sender.Connection.Send(error);
            }
        }
    }
}
