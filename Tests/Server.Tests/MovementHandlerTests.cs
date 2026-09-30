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
    public class MovementHandlerTests
    {
        [Fact]
        public void HandleMoveRequest_ShouldUpdatePosition_WhenSpeedIsValid()
        {
            // Arrange
            var client = new MockClientConnection { AccountId = 1, PlayerId = 100 };
            var player = new Player(100, "Runner", client);
            player.Position = new Vector3(0, 0, 0);
            player.Health = 100;
            
            // Set last move time to 1 second ago
            player.LastMoveTime = System.DateTime.UtcNow.AddSeconds(-1);
            
            GameLogic.MapMgr.ActiveMaps[1].Players.Clear();
            GameLogic.MapMgr.AddPlayer(player);

            using var writePacket = new Packet(OpCode.PlayerMoveRequest);
            writePacket.Write(10.0f); // X (moved 10 units in 1 second, allowed)
            writePacket.Write(0.0f);  // Y
            writePacket.Write(0.0f);  // Z

            using var packet = new Packet(writePacket.ToArray());

            // Act
            MovementHandler.HandleMoveRequest(client, packet);

            // Assert
            Assert.Equal(10.0f, player.Position.X);
            Assert.Empty(client.SentPackets); // No rubberband packet sent
        }

        [Fact]
        public void HandleMoveRequest_ShouldRubberband_WhenSpeedIsTooFast()
        {
            // Arrange
            var client = new MockClientConnection { AccountId = 1, PlayerId = 100 };
            var player = new Player(100, "Hacker", client);
            player.Position = new Vector3(0, 0, 0);
            player.Health = 100;
            
            // Set last move time to 1 second ago
            player.LastMoveTime = System.DateTime.UtcNow.AddSeconds(-1);
            
            GameLogic.MapMgr.ActiveMaps[1].Players.Clear();
            GameLogic.MapMgr.AddPlayer(player);

            using var writePacket = new Packet(OpCode.PlayerMoveRequest);
            writePacket.Write(50.0f); // X (moved 50 units in 1 second, way too fast!)
            writePacket.Write(0.0f);  // Y
            writePacket.Write(0.0f);  // Z

            using var packet = new Packet(writePacket.ToArray());

            // Act
            MovementHandler.HandleMoveRequest(client, packet);

            // Assert
            // Position should NOT change
            Assert.Equal(0.0f, player.Position.X);
            
            // Should have sent a rubberband packet
            Assert.Single(client.SentPackets);
            var response = client.SentPackets[0];
            Assert.Equal(OpCode.EntityPositionUpdate, response.PacketId);
            
            int entityId = response.ReadInt();
            var rubberbandPos = response.ReadVector3();
            
            Assert.Equal(100, entityId);
            Assert.Equal(0.0f, rubberbandPos.X); // Tells client to snap back to 0
        }
    }
}
