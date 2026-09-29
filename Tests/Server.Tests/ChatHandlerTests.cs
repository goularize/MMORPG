using System.Linq;
using Server.Handlers;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Network;
using Shared.Math;
using Xunit;

namespace Server.Tests
{
    public class ChatHandlerTests
    {
        private void SetupWorld(out MockClientConnection senderClient, out MockClientConnection receiverClient)
        {
            GameLogic.EntityMgr.Players.Clear();

            senderClient = new MockClientConnection { AccountId = 1, PlayerId = 1 };
            var sender = new Player(1, "Alice", senderClient) { Position = new Vector3(0, 0, 0) };
            GameLogic.EntityMgr.AddPlayer(sender);

            receiverClient = new MockClientConnection { AccountId = 2, PlayerId = 2 };
            var receiver = new Player(2, "Bob", receiverClient) { Position = new Vector3(10, 0, 0) }; // Close to Alice
            GameLogic.EntityMgr.AddPlayer(receiver);
        }

        [Fact]
        public void HandleChatMessage_Global_ShouldSendToAll()
        {
            SetupWorld(out var senderClient, out var receiverClient);

            using var writePacket = new Packet(OpCode.ChatMessageRequest);
            writePacket.Write((byte)ChatChannel.Global);
            writePacket.Write("Hello World!");
            using var packet = new Packet(writePacket.ToArray());

            ChatHandler.HandleChatMessage(senderClient, packet);

            // Alice should receive her own global message
            Assert.Single(senderClient.SentPackets);
            Assert.Equal(OpCode.ChatMessageBroadcast, senderClient.SentPackets[0].PacketId);

            // Bob should receive Alice's message
            Assert.Single(receiverClient.SentPackets);
            var response = receiverClient.SentPackets[0];
            Assert.Equal(OpCode.ChatMessageBroadcast, response.PacketId);
            
            ChatChannel channel = (ChatChannel)response.ReadByte();
            string senderName = response.ReadString();
            string message = response.ReadString();

            Assert.Equal(ChatChannel.Global, channel);
            Assert.Equal("Alice", senderName);
            Assert.Equal("Hello World!", message);
        }

        [Fact]
        public void HandleChatMessage_Local_ShouldOnlySendToNearby()
        {
            SetupWorld(out var senderClient, out var receiverClient);

            // Add Charlie who is far away
            var farClient = new MockClientConnection { AccountId = 3, PlayerId = 3 };
            var charlie = new Player(3, "Charlie", farClient) { Position = new Vector3(100, 0, 0) };
            GameLogic.EntityMgr.AddPlayer(charlie);

            using var writePacket = new Packet(OpCode.ChatMessageRequest);
            writePacket.Write((byte)ChatChannel.Local);
            writePacket.Write("Hello Locals!");
            using var packet = new Packet(writePacket.ToArray());

            ChatHandler.HandleChatMessage(senderClient, packet);

            Assert.Single(senderClient.SentPackets); // Alice receives her own
            Assert.Single(receiverClient.SentPackets); // Bob receives it (distance = 10 <= 50)
            Assert.Empty(farClient.SentPackets); // Charlie does not receive it (distance = 100 > 50)
        }

        [Fact]
        public void HandleChatMessage_Whisper_ShouldSendToTargetAndEcho()
        {
            SetupWorld(out var senderClient, out var receiverClient);

            using var writePacket = new Packet(OpCode.ChatMessageRequest);
            writePacket.Write((byte)ChatChannel.Whisper);
            writePacket.Write("Hello Bob!");
            writePacket.Write("Bob"); // Target name
            using var packet = new Packet(writePacket.ToArray());

            ChatHandler.HandleChatMessage(senderClient, packet);

            // Bob receives the Whisper
            Assert.Single(receiverClient.SentPackets);
            var response = receiverClient.SentPackets[0];
            
            ChatChannel channel = (ChatChannel)response.ReadByte();
            string senderName = response.ReadString();
            string message = response.ReadString();

            Assert.Equal(ChatChannel.Whisper, channel);
            Assert.Equal("Alice", senderName);
            Assert.Equal("Hello Bob!", message);

            // Alice receives an echo
            Assert.Single(senderClient.SentPackets);
            var echo = senderClient.SentPackets[0];
            
            ChatChannel echoChannel = (ChatChannel)echo.ReadByte();
            string echoName = echo.ReadString();
            
            Assert.Equal(ChatChannel.Whisper, echoChannel);
            Assert.Equal("To Bob", echoName);
        }

        [Fact]
        public void HandleChatMessage_Whisper_ShouldReturnSystemError_IfTargetOffline()
        {
            SetupWorld(out var senderClient, out var receiverClient);

            using var writePacket = new Packet(OpCode.ChatMessageRequest);
            writePacket.Write((byte)ChatChannel.Whisper);
            writePacket.Write("Hello Charlie!");
            writePacket.Write("Charlie"); // Target name (offline)
            using var packet = new Packet(writePacket.ToArray());

            ChatHandler.HandleChatMessage(senderClient, packet);

            Assert.Empty(receiverClient.SentPackets);

            Assert.Single(senderClient.SentPackets);
            var error = senderClient.SentPackets[0];
            
            ChatChannel errorChannel = (ChatChannel)error.ReadByte();
            string errorName = error.ReadString();
            string errorMessage = error.ReadString();

            Assert.Equal(ChatChannel.System, errorChannel);
            Assert.Equal("System", errorName);
            Assert.Equal("Player 'Charlie' is not online.", errorMessage);
        }
    }
}
