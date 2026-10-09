using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
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
    public static class GameCommandQueueTestExtensions
    {
        // A single Drain may stop at the time budget; tests that need "everything applied" drain until empty
        public static void DrainAll(this GameCommandQueue queue)
        {
            while (queue.Count > 0) queue.Drain();
        }
    }

    public class TestClient : IClientConnection
    {
        public TestClient(int id) { Id = id; }
        public int Id { get; }
        public int? AccountId { get; set; } = 1;
        public int? PlayerId { get; set; }
        public bool IsConnected { get; private set; } = true;
        public List<Packet> SentPackets { get; } = new();

        public void Send(Packet packet) => SentPackets.Add(new Packet(packet.ToArray()));
        public void Disconnect() => IsConnected = false;
    }

    public class GameCommandQueueTests
    {
        // A framed packet nobody handles: [Length][OpCode 65000]
        private static byte[] UnroutedPacket() => new byte[] { 4, 0, 0xE8, 0xFD };

        [Fact]
        public void Drain_RunsCommandsOnTheDrainingThread_InArrivalOrder()
        {
            var queue = new GameCommandQueue(budgetMs: 1000);
            var order = new List<int>();
            var threadIds = new List<int>();

            // Enqueued from another thread, like a network read loop
            Task.Run(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    int n = i;
                    queue.Post(() => { order.Add(n); threadIds.Add(Environment.CurrentManagedThreadId); });
                }
            }).Wait();

            Assert.Empty(order); // nothing runs until the game thread drains
            queue.Drain();

            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, order);
            Assert.All(threadIds, id => Assert.Equal(Environment.CurrentManagedThreadId, id));
        }

        [Fact]
        public void Enqueue_FromManyThreadsAtOnce_LosesNothing()
        {
            var queue = new GameCommandQueue(maxPendingPerClient: 100_000, budgetMs: 10_000);
            int executed = 0;

            Parallel.For(0, 2000, _ => queue.Post(() => executed++)); // drained single-threaded, so no Interlocked needed
            queue.Drain();

            Assert.Equal(2000, executed);
        }

        [Fact]
        public void EnqueuePacket_RejectsOnlyTheClientThatExceedsItsBacklog_AndRecoversAfterDrain()
        {
            var queue = new GameCommandQueue(maxPendingPerClient: 3, budgetMs: 1000);
            var flooder = new TestClient(7);
            var other = new TestClient(8);

            for (int i = 0; i < 3; i++) Assert.Equal(EnqueueResult.Accepted, queue.EnqueuePacket(flooder, UnroutedPacket()));
            Assert.Equal(EnqueueResult.ClientBacklogFull, queue.EnqueuePacket(flooder, UnroutedPacket()));
            Assert.Equal(EnqueueResult.Accepted, queue.EnqueuePacket(other, UnroutedPacket()));
            Assert.Equal(4, queue.Count); // the rejected packet was not queued

            queue.Drain();

            Assert.Equal(EnqueueResult.Accepted, queue.EnqueuePacket(flooder, UnroutedPacket()));
        }

        [Fact]
        public void Drain_AFailingCommandDoesNotStopTheOthers()
        {
            var queue = new GameCommandQueue(budgetMs: 10_000);
            int ran = 0;

            queue.Post(() => ran++);
            queue.Post(() => throw new InvalidOperationException("boom"));
            queue.Post(() => ran++);

            Assert.Equal(3, queue.Drain());
            Assert.Equal(2, ran);
        }

        [Fact]
        public void Drain_StopsAtTheTimeBudget_AndFinishesOnLaterTicks()
        {
            var queue = new GameCommandQueue(budgetMs: 5);
            int ran = 0;
            for (int i = 0; i < 20; i++) queue.Post(() => { Thread.Sleep(2); ran++; });

            int firstTick = queue.Drain();

            Assert.InRange(firstTick, 1, 19);
            Assert.Equal(1, queue.BudgetExceededCount);

            while (queue.Count > 0) queue.Drain();
            Assert.Equal(20, ran);
        }

        [Fact]
        public void Drain_AlwaysRunsAtLeastOneCommand_EvenWithAnExhaustedBudget()
        {
            var queue = new GameCommandQueue(budgetMs: 0.0001);
            queue.Post(() => Thread.Sleep(2));
            queue.Post(() => { });

            Assert.Equal(1, queue.Drain());
            Assert.Equal(1, queue.Drain());
        }
    }

    public class PacketRoutingTests
    {
        private static byte[] MovePacket(float x)
        {
            using var write = new Packet(OpCode.PlayerMoveRequest);
            write.Write(x);
            write.Write(0f);
            write.Write(0f);
            return write.ToArray();
        }

        private static AppDbContext InMemoryDb(string name)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options;
            var ctx = new AppDbContext(options);
            ctx.Database.EnsureCreated();
            return ctx;
        }

        public PacketRoutingTests()
        {
            PacketHandler.Initialize();
            GameLogic.Commands.DrainAll();
        }

        [Fact]
        public void EveryRegisteredOpCode_HasAnExplicitLane_AndOnlyAccountTrafficIsSession()
        {
            var session = PacketHandler.RegisteredOpCodes.Where(o => PacketHandler.GetLane(o) == PacketLane.Session).OrderBy(o => o).ToArray();

            Assert.Equal(new[]
            {
                OpCode.SignInRequest, OpCode.SignUpRequest, OpCode.CharacterListRequest,
                OpCode.CharacterCreateRequest, OpCode.CharacterDeleteRequest, OpCode.CharacterSelectRequest
            }.OrderBy(o => o).ToArray(), session);
            Assert.All(PacketHandler.RegisteredOpCodes, o => Assert.NotNull(PacketHandler.GetLane(o)));
        }

        [Fact]
        public void DialogueAndQuestPackets_RunOnTheGameThread()
        {
            // They read and change world state (NPCs, inventory, progress), so they must be World lane packets
            foreach (var opCode in new[] { OpCode.EntityInteractRequest, OpCode.DialogueChoose, OpCode.DialogueClose, OpCode.QuestAbandonRequest })
            {
                Assert.Equal(PacketLane.World, PacketHandler.GetLane(opCode));
            }
        }

        [Fact]
        public void WorldPacket_IsOnlyAppliedWhenTheGameLoopDrains()
        {
            var client = new TestClient(9100) { PlayerId = 9100 };
            var player = new Player(9100, "Queued", client) { Position = new Vector3(0, 0, 0), Health = 100 };
            player.LastMoveTime = DateTime.UtcNow.AddSeconds(-1);
            GameLogic.MapMgr.ActiveMaps[1].Players.Clear();
            GameLogic.MapMgr.AddPlayer(player);

            PacketHandler.Receive(client, MovePacket(2f)); // network thread

            Assert.Equal(0f, player.Position.X);
            Assert.Equal(1, GameLogic.Commands.Count);

            GameLogic.Commands.DrainAll(); // game thread

            Assert.Equal(2f, player.Position.X);
        }

        [Fact]
        public void SessionPacket_IsHandledImmediately_WithoutTouchingTheQueue()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => InMemoryDb(dbName);
            var client = new TestClient(9101);

            using var write = new Packet(OpCode.CharacterListRequest);
            PacketHandler.Receive(client, write.ToArray());

            Assert.Equal(0, GameLogic.Commands.Count);
            Assert.Contains(client.SentPackets, p => p.PacketId == OpCode.CharacterListResponse);
        }

        [Fact]
        public void Receive_TruncatedPacket_DisconnectsTheClient()
        {
            var client = new TestClient(9102);

            PacketHandler.Receive(client, new byte[] { 2, 0 });

            Assert.False(client.IsConnected);
        }

        [Fact]
        public void Receive_ClientFloodingTheQueue_IsDisconnected_AndTheBacklogIsBounded()
        {
            var client = new TestClient(9103);
            int limit = GameLogic.Commands.MaxPendingPerClient;

            for (int i = 0; i < limit; i++) PacketHandler.Receive(client, MovePacket(0f));
            Assert.True(client.IsConnected);
            Assert.Equal(limit, GameLogic.Commands.Count);

            PacketHandler.Receive(client, MovePacket(0f));

            Assert.False(client.IsConnected);
            Assert.Equal(limit, GameLogic.Commands.Count);
            GameLogic.Commands.DrainAll();
        }

        [Fact]
        public void Disconnect_QueuedAfterAPacket_RunsAfterIt()
        {
            var client = new TestClient(9104) { PlayerId = 9104 };
            var player = new Player(9104, "Leaving", client) { Position = new Vector3(0, 0, 0), Health = 100 };
            player.LastMoveTime = DateTime.UtcNow.AddSeconds(-1);
            GameLogic.MapMgr.ActiveMaps[1].Players.Clear();
            GameLogic.MapMgr.AddPlayer(player);
            AppDbContext.Factory = () => InMemoryDb(Guid.NewGuid().ToString());

            PacketHandler.Receive(client, MovePacket(2f));
            GameLogic.Commands.Post(() => GameLogic.MapMgr.RemovePlayer(9104, saveState: false));
            GameLogic.Commands.DrainAll();

            Assert.Equal(2f, player.Position.X);                                  // the packet was applied first
            Assert.False(GameLogic.MapMgr.ActiveMaps[1].Players.ContainsKey(9104)); // then the player left
        }

        [Fact]
        public void SelectCharacter_WorldEntryIsDeferredToTheGameThread_AndSkippedIfTheClientDropped()
        {
            string dbName = Guid.NewGuid().ToString();
            AppDbContext.Factory = () => InMemoryDb(dbName);
            using (var db = AppDbContext.Factory())
            {
                db.Characters.Add(new Character { Id = 9105, AccountId = 1, Name = "Entering", Health = 50 });
                db.Characters.Add(new Character { Id = 9106, AccountId = 1, Name = "Dropped", Health = 50 });
                db.SaveChanges();
            }

            var staying = new TestClient(9105);
            using (var write = new Packet(OpCode.CharacterSelectRequest)) { write.Write(9105); PacketHandler.Receive(staying, write.ToArray()); }
            Assert.Null(GameLogic.MapMgr.GetPlayer(9105)); // loaded, but not in the world until the game thread runs

            var dropped = new TestClient(9106);
            using (var write = new Packet(OpCode.CharacterSelectRequest)) { write.Write(9106); PacketHandler.Receive(dropped, write.ToArray()); }
            dropped.Disconnect();

            GameLogic.Commands.DrainAll();

            Assert.NotNull(GameLogic.MapMgr.GetPlayer(9105));
            Assert.Equal(9105, staying.PlayerId);
            Assert.Contains(staying.SentPackets, p => p.PacketId == OpCode.CharacterSelectResponse);
            Assert.Null(GameLogic.MapMgr.GetPlayer(9106)); // no ghost player for a dead connection
            GameLogic.MapMgr.RemovePlayer(9105, saveState: false);
        }

        // Real sockets + the real game loop: the whole path network thread -> queue -> game thread
        [Fact]
        public void EndToEnd_OverTcp_TheGameLoopDrainsQueuedPackets_AndAFloodingClientIsDropped()
        {
            const int port = 17877;
            var server = new GameServer(port);
            var logic = new GameLogic();
            server.Start();
            logic.Start();
            try
            {
                // A well-behaved client: a few world packets spread out, all drained by the running loop
                using (var polite = new TcpClient("127.0.0.1", port))
                {
                    var stream = polite.GetStream();
                    for (int i = 0; i < 20; i++) stream.Write(MovePacket(0f));
                    Assert.True(WaitUntil(() => GameLogic.Commands.Count == 0), "The game loop should have drained the queue.");
                }

                // A flooding client: far more packets than its backlog allows in a single burst
                using (var flooder = new TcpClient("127.0.0.1", port))
                {
                    var stream = flooder.GetStream();
                    var burst = Enumerable.Range(0, 1000).SelectMany(_ => MovePacket(0f)).ToArray();
                    try { stream.Write(burst); } catch (System.IO.IOException) { /* server already closed it */ }

                    flooder.ReceiveTimeout = 3000;
                    Assert.True(IsClosedByServer(flooder), "The server should drop a client that overflows its command backlog.");
                }
            }
            finally
            {
                logic.Stop();
                server.Stop();
                GameLogic.Commands.DrainAll();
            }
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        private static bool IsClosedByServer(TcpClient client)
        {
            try
            {
                var buffer = new byte[16];
                return client.GetStream().Read(buffer, 0, buffer.Length) == 0;
            }
            catch (System.IO.IOException)
            {
                return true; // connection reset
            }
        }
    }
}
