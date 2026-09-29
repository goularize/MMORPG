using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Server.Network;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class MockClientConnection : IClientConnection
    {
        public int Id { get; } = 1;
        public int? AccountId { get; set; } = 1; // Pre-authenticated for tests
        public List<Packet> SentPackets { get; } = new();

        public void Send(Packet packet)
        {
            // We must copy the packet data because the using block in the handler will dispose it
            var copy = new Packet(packet.ToArray());
            SentPackets.Add(copy);
        }

        public void Disconnect() { }
    }

    public class CharacterHandlerTests
    {
        private AppDbContext GetInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public void HandleCreateRequest_ShouldSendSuccess_WhenNameIsUnique()
        {
            // Arrange
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);
            
            var client = new MockClientConnection(); // AccountId is 1 by default
            
            using var writePacket = new Packet(OpCode.CharacterCreateRequest);
            writePacket.Write("Thrall"); // Name
            writePacket.Write(5);        // AppearanceId
            
            using var packet = new Packet(writePacket.ToArray());
            
            // Act
            CharacterHandler.HandleCreateRequest(client, packet);
            
            // Assert: Packet Sent
            Assert.Single(client.SentPackets);
            var response = client.SentPackets[0];
            Assert.Equal(OpCode.CharacterCreateResponse, response.PacketId);
            
            bool isSuccess = response.ReadBool();
            string message = response.ReadString();
            
            Assert.True(isSuccess);
            Assert.Equal("Character created successfully!", message);

            // Assert: Database state
            using var db = AppDbContext.Factory();
            var savedChar = db.Characters.FirstOrDefault(c => c.Name == "Thrall");
            Assert.NotNull(savedChar);
            Assert.Equal(1, savedChar.AccountId);
            Assert.Equal(5, savedChar.AppearanceId);
        }

        [Fact]
        public void HandleListRequest_ShouldReturnCharacters_ForSpecificAccount()
        {
            // Arrange
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);

            using (var db = AppDbContext.Factory())
            {
                db.Characters.Add(new Character { AccountId = 1, Name = "CharA", AppearanceId = 1, Level = 10 });
                db.Characters.Add(new Character { AccountId = 1, Name = "CharB", AppearanceId = 2, Level = 20 });
                db.Characters.Add(new Character { AccountId = 2, Name = "CharC", AppearanceId = 3, Level = 30 }); // Different account
                db.SaveChanges();
            }

            var client = new MockClientConnection { AccountId = 1 };
            using var writePacket = new Packet(OpCode.CharacterListRequest);
            using var packet = new Packet(writePacket.ToArray());

            // Act
            CharacterHandler.HandleListRequest(client, packet);

            // Assert
            Assert.Single(client.SentPackets);
            var response = client.SentPackets[0];
            Assert.Equal(OpCode.CharacterListResponse, response.PacketId);
            
            int count = response.ReadInt();
            Assert.Equal(2, count); // Should only see CharA and CharB

            // First character
            int id1 = response.ReadInt();
            string name1 = response.ReadString();
            int app1 = response.ReadInt();
            int lvl1 = response.ReadInt();
            Assert.Equal("CharA", name1);
            Assert.Equal(10, lvl1);

            // Second character
            int id2 = response.ReadInt();
            string name2 = response.ReadString();
            Assert.Equal("CharB", name2);
        }
    }
}
