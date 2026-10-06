using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Server.Network;

namespace Server.World
{
    public enum EnqueueResult
    {
        Accepted,
        ClientBacklogFull
    }

    /// <summary>
    /// Hand-off between the network threads and the game thread. Network threads only enqueue; the game loop
    /// drains the queue at the start of every tick, so all world state is mutated on a single thread and needs
    /// no locks. A single FIFO keeps each client's packets (and its disconnect) in arrival order.
    /// Safeguards: a per-client backlog cap (flood protection), a time budget per drain (a burst of commands
    /// cannot stretch a tick), and per-command fault isolation (one bad packet cannot stop the loop).
    /// </summary>
    public sealed class GameCommandQueue
    {
        private readonly struct Command
        {
            public readonly IClientConnection? Client;
            public readonly byte[]? Packet;
            public readonly Action? Work;

            public Command(IClientConnection client, byte[] packet) { Client = client; Packet = packet; Work = null; }
            public Command(Action work) { Client = null; Packet = null; Work = work; }
        }

        private readonly ConcurrentQueue<Command> _queue = new();
        private readonly ConcurrentDictionary<int, int> _pendingPerClient = new();
        private readonly int? _maxPendingPerClient;
        private readonly double? _budgetMs;
        private long _lastBudgetWarningTicks;

        // Explicit values are for tests; by default the live ServerConfig values apply.
        public GameCommandQueue(int? maxPendingPerClient = null, double? budgetMs = null)
        {
            _maxPendingPerClient = maxPendingPerClient;
            _budgetMs = budgetMs;
        }

        public int MaxPendingPerClient => _maxPendingPerClient ?? ServerConfig.MaxPendingCommandsPerClient;
        public double BudgetMs => _budgetMs ?? ServerConfig.CommandBudgetMs;

        public int Count => _queue.Count;

        /// <summary>Number of drains that stopped early because the time budget ran out.</summary>
        public long BudgetExceededCount { get; private set; }

        /// <summary>Queues a packet to be handled on the game thread. Safe to call from any thread.</summary>
        public EnqueueResult EnqueuePacket(IClientConnection client, byte[] packet)
        {
            int pending = _pendingPerClient.AddOrUpdate(client.Id, 1, (_, current) => current + 1);
            if (pending > MaxPendingPerClient)
            {
                _pendingPerClient.AddOrUpdate(client.Id, 0, (_, current) => current - 1);
                return EnqueueResult.ClientBacklogFull;
            }

            _queue.Enqueue(new Command(client, packet));
            return EnqueueResult.Accepted;
        }

        /// <summary>Queues internal work (disconnects, world entry) to run on the game thread. Never rejected.</summary>
        public void Post(Action work) => _queue.Enqueue(new Command(work));

        /// <summary>Drops the backlog counter of a client that is gone. Call from the game thread, after its disconnect.</summary>
        public void ForgetClient(int clientId) => _pendingPerClient.TryRemove(clientId, out _);

        /// <summary>
        /// Runs queued commands in order until the queue is empty or the time budget is spent (at least one
        /// command always runs, so progress is guaranteed). Must be called from the game thread only.
        /// </summary>
        public int Drain()
        {
            var timer = Stopwatch.StartNew();
            double budgetMs = BudgetMs;
            int executed = 0;

            while (_queue.TryDequeue(out var command))
            {
                Execute(command);
                executed++;

                if (timer.Elapsed.TotalMilliseconds >= budgetMs && !_queue.IsEmpty)
                {
                    BudgetExceededCount++;
                    WarnBudgetExceeded(executed);
                    break;
                }
            }

            return executed;
        }

        private void Execute(Command command)
        {
            try
            {
                if (command.Work != null)
                {
                    command.Work();
                }
                else
                {
                    PacketHandler.HandlePacket(command.Client!, command.Packet!);
                }
            }
            catch (Exception ex)
            {
                string source = command.Client != null ? $"packet from Client {command.Client.Id}" : "internal command";
                Console.WriteLine($"[Commands] Error while handling {source}: {ex}");
            }
            finally
            {
                if (command.Client != null)
                {
                    _pendingPerClient.AddOrUpdate(command.Client.Id, 0, (_, current) => Math.Max(0, current - 1));
                }
            }
        }

        // Under sustained overload this would fire every tick; log at most once every 5 seconds.
        private void WarnBudgetExceeded(int executed)
        {
            long now = Stopwatch.GetTimestamp();
            if (now - _lastBudgetWarningTicks < 5 * Stopwatch.Frequency) return;
            _lastBudgetWarningTicks = now;
            Console.WriteLine($"[Commands] Drain budget ({BudgetMs:F1}ms) exceeded after {executed} commands; {_queue.Count} still queued for the next tick.");
        }
    }
}
