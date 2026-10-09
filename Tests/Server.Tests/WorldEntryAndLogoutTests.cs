using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Server.Database;
using Server.Database.Models;
using Server.Network;
using Server.World;
using Server.World.Entities;
using Shared.Constants;
using Shared.Enums;
using Shared.Math;
using Shared.Network;
using Xunit;

namespace Server.Tests
{
    /// <summary>The WorldReady handshake (#177) and the logout back to character select (#184).</summary>
    public class WorldEntryAndLogoutTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly System.Collections.Generic.List<int> _ids = new();

        public WorldEntryAndLogoutTests()
        {
            PacketHandler.Initialize();
            GameLogic.Commands.DrainAll();
            AppDbContext.Factory = () =>
            {
                var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options);
                ctx.Database.EnsureCreated();
                return ctx;
            };
            using var db = AppDbContext.Factory();
            db.Accounts.Add(new Account { Id = 1, Username = "wanderer", PasswordHash = "x", CharacterSlots = 3 });
            db.SaveChanges();
        }

        public void Dispose()
        {
            foreach (var id in _ids) GameLogic.MapMgr.RemovePlayer(id, saveState: false);
            GameLogic.Commands.DrainAll();
            Environment.SetEnvironmentVariable("WORLD_READY_TIMEOUT_SECONDS", null);
            Environment.SetEnvironmentVariable("COMBAT_LOGOUT_LINGER_SECONDS", null);
            ServerConfig.Initialize();
        }

        private void AddCharacter(int id)
        {
            _ids.Add(id);
            using var db = AppDbContext.Factory();
            db.Characters.Add(new Character { Id = id, AccountId = 1, Name = "Hero" + id, Level = 3, Health = 50, Mana = 20, MapId = 1 });
            db.SaveChanges();
        }

        /// <summary>Runs the real select flow: the player is in the world but its client has not said it is ready.</summary>
        private Player Select(TestClient client, int characterId)
        {
            AddCharacter(characterId);
            return SelectExisting(client, characterId);
        }

        private static Player SelectExisting(TestClient client, int characterId)
        {
            using var select = new Packet(OpCode.CharacterSelectRequest);
            select.Write(characterId);
            PacketHandler.HandlePacket(client, select.ToArray());
            GameLogic.Commands.DrainAll();
            return GameLogic.MapMgr.GetPlayer(characterId)!;
        }

        private static void Send(TestClient client, OpCode op, Action<Packet>? fill = null)
        {
            using var packet = new Packet(op);
            fill?.Invoke(packet);
            PacketHandler.HandlePacket(client, packet.ToArray());
        }

        private static void Ready(TestClient client) => Send(client, OpCode.WorldReadyRequest);

        private Player AddReadyPlayer(int id, TestClient client, Vector3 position)
        {
            _ids.Add(id);
            client.PlayerId = id;
            var player = new Player(id, "Watcher" + id, client) { Position = position, Health = 50, MaxHealth = 50, AccountId = 1 };
            Assert.True(GameLogic.MapMgr.AddPlayer(player));
            return player;
        }

        private static bool Received(TestClient client, OpCode op) => client.SentPackets.Any(p => p.PacketId == op);

        private static bool SpawnedPlayer(TestClient client, int playerId)
            => client.SentPackets.Where(p => p.PacketId == OpCode.EntitySpawn).Any(p => p.ReadInt() == playerId);

        private static LogoutResult LastLogoutResult(TestClient client, out float lingerSeconds)
        {
            var response = client.SentPackets.Last(p => p.PacketId == OpCode.LogoutResponse);
            var result = (LogoutResult)response.ReadByte();
            lingerSeconds = response.ReadFloat();
            return result;
        }

        // --- #177: WorldReady handshake ---

        [Fact]
        public void SelectingACharacter_OnlyTellsTheClientWhichMapToLoad()
        {
            var client = new TestClient(9601);
            var player = Select(client, 9601);

            Assert.True(player.AwaitingWorldReady);
            Assert.Single(client.SentPackets);
            var response = client.SentPackets[0];
            Assert.Equal(OpCode.CharacterSelectResponse, response.PacketId);
            Assert.True(response.ReadBool());
            Assert.Equal(1, response.ReadInt());
            Assert.False(Received(client, OpCode.StatsUpdate));
            Assert.False(Received(client, OpCode.InventorySync));
        }

        [Fact]
        public void UnreadyPlayer_ReceivesNoWorldTraffic_AndIsInvisibleToOthers()
        {
            var watcherClient = new TestClient(9602);
            AddReadyPlayer(9602, watcherClient, new Vector3(0, 0, 0));
            var loading = new TestClient(9603);
            Select(loading, 9603);

            GameLogic.MapMgr.Update();
            GameLogic.MapMgr.Update();

            Assert.False(SpawnedPlayer(watcherClient, 9603));
            Assert.DoesNotContain(watcherClient.SentPackets, p => p.PacketId == OpCode.EntityVitals && p.ReadInt() == 9603);
            Assert.Single(loading.SentPackets); // still only the select response
        }

        [Fact]
        public void WorldReady_SendsTheCharacterState_AndStartsAreaOfInterest()
        {
            var watcherClient = new TestClient(9604);
            AddReadyPlayer(9604, watcherClient, new Vector3(0, 0, 0));
            var client = new TestClient(9605);
            var player = Select(client, 9605);
            GameLogic.MapMgr.Update();

            Ready(client);

            Assert.False(player.AwaitingWorldReady);
            Assert.True(Received(client, OpCode.StatsUpdate));
            Assert.True(Received(client, OpCode.VitalsUpdate));
            Assert.True(Received(client, OpCode.PlayerProgressionSync));
            Assert.True(Received(client, OpCode.PlayerExpUpdate));
            Assert.True(Received(client, OpCode.InventorySync));
            Assert.True(Received(client, OpCode.EquippedItemsSync));

            GameLogic.MapMgr.Update();

            Assert.True(SpawnedPlayer(client, 9604));        // the new client now sees who is around
            Assert.True(SpawnedPlayer(watcherClient, 9605)); // and they see it
        }

        [Fact]
        public void WorldReady_SentTwice_FlushesTheStateOnlyOnce()
        {
            var client = new TestClient(9606);
            Select(client, 9606);

            Ready(client);
            int afterFirst = client.SentPackets.Count;
            Ready(client);

            Assert.Equal(afterFirst, client.SentPackets.Count);
        }

        [Fact]
        public void WorldReady_FromAClientThatHasNoCharacterInTheWorld_IsIgnored()
        {
            var stranger = new TestClient(9607) { PlayerId = null };

            Ready(stranger);

            Assert.Empty(stranger.SentPackets);
        }

        [Fact]
        public void WorldReady_FromAStaleConnection_DoesNotReadyTheCharacter()
        {
            var owner = new TestClient(9608);
            var player = Select(owner, 9608);
            var stale = new TestClient(9609) { PlayerId = 9608 };

            Ready(stale);

            Assert.True(player.AwaitingWorldReady);
            Assert.Empty(stale.SentPackets);
        }

        [Fact]
        public void UnreadyPlayer_CannotAct()
        {
            var client = new TestClient(9610);
            var player = Select(client, 9610);
            player.LastMoveTime = DateTime.UtcNow.AddSeconds(-1);
            var start = player.Position;

            Send(client, OpCode.PlayerMoveRequest, p => { p.Write(start.X + 1f); p.Write(start.Y); p.Write(start.Z); });

            Assert.Equal(start.X, player.Position.X);

            Ready(client);
            player.LastMoveTime = DateTime.UtcNow.AddSeconds(-1);
            Send(client, OpCode.PlayerMoveRequest, p => { p.Write(start.X + 1f); p.Write(start.Y); p.Write(start.Z); });

            Assert.Equal(start.X + 1f, player.Position.X);
        }

        [Fact]
        public void UnreadyPlayer_IsDisconnectedAndRemoved_WhenTheTimeoutPasses()
        {
            Environment.SetEnvironmentVariable("WORLD_READY_TIMEOUT_SECONDS", "0.05");
            ServerConfig.Initialize();
            var client = new TestClient(9611);
            Select(client, 9611);

            Thread.Sleep(100);
            GameLogic.MapMgr.Update();

            Assert.Null(GameLogic.MapMgr.GetPlayer(9611));
            Assert.False(client.IsConnected);
        }

        [Fact]
        public void ReadyPlayer_IsNeverTimedOut()
        {
            Environment.SetEnvironmentVariable("WORLD_READY_TIMEOUT_SECONDS", "0.05");
            ServerConfig.Initialize();
            var client = new TestClient(9612);
            Select(client, 9612);
            Ready(client);

            Thread.Sleep(100);
            GameLogic.MapMgr.Update();

            Assert.NotNull(GameLogic.MapMgr.GetPlayer(9612));
            Assert.True(client.IsConnected);
        }

        [Fact]
        public void ReattachingALingeringCharacter_WaitsForTheNewClientToBeReady()
        {
            var oldClient = new TestClient(9613);
            var player = Select(oldClient, 9613);
            Ready(oldClient);
            player.MarkInCombat();
            GameLogic.MapMgr.RemovePlayerOwnedBy(oldClient);

            var newClient = new TestClient(9614);
            SelectExisting(newClient, 9613);

            Assert.Same(player, GameLogic.MapMgr.GetPlayer(9613));
            Assert.True(player.AwaitingWorldReady);
            Assert.Single(newClient.SentPackets);

            Ready(newClient);
            Assert.True(Received(newClient, OpCode.StatsUpdate));
        }

        // --- Map catalog ---

        [Fact]
        public void EveryServerMap_HasASceneInTheCatalog()
        {
            var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data", "Maps"), "*.json");
            Assert.NotEmpty(files);

            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                int mapId = int.Parse(name.Split('_')[0]);
                Assert.True(MapCatalog.TryGetSceneName(mapId, out var scene), $"map {name} is missing from MapCatalog");
                Assert.Equal(name, scene);
            }
        }

        // --- #184: logout ---

        [Fact]
        public void Logout_OutOfCombat_RemovesTheCharacterAndFreesTheConnection()
        {
            var client = new TestClient(9620);
            Select(client, 9620);
            Ready(client);

            Send(client, OpCode.LogoutRequest, p => p.Write(false));

            Assert.Equal(LogoutResult.Success, LastLogoutResult(client, out _));
            Assert.Null(GameLogic.MapMgr.GetPlayer(9620));
            Assert.Null(client.PlayerId);
            Assert.True(client.IsConnected);
        }

        [Fact]
        public void Logout_ThenSelectingAgain_EntersTheWorldAsANewSession()
        {
            var client = new TestClient(9621);
            Select(client, 9621);
            Ready(client);
            Send(client, OpCode.LogoutRequest, p => p.Write(false));

            var again = SelectExisting(client, 9621);

            Assert.NotNull(again);
            Assert.True(again.AwaitingWorldReady);
            Assert.Equal(9621, client.PlayerId);
        }

        [Fact]
        public void Logout_WhileStillLoading_IsAllowed()
        {
            var client = new TestClient(9622);
            Select(client, 9622);

            Send(client, OpCode.LogoutRequest, p => p.Write(false));

            Assert.Equal(LogoutResult.Success, LastLogoutResult(client, out _));
            Assert.Null(GameLogic.MapMgr.GetPlayer(9622));
        }

        [Fact]
        public void Logout_InCombat_AsksForConfirmationAndChangesNothing()
        {
            var client = new TestClient(9623);
            var player = Select(client, 9623);
            Ready(client);
            player.MarkInCombat();

            Send(client, OpCode.LogoutRequest, p => p.Write(false));

            Assert.Equal(LogoutResult.ConfirmRequired, LastLogoutResult(client, out float linger));
            Assert.Equal((float)ServerConfig.CombatLogoutLingerSeconds, linger);
            Assert.Same(player, GameLogic.MapMgr.GetPlayer(9623));
            Assert.False(player.IsLingering);
            Assert.Equal(9623, client.PlayerId);
        }

        [Fact]
        public void Logout_InCombat_Confirmed_LeavesTheCharacterLingering()
        {
            var client = new TestClient(9624);
            var player = Select(client, 9624);
            Ready(client);
            player.MarkInCombat();

            Send(client, OpCode.LogoutRequest, p => p.Write(true));

            Assert.Equal(LogoutResult.Success, LastLogoutResult(client, out _));
            Assert.Same(player, GameLogic.MapMgr.GetPlayer(9624));
            Assert.True(player.IsLingering);
            Assert.Null(client.PlayerId);
            Assert.True(client.IsConnected);
        }

        [Fact]
        public void Logout_InCombat_ThenSelectingTheSameCharacter_ResumesTheLingeringOne()
        {
            var client = new TestClient(9625);
            var player = Select(client, 9625);
            Ready(client);
            player.MarkInCombat();
            Send(client, OpCode.LogoutRequest, p => p.Write(true));

            var resumed = SelectExisting(client, 9625);

            Assert.Same(player, resumed);
            Assert.False(player.IsLingering);
            Assert.Same(client, player.Connection);
        }

        [Fact]
        public void Logout_InCombat_WithTheLingerDisabled_NeedsNoConfirmation()
        {
            Environment.SetEnvironmentVariable("COMBAT_LOGOUT_LINGER_SECONDS", "0");
            ServerConfig.Initialize();
            var client = new TestClient(9626);
            var player = Select(client, 9626);
            Ready(client);
            player.MarkInCombat();

            Send(client, OpCode.LogoutRequest, p => p.Write(false));

            Assert.Equal(LogoutResult.Success, LastLogoutResult(client, out _));
            Assert.Null(GameLogic.MapMgr.GetPlayer(9626));
        }

        [Fact]
        public void Logout_OfADeadCharacter_RemovesItWithoutConfirmation()
        {
            var client = new TestClient(9627);
            var player = Select(client, 9627);
            Ready(client);
            player.MarkInCombat();
            player.Health = 0;

            Send(client, OpCode.LogoutRequest, p => p.Write(false));

            Assert.Equal(LogoutResult.Success, LastLogoutResult(client, out _));
            Assert.Null(GameLogic.MapMgr.GetPlayer(9627));
        }

        // --- Respawn after dying in the world (DeathHUD, #171) ---

        [Fact]
        public void DeadCharacter_CanRespawnAtItsBindPoint_AfterEnteringTheWorld()
        {
            var client = new TestClient(9640);
            var player = Select(client, 9640);
            Ready(client);
            player.BindMapId = 1;
            player.BindPosition = new Vector3(7, 8, 0);
            player.Position = new Vector3(30, 30, 0);
            player.Health = 0;
            client.SentPackets.Clear();

            Send(client, OpCode.PlayerRespawnRequest);

            Assert.Equal(player.MaxHealth, player.Health);
            Assert.Equal(new Vector3(7, 8, 0), player.Position);
            var response = client.SentPackets.First(p => p.PacketId == OpCode.PlayerRespawnResponse);
            Assert.True(response.ReadBool());
            Assert.Equal(1, response.ReadInt());
            Assert.Equal(new Vector3(7, 8, 0), response.ReadVector3());
            Assert.True(Received(client, OpCode.VitalsUpdate));
        }

        [Fact]
        public void Respawn_FromAStaleConnection_DoesNotReviveTheCharacter()
        {
            var owner = new TestClient(9641);
            var player = Select(owner, 9641);
            Ready(owner);
            player.Health = 0;
            var stale = new TestClient(9642) { PlayerId = 9641 };

            Send(stale, OpCode.PlayerRespawnRequest);

            Assert.Equal(0, player.Health);
            Assert.Empty(stale.SentPackets);
        }

        [Fact]
        public void Logout_WithoutACharacterInTheWorld_ReportsNotInWorld()
        {
            var client = new TestClient(9628) { PlayerId = null };

            Send(client, OpCode.LogoutRequest, p => p.Write(false));

            Assert.Equal(LogoutResult.NotInWorld, LastLogoutResult(client, out _));
        }

        [Fact]
        public void Logout_FromAStaleConnection_DoesNotTouchTheCharacter()
        {
            var owner = new TestClient(9629);
            var player = Select(owner, 9629);
            Ready(owner);
            var stale = new TestClient(9630) { PlayerId = 9629 };

            Send(stale, OpCode.LogoutRequest, p => p.Write(true));

            Assert.Equal(LogoutResult.NotInWorld, LastLogoutResult(stale, out _));
            Assert.Same(player, GameLogic.MapMgr.GetPlayer(9629));
            Assert.Same(owner, player.Connection);
        }
    }
}
