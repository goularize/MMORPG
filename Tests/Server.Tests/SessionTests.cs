using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Handlers;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    public class SessionTests : IDisposable
    {
        private string _dbName = Guid.NewGuid().ToString();

        public SessionTests()
        {
            UseFreshDb();
            GameLogic.Commands.DrainAll();
        }

        public void Dispose()
        {
            GameLogic.Commands.DrainAll();
        }

        private void UseFreshDb()
        {
            _dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () =>
            {
                var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
                var ctx = new AppDbContext(options);
                ctx.Database.EnsureCreated();
                return ctx;
            };
        }

        private static byte[] SignIn(string username, string password)
        {
            using var write = new Packet(OpCode.SignInRequest);
            write.Write(ServerConfig.GameVersion);
            write.Write(username);
            write.Write(password);
            return write.ToArray();
        }

        private static byte[] Select(int characterId)
        {
            using var write = new Packet(OpCode.CharacterSelectRequest);
            write.Write(characterId);
            return write.ToArray();
        }

        private static byte[] Delete(int characterId)
        {
            using var write = new Packet(OpCode.CharacterDeleteRequest);
            write.Write(characterId);
            return write.ToArray();
        }

        private static void Handle(IClientConnection client, byte[] bytes)
        {
            PacketHandler.HandlePacket(client, bytes);
        }

        private int AddAccount(string username, string password)
        {
            using var db = AppDbContext.Factory();
            var account = new Account { Username = username, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 4), CharacterSlots = 3 };
            db.Accounts.Add(account);
            db.SaveChanges();
            return account.Id;
        }

        private void AddCharacter(int id, int accountId, int mapId = 1)
        {
            using var db = AppDbContext.Factory();
            db.Characters.Add(new Character { Id = id, AccountId = accountId, Name = "Hero" + id, Health = 50, MapId = mapId });
            db.SaveChanges();
        }

        private static bool LastResponseWasSuccess(TestClient client, OpCode opCode)
        {
            var response = client.SentPackets.Last(p => p.PacketId == opCode);
            return response.ReadBool();
        }

        [Fact]
        public void SessionRegistry_BindingTheSameAccountAgain_DisconnectsThePreviousSession()
        {
            var first = new TestClient(9301);
            var second = new TestClient(9302);

            Assert.Null(SessionRegistry.Bind(9300, first));
            var replaced = SessionRegistry.Bind(9300, second);

            Assert.Same(first, replaced);
            Assert.False(first.IsConnected);
            Assert.True(second.IsConnected);
            Assert.Same(second, SessionRegistry.GetSession(9300));
            SessionRegistry.Release(9300, second);
        }

        [Fact]
        public void SessionRegistry_RebindingTheSameConnection_DoesNotDisconnectIt_AndOtherAccountsAreIndependent()
        {
            var client = new TestClient(9303);
            var other = new TestClient(9304);

            SessionRegistry.Bind(9310, client);
            Assert.Null(SessionRegistry.Bind(9310, client));
            Assert.Null(SessionRegistry.Bind(9311, other));

            Assert.True(client.IsConnected);
            Assert.True(other.IsConnected);
            SessionRegistry.Release(9310, client);
            SessionRegistry.Release(9311, other);
        }

        [Fact]
        public void SessionRegistry_ReleaseByAStaleConnection_DoesNotDropTheCurrentSession()
        {
            var stale = new TestClient(9305);
            var current = new TestClient(9306);
            SessionRegistry.Bind(9312, stale);
            SessionRegistry.Bind(9312, current);

            SessionRegistry.Release(9312, stale); // e.g. the old connection's disconnect callback arriving late

            Assert.Same(current, SessionRegistry.GetSession(9312));
            SessionRegistry.Release(9312, current);
            Assert.Null(SessionRegistry.GetSession(9312));
        }

        [Fact]
        public void SignIn_OnASecondConnection_KicksTheFirstConnectionOfTheSameAccount()
        {
            int accountId = AddAccount("sharedacct", "pw12345");
            var first = new TestClient(9307) { AccountId = null };
            var second = new TestClient(9308) { AccountId = null };

            Handle(first, SignIn("sharedacct", "pw12345"));
            Assert.True(LastResponseWasSuccess(first, OpCode.SignInResponse));
            Assert.True(first.IsConnected);

            Handle(second, SignIn("sharedacct", "pw12345"));

            Assert.False(first.IsConnected);
            Assert.True(second.IsConnected);
            Assert.Equal(accountId, second.AccountId);
            SessionRegistry.Release(accountId, second);
        }

        [Fact]
        public void SignIn_WithWrongPassword_DoesNotKickTheExistingSession()
        {
            int accountId = AddAccount("victim", "right-pw");
            var legit = new TestClient(9309) { AccountId = null };
            var attacker = new TestClient(9313) { AccountId = null };
            Handle(legit, SignIn("victim", "right-pw"));

            Handle(attacker, SignIn("victim", "wrong-pw"));

            Assert.True(legit.IsConnected);
            Assert.Null(attacker.AccountId);
            SessionRegistry.Release(accountId, legit);
        }

        [Fact]
        public void SignIn_WhileAlreadyInTheWorld_IsRefused()
        {
            AddAccount("inworld", "pw12345");
            var client = new TestClient(9314) { AccountId = null, PlayerId = 9314 };

            Handle(client, SignIn("inworld", "pw12345"));

            Assert.False(LastResponseWasSuccess(client, OpCode.SignInResponse));
            Assert.Null(client.AccountId);
        }

        [Fact]
        public void Select_SecondCharacterOnTheSameConnection_IsRefused_AndTheFirstStaysIntact()
        {
            int accountId = AddAccount("multi", "pw12345");
            AddCharacter(9401, accountId);
            AddCharacter(9402, accountId);
            var client = new TestClient(9315) { AccountId = accountId };

            Handle(client, Select(9401));
            GameLogic.Commands.DrainAll();
            Handle(client, Select(9402));
            GameLogic.Commands.DrainAll();

            Assert.Equal(9401, client.PlayerId);
            Assert.NotNull(GameLogic.MapMgr.GetPlayer(9401));
            Assert.Null(GameLogic.MapMgr.GetPlayer(9402));
            Assert.False(LastResponseWasSuccess(client, OpCode.CharacterSelectResponse)); // the refusal
            GameLogic.MapMgr.RemovePlayer(9401, savePosition: false);
        }

        [Fact]
        public void Select_TwoSelectsBeforeTheGameLoopRuns_OnlyTheFirstIsAccepted()
        {
            int accountId = AddAccount("rapid", "pw12345");
            AddCharacter(9403, accountId);
            AddCharacter(9404, accountId);
            var client = new TestClient(9316) { AccountId = accountId };

            Handle(client, Select(9403));
            Handle(client, Select(9404)); // arrives before the first world entry has run
            GameLogic.Commands.DrainAll();

            Assert.NotNull(GameLogic.MapMgr.GetPlayer(9403));
            Assert.Null(GameLogic.MapMgr.GetPlayer(9404));
            Assert.Equal(9403, client.PlayerId);
            GameLogic.MapMgr.RemovePlayer(9403, savePosition: false);
        }

        [Fact]
        public void Delete_OfTheCharacterInTheWorld_IsRefused_ButOtherCharactersCanBeDeleted()
        {
            int accountId = AddAccount("deleter", "pw12345");
            AddCharacter(9405, accountId);
            AddCharacter(9406, accountId);
            var client = new TestClient(9317) { AccountId = accountId };
            Handle(client, Select(9405));
            GameLogic.Commands.DrainAll();

            Handle(client, Delete(9405));
            Assert.False(LastResponseWasSuccess(client, OpCode.CharacterDeleteResponse));

            Handle(client, Delete(9406));
            Assert.True(LastResponseWasSuccess(client, OpCode.CharacterDeleteResponse));

            using var db = AppDbContext.Factory();
            Assert.NotNull(db.Characters.Find(9405));
            Assert.Null(db.Characters.Find(9406));
            GameLogic.MapMgr.RemovePlayer(9405, savePosition: false);
        }

        [Fact]
        public void Delete_WhileTheWorldEntryIsStillPending_IsRefused()
        {
            int accountId = AddAccount("pendingdel", "pw12345");
            AddCharacter(9407, accountId);
            var client = new TestClient(9318) { AccountId = accountId };

            Handle(client, Select(9407));
            Handle(client, Delete(9407)); // before the game loop has put the character in the world

            Assert.False(LastResponseWasSuccess(client, OpCode.CharacterDeleteResponse));
            GameLogic.Commands.DrainAll();
            GameLogic.MapMgr.RemovePlayer(9407, savePosition: false);
        }

        [Fact]
        public void Select_PutsTheCharacterOnItsSavedMap_AndReportsThatMapToTheClient()
        {
            int accountId = AddAccount("mapper", "pw12345");
            AddCharacter(9408, accountId, mapId: 2);
            var client = new TestClient(9319) { AccountId = accountId };

            Handle(client, Select(9408));
            GameLogic.Commands.DrainAll();

            Assert.True(GameLogic.MapMgr.GetMap(2)!.Players.ContainsKey(9408));
            var response = client.SentPackets.First(p => p.PacketId == OpCode.CharacterSelectResponse);
            Assert.True(response.ReadBool());
            Assert.Equal(2, response.ReadInt());
            GameLogic.MapMgr.RemovePlayer(9408, savePosition: false);
        }

        [Fact]
        public void Select_WhenTheSavedMapDoesNotExist_ReportsFailureInsteadOfALimboPlayer()
        {
            int accountId = AddAccount("lost", "pw12345");
            AddCharacter(9409, accountId, mapId: 987);
            var client = new TestClient(9320) { AccountId = accountId };

            Handle(client, Select(9409));
            GameLogic.Commands.DrainAll();

            Assert.False(LastResponseWasSuccess(client, OpCode.CharacterSelectResponse));
            Assert.Null(client.PlayerId); // free to pick again
            Assert.Null(GameLogic.MapMgr.GetPlayer(9409));
        }

        [Fact]
        public void RemovePlayerOwnedBy_NeverRemovesAPlayerThatBelongsToANewerSession()
        {
            var staleClient = new TestClient(9321) { PlayerId = 9410 };
            var newClient = new TestClient(9322) { PlayerId = 9410 };
            var newerPlayer = new Player(9410, "Reconnected", newClient) { Position = new Vector3(0, 0, 0) };
            GameLogic.MapMgr.AddPlayer(newerPlayer);

            Assert.False(GameLogic.MapMgr.RemovePlayerOwnedBy(staleClient)); // the old session's late disconnect
            Assert.NotNull(GameLogic.MapMgr.GetPlayer(9410));

            Assert.True(GameLogic.MapMgr.RemovePlayerOwnedBy(newClient));
            Assert.Null(GameLogic.MapMgr.GetPlayer(9410));
        }

        [Fact]
        public void MapAddPlayer_AlreadyOnlinePlayer_IsReportedNotSilentlyIgnored()
        {
            var first = new Player(9411, "First", new TestClient(9323));
            var duplicate = new Player(9411, "Duplicate", new TestClient(9324));

            Assert.True(GameLogic.MapMgr.AddPlayer(first));
            Assert.False(GameLogic.MapMgr.AddPlayer(duplicate));
            Assert.Same(first, GameLogic.MapMgr.GetPlayer(9411));
            GameLogic.MapMgr.RemovePlayer(9411, savePosition: false);
        }

        // Real sockets: the second sign-in of an account closes the first connection's socket
        [Fact]
        public void EndToEnd_OverTcp_SigningInAgainClosesTheOlderConnection()
        {
            AddAccount("tcpuser", "pw12345");
            const int port = 17878;
            PacketHandler.Initialize();
            var server = new GameServer(port);
            var logic = new GameLogic();
            server.Start();
            logic.Start();
            try
            {
                using var first = new TcpClient("127.0.0.1", port);
                first.GetStream().Write(SignIn("tcpuser", "pw12345"));
                Assert.True(ReadResponseSuccess(first), "first sign-in should succeed");

                using var second = new TcpClient("127.0.0.1", port);
                second.GetStream().Write(SignIn("tcpuser", "pw12345"));
                Assert.True(ReadResponseSuccess(second), "second sign-in should succeed");

                first.ReceiveTimeout = 3000;
                Assert.True(IsClosedByServer(first), "the first connection should have been closed by the server");
            }
            finally
            {
                logic.Stop();
                server.Stop();
                GameLogic.Commands.DrainAll();
            }
        }

        private static bool ReadResponseSuccess(TcpClient client)
        {
            client.ReceiveTimeout = 5000;
            var stream = client.GetStream();
            var header = new byte[2];
            ReadExactly(stream, header);
            var body = new byte[BitConverter.ToUInt16(header, 0) - 2];
            ReadExactly(stream, body);
            using var packet = new Packet(header.Concat(body).ToArray());
            return packet.ReadBool();
        }

        private static void ReadExactly(NetworkStream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0) throw new InvalidOperationException("connection closed");
                read += n;
            }
        }

        private static bool IsClosedByServer(TcpClient client)
        {
            try
            {
                return client.GetStream().Read(new byte[16], 0, 16) == 0;
            }
            catch (System.IO.IOException)
            {
                return true;
            }
        }
    }
}
