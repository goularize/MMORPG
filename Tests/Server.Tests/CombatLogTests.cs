using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class CombatLogTests : IDisposable
    {
        public CombatLogTests()
        {
            PacketHandler.Initialize();
            GameLogic.Commands.DrainAll();
        }

        public void Dispose()
        {
            GameLogic.Commands.DrainAll();
        }

        private static Player AddPlayer(int id, TestClient client)
        {
            client.PlayerId = id;
            var player = new Player(id, "Fighter" + id, client) { Position = new Vector3(0, 0, 0), Health = 50, MaxHealth = 50, AccountId = client.AccountId ?? 0 };
            Assert.True(GameLogic.MapMgr.AddPlayer(player));
            return player;
        }

        private static void Cleanup(int id) => GameLogic.MapMgr.RemovePlayer(id, saveState: false);

        [Fact]
        public void DisconnectOutOfCombat_RemovesThePlayerImmediately()
        {
            var client = new TestClient(9501);
            AddPlayer(9501, client);

            Assert.True(GameLogic.MapMgr.RemovePlayerOwnedBy(client));

            Assert.Null(GameLogic.MapMgr.GetPlayer(9501));
        }

        [Fact]
        public void DisconnectInCombat_LeavesThePlayerLingeringWithoutAConnection()
        {
            var client = new TestClient(9502);
            var player = AddPlayer(9502, client);
            player.MarkInCombat();

            Assert.True(GameLogic.MapMgr.RemovePlayerOwnedBy(client));

            Assert.Same(player, GameLogic.MapMgr.GetPlayer(9502));
            Assert.True(player.IsLingering);
            Assert.False(player.Connection.IsConnected);
            Assert.InRange((player.LingerUntilUtc!.Value - DateTime.UtcNow).TotalSeconds, ServerConfig.CombatLogoutLingerSeconds - 2, ServerConfig.CombatLogoutLingerSeconds + 0.1);
            Cleanup(9502);
        }

        [Fact]
        public void HarvestingAResource_IsNotCombat()
        {
            var client = new TestClient(9503);
            var player = AddPlayer(9503, client);
            player.MarkRegenBlocked(); // what chopping a tree does

            Assert.True(player.IsRegenBlockedByCombat);
            Assert.False(player.IsCombatTagged);
            GameLogic.MapMgr.RemovePlayerOwnedBy(client);
            Assert.Null(GameLogic.MapMgr.GetPlayer(9503));
        }

        [Fact]
        public void CombatOlderThanTheTag_DoesNotLinger()
        {
            var client = new TestClient(9504);
            var player = AddPlayer(9504, client);
            player.LastCombatTagTime = DateTime.UtcNow.AddSeconds(-(ServerConfig.CombatTagSeconds + 1));

            GameLogic.MapMgr.RemovePlayerOwnedBy(client);

            Assert.Null(GameLogic.MapMgr.GetPlayer(9504));
        }

        [Fact]
        public void DeadPlayer_DoesNotLinger()
        {
            var client = new TestClient(9505);
            var player = AddPlayer(9505, client);
            player.MarkInCombat();
            player.Health = 0;

            GameLogic.MapMgr.RemovePlayerOwnedBy(client);

            Assert.Null(GameLogic.MapMgr.GetPlayer(9505));
        }

        [Fact]
        public void LingeringPlayer_LeavesWhenTheTimeIsUp_AndNotBefore()
        {
            var client = new TestClient(9506);
            var player = AddPlayer(9506, client);
            player.MarkInCombat();
            GameLogic.MapMgr.RemovePlayerOwnedBy(client);

            GameLogic.MapMgr.Update();
            Assert.NotNull(GameLogic.MapMgr.GetPlayer(9506)); // still within the window

            player.LingerUntilUtc = DateTime.UtcNow.AddSeconds(-1);
            GameLogic.MapMgr.Update();
            Assert.Null(GameLogic.MapMgr.GetPlayer(9506));
        }

        [Fact]
        public void LingeringPlayer_WhoDies_LeavesAtOnce()
        {
            var client = new TestClient(9507);
            var player = AddPlayer(9507, client);
            player.MarkInCombat();
            GameLogic.MapMgr.RemovePlayerOwnedBy(client);

            player.Health = 0; // killed by the mob that was fighting them
            GameLogic.MapMgr.Update();

            Assert.Null(GameLogic.MapMgr.GetPlayer(9507));
        }

        [Fact]
        public void DisabledLinger_RemovesImmediatelyEvenInCombat()
        {
            var client = new TestClient(9508);
            var player = AddPlayer(9508, client);
            player.MarkInCombat();
            Environment.SetEnvironmentVariable("COMBAT_LOGOUT_LINGER_SECONDS", "0");
            try
            {
                ServerConfig.Initialize();
                GameLogic.MapMgr.RemovePlayerOwnedBy(client);
            }
            finally
            {
                Environment.SetEnvironmentVariable("COMBAT_LOGOUT_LINGER_SECONDS", null);
                ServerConfig.Initialize();
            }

            Assert.Null(GameLogic.MapMgr.GetPlayer(9508));
        }

        [Fact]
        public void LateDisconnectOfTheStaleConnection_DoesNotAffectAReattachedPlayer()
        {
            var oldClient = new TestClient(9509);
            var player = AddPlayer(9509, oldClient);
            player.MarkInCombat();
            GameLogic.MapMgr.RemovePlayerOwnedBy(oldClient);

            var newClient = new TestClient(9510) { PlayerId = 9509 };
            player.Reattach(newClient);

            Assert.False(GameLogic.MapMgr.RemovePlayerOwnedBy(oldClient));
            Assert.False(player.IsLingering);
            Assert.Same(newClient, player.Connection);
            Assert.True(player.AoiResetRequested);
            Cleanup(9509);
        }

        [Fact]
        public void Reconnecting_WhileLingering_ResumesTheLivePlayerInsteadOfLoadingACopy()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () =>
            {
                var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
                ctx.Database.EnsureCreated();
                return ctx;
            };
            int accountId;
            using (var db = AppDbContext.Factory())
            {
                var account = new Account { Username = "logger", PasswordHash = "x", CharacterSlots = 3 };
                db.Accounts.Add(account);
                db.SaveChanges();
                accountId = account.Id;
                db.Characters.Add(new Character { Id = 9511, AccountId = accountId, Name = "Logger", Health = 50, MapId = 1 });
                db.SaveChanges();
            }

            var oldClient = new TestClient(9511) { AccountId = accountId };
            var live = AddPlayer(9511, oldClient);
            live.Health = 17; // mid-fight state that is not in the database
            live.MarkInCombat();
            GameLogic.MapMgr.RemovePlayerOwnedBy(oldClient);

            var newClient = new TestClient(9512) { AccountId = accountId };
            using var select = new Packet(OpCode.CharacterSelectRequest);
            select.Write(9511);
            PacketHandler.HandlePacket(newClient, select.ToArray());
            GameLogic.Commands.DrainAll();

            Assert.Same(live, GameLogic.MapMgr.GetPlayer(9511));
            Assert.Same(newClient, live.Connection);
            Assert.False(live.IsLingering);
            Assert.Equal(17, live.Health); // the live state, not the stale database row
            var response = newClient.SentPackets.Last(p => p.PacketId == OpCode.CharacterSelectResponse);
            Assert.True(response.ReadBool());
            Cleanup(9511);
        }

        [Fact]
        public void Reconnecting_ToACharacterThatIsConnectedElsewhere_IsStillRefused()
        {
            var otherAccountClient = new TestClient(9513) { AccountId = 1 };
            AddPlayer(9513, otherAccountClient);

            var player = GameLogic.MapMgr.GetPlayer(9513)!;
            Assert.False(player.IsLingering);
            Assert.False(GameLogic.MapMgr.AddPlayer(new Player(9513, "Dup", new TestClient(9514))));
            Cleanup(9513);
        }
    }
}
