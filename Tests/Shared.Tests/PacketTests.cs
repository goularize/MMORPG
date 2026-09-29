using System;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Shared.Tests
{
    public class PacketTests
    {
        [Fact]
        public void Packet_ShouldSerializeAndDeserialize_Correctly()
        {
            // 1. Arrange & Act (Write Data)
            byte[] packetData;
            using (var writePacket = new Packet(OpCode.EntitySpawn))
            {
                writePacket.Write(42);                      // EntityId
                writePacket.Write("Orc Grunt");             // Name
                writePacket.Write(new Vector3(10, 5, 2));   // Position
                
                packetData = writePacket.ToArray();
            }
            
            // 2. Act & Assert (Read Data)
            using (var readPacket = new Packet(packetData))
            {
                // Verify OpCode was framed correctly
                Assert.Equal(OpCode.EntitySpawn, readPacket.PacketId);
                
                // Verify Payload
                int readId = readPacket.ReadInt();
                string readName = readPacket.ReadString();
                Vector3 readPos = readPacket.ReadVector3();
                
                Assert.Equal(42, readId);
                Assert.Equal("Orc Grunt", readName);
                Assert.Equal(10f, readPos.X);
                Assert.Equal(5f, readPos.Y);
                Assert.Equal(2f, readPos.Z);
            }
        }
    }
}
