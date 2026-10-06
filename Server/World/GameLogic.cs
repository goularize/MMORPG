using System;
using System.Diagnostics;
using System.Threading;

namespace Server.World
{
    public class GameLogic
    {
        private const int TICKS_PER_SECOND = Shared.Constants.GameRules.ServerTickRate;

        // Longest tick we consider healthy: the whole step budget (approx 33.33ms at 30 TPS)
        private const double MS_PER_TICK = 1000.0 / TICKS_PER_SECOND;

        private readonly Action _update;
        private volatile bool _isRunning = false;
        private Thread? _logicThread;
        private long _tickErrorCount;
        private long _slowTickCount;
        private long _droppedStepCount;

        /// <summary>Ticks whose update threw (the loop survives them).</summary>
        public long TickErrorCount => Volatile.Read(ref _tickErrorCount);

        /// <summary>Ticks that took longer than the step budget.</summary>
        public long SlowTickCount => Volatile.Read(ref _slowTickCount);

        /// <summary>Simulation steps skipped because the server fell too far behind to catch up.</summary>
        public long DroppedStepCount => Volatile.Read(ref _droppedStepCount);

        // The manager that holds all active Map Instances
        public static MapManager MapMgr { get; } = new MapManager();

        // Hand-off from the network threads: everything that changes world state runs from here, on this thread
        public static GameCommandQueue Commands { get; } = new GameCommandQueue();

        // The update delegate is injectable so the loop's resilience can be tested without a real world
        public GameLogic(Action? update = null)
        {
            _update = update ?? Update;
        }

        public void Start()
        {
            _isRunning = true;
            _logicThread = new Thread(Loop)
            {
                Name = "GameLogicThread",
                IsBackground = true
            };
            _logicThread.Start();

            Console.WriteLine($"GameLogic started at {TICKS_PER_SECOND} TPS (Target: {MS_PER_TICK:F2}ms per tick).");
        }

        public void Stop()
        {
            _isRunning = false;
            // Wait for the thread to finish its current loop before killing it completely
            _logicThread?.Join(1000);
            Console.WriteLine("GameLogic stopped.");
        }

        private void Loop()
        {
            var clock = new FixedTimestepClock(TICKS_PER_SECOND);
            var wallClock = Stopwatch.StartNew();
            double previous = 0.0;

            while (_isRunning)
            {
                double now = wallClock.Elapsed.TotalSeconds;
                int steps = clock.Advance(now - previous, out int dropped);
                previous = now;

                if (dropped > 0)
                {
                    Interlocked.Add(ref _droppedStepCount, dropped);
                    LogThrottle.Warn("gameloop.dropped", $"[GameLoop] Server is overloaded: dropped {dropped} simulation step(s) it could not catch up on ({DroppedStepCount} so far).");
                }

                for (int i = 0; i < steps; i++)
                {
                    RunTick();
                }

                // Sleep until the next step is due (rounded up so we never wake early and spin)
                int sleepMs = (int)Math.Ceiling(clock.SecondsUntilNextStep * 1000.0);
                if (sleepMs > 0) Thread.Sleep(sleepMs);
            }
        }

        /// <summary>
        /// Runs one simulation step. An exception in the update is logged and swallowed: a bug in one entity must
        /// not kill the process (this runs on a background thread, where an unhandled exception terminates it).
        /// </summary>
        private void RunTick()
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                _update();
            }
            catch (Exception ex)
            {
                long errors = Interlocked.Increment(ref _tickErrorCount);
                LogThrottle.Warn("gameloop.error", $"[GameLoop] Unhandled exception in game tick (error #{errors}): {ex}");
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs > MS_PER_TICK)
            {
                long slow = Interlocked.Increment(ref _slowTickCount);
                LogThrottle.Warn("gameloop.slow", $"[GameLoop] Slow tick: {elapsedMs:F1}ms (budget {MS_PER_TICK:F1}ms, {slow} slow tick(s) so far).");
            }
        }

        /// <summary>
        /// One fixed simulation step (always 1/<see cref="TICKS_PER_SECOND"/> s, see <see cref="FixedTimestepClock"/>).
        /// </summary>
        private void Update()
        {
            // 1. Apply everything the network threads queued since the last tick
            Commands.Drain();

            // 2. Simulate all active Maps in the world
            MapMgr.Update();
        }
    }
}
