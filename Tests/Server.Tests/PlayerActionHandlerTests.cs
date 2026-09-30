using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Network;
using Shared.Math;
using Xunit;
using System.Collections.Generic;

namespace Server.Tests
{
    public class PlayerActionHandlerTests : IDisposable
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

        public PlayerActionHandlerTests()
        {
            // Reset MapManager state
            GameLogic.MapMgr.ActiveMaps.Clear();
            GameLogic.MapMgr.ActiveMaps.TryAdd(1, new MapInstance(1));
            GameLogic.MapMgr.ActiveMaps.TryAdd(2, new MapInstance(2));
        }

        public void Dispose()
        {
            // Reset factory to default after test
            AppDbContext.Factory = () => new AppDbContext();
        }

        [Fact]
        public void HandleRespawnRequest_ShouldReviveAndTeleport_WhenPlayerIsDead()
        {
            // Arrange
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);
            
            using var setupDb = AppDbContext.Factory();
            var account = new Account { Username = "TestRespawn", PasswordHash = "Hash" };
            setupDb.Accounts.Add(account);
            setupDb.SaveChanges();

            var character = new Character
            {
                AccountId = account.Id,
                Name = "DeadHero",
                Health = 0,
                MapId = 2,
                X = 50f, Y = 50f, Z = 50f,
                BindMapId = 1,
                BindX = 10f, BindY = 10f, BindZ = 10f
            };
            setupDb.Characters.Add(character);
            setupDb.SaveChanges();

            var client = new MockClientConnection { AccountId = account.Id, PlayerId = character.Id };
            var player = new Player(character.Id, character.Name, client)
            {
                MaxHealth = 100,
                Health = 0,
                MapId = 2,
                Position = new Vector3(50f, 50f, 50f),
                BindMapId = 1,
                BindPosition = new Vector3(10f, 10f, 10f)
            };

            GameLogic.MapMgr.AddPlayer(player);

            using var writePacket = new Packet(OpCode.PlayerRespawnRequest);
            using var packet = new Packet(writePacket.ToArray());

            // Act
            PlayerActionHandler.HandleRespawnRequest(client, packet);

            // Assert State
            Assert.Equal(100, player.Health);
            Assert.Equal(1, player.MapId);
            Assert.Equal(10f, player.Position.X);
            Assert.Equal(10f, player.Position.Y);
            Assert.Equal(10f, player.Position.Z);

            // Assert Map Locations
            Assert.False(GameLogic.MapMgr.GetMap(2)?.Players.ContainsKey(player.Id), "Player should be removed from Map 2");
            Assert.True(GameLogic.MapMgr.GetMap(1)?.Players.ContainsKey(player.Id), "Player should be added to Map 1");

            // Assert DB Persistence
            using var assertDb = AppDbContext.Factory();
            var savedChar = assertDb.Characters.Find(player.Id);
            Assert.NotNull(savedChar);
            Assert.Equal(100, savedChar.Health);
            Assert.Equal(1, savedChar.MapId);
            Assert.Equal(10f, savedChar.X);

            // Assert Packets
            Assert.Contains(client.SentPackets, p => p.PacketId == OpCode.PlayerRespawnResponse);
            Assert.Contains(client.SentPackets, p => p.PacketId == OpCode.VitalsUpdate);
        }

        [Fact]
        public void HandleRespawnRequest_ShouldDoNothing_WhenPlayerIsAlive()
        {
            // Arrange
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => GetInMemoryDbContext(dbName);
            
            var client = new MockClientConnection { AccountId = 1, PlayerId = 100 };
            var player = new Player(100, "AliveHero", client)
            {
                MaxHealth = 100,
                Health = 50, // Alive!
                MapId = 2,
                Position = new Vector3(50f, 50f, 50f),
                BindMapId = 1,
                BindPosition = new Vector3(10f, 10f, 10f)
            };

            GameLogic.MapMgr.AddPlayer(player);

            using var writePacket = new Packet(OpCode.PlayerRespawnRequest);
            using var packet = new Packet(writePacket.ToArray());

            int initialPacketCount = client.SentPackets.Count;

            // Act
            PlayerActionHandler.HandleRespawnRequest(client, packet);

            // Assert State (Should not have changed)
            Assert.Equal(50, player.Health);
            Assert.Equal(2, player.MapId);
            
            // Should still be in Map 2
            Assert.True(GameLogic.MapMgr.GetMap(2)?.Players.ContainsKey(player.Id));

            // No new packets should have been sent
            Assert.Equal(initialPacketCount, client.SentPackets.Count);
        }
    }
}
