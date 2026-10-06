using System;
using System.Threading;
using Server.World;
using Server.World.Entities;
using Shared.Math;
using Xunit;

namespace Server.Tests
{
    public class FixedTimestepClockTests
    {
        private const double Step = 1.0 / 30.0;

        [Fact]
        public void Advance_ExactlyOneStep_RunsOneStep()
        {
            var clock = new FixedTimestepClock(30);

            Assert.Equal(1, clock.Advance(Step + 1e-9, out int dropped));
            Assert.Equal(0, dropped);
        }

        [Fact]
        public void Advance_PartialTime_AccumulatesUntilAStepIsDue()
        {
            var clock = new FixedTimestepClock(30);

            Assert.Equal(0, clock.Advance(0.02, out _));
            Assert.Equal(1, clock.Advance(0.02, out _)); // 0.04s >= one 0.0333s step, 0.0067s carried over
            Assert.InRange(clock.SecondsUntilNextStep, 0.026, 0.027);
        }

        [Fact]
        public void Advance_AfterAStall_CatchesUpAtMostMaxStepsAndDropsTheRest()
        {
            var clock = new FixedTimestepClock(30, maxCatchUpSteps: 5);

            int steps = clock.Advance(1.0, out int dropped); // a one second stall = 30 due steps

            Assert.Equal(5, steps);
            Assert.Equal(25, dropped);
            Assert.Equal(0, clock.Advance(0.0, out int droppedAgain)); // the dropped time is forgotten, not replayed
            Assert.Equal(0, droppedAgain);
        }

        [Fact]
        public void Advance_NegativeElapsedTime_IsIgnored()
        {
            var clock = new FixedTimestepClock(30);

            Assert.Equal(0, clock.Advance(-5.0, out _));
        }

        [Fact]
        public void Advance_JitteryRealTime_DoesNotDriftFromTheTargetRate()
        {
            var clock = new FixedTimestepClock(30, maxCatchUpSteps: 100);
            var rng = new Random(1);
            int total = 0;
            double simulated = 0;

            while (simulated < 10.0)
            {
                double slice = 0.001 + rng.NextDouble() * 0.045; // wake-ups anywhere between 1ms and 46ms apart
                simulated += slice;
                total += clock.Advance(slice, out _);
            }

            Assert.InRange(total, (int)(simulated * 30) - 1, (int)(simulated * 30) + 1);
        }

        [Fact]
        public void Constructor_RejectsANonPositiveTickRate()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimestepClock(0));
        }

        [Fact]
        public void Constructor_RejectsANonPositiveCatchUpLimit()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimestepClock(30, maxCatchUpSteps: 0));
        }
    }

    public class GameLoopResilienceTests
    {
        private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(5);
            }
            return condition();
        }

        [Fact]
        public void Loop_SurvivesAnExceptionInTheTick_AndKeepsTicking()
        {
            int ticks = 0;
            var logic = new GameLogic(() =>
            {
                int n = Interlocked.Increment(ref ticks);
                if (n == 2) throw new InvalidOperationException("simulated gameplay bug");
            });

            logic.Start();
            try
            {
                Assert.True(WaitUntil(() => Volatile.Read(ref ticks) >= 6), "The loop should keep ticking after the failed tick.");
                Assert.Equal(1, logic.TickErrorCount);
            }
            finally { logic.Stop(); }
        }

        [Fact]
        public void Loop_RunsAtTheTargetTickRate()
        {
            int ticks = 0;
            var logic = new GameLogic(() => Interlocked.Increment(ref ticks));

            logic.Start();
            Thread.Sleep(1000);
            logic.Stop();

            // 30 TPS target; allow generous slack for a loaded CI machine
            Assert.InRange(Volatile.Read(ref ticks), 22, 38);
        }

        [Fact]
        public void Loop_ReportsSlowTicks()
        {
            int ticks = 0;
            var logic = new GameLogic(() => { if (Interlocked.Increment(ref ticks) == 1) Thread.Sleep(80); });

            logic.Start();
            try
            {
                Assert.True(WaitUntil(() => logic.SlowTickCount >= 1));
            }
            finally { logic.Stop(); }
        }

        [Fact]
        public void Loop_WhenOverloaded_DropsStepsInsteadOfSpiralling()
        {
            // Every tick takes ~10 steps worth of time, so the loop can never catch up
            var logic = new GameLogic(() => Thread.Sleep(330));

            logic.Start();
            try
            {
                Assert.True(WaitUntil(() => logic.DroppedStepCount > 0), "Steps beyond the catch-up cap should be dropped.");
            }
            finally { logic.Stop(); }
        }
    }

    public class ThrowingNpc : NPC
    {
        public ThrowingNpc(int id) : base(id, "Broken") { }
        public override void Update() => throw new InvalidOperationException("broken npc");
    }

    public class CountingNpc : NPC
    {
        public int Updates;
        public CountingNpc(int id) : base(id, "Healthy") { }
        public override void Update() => Updates++;
    }

    public class MapIsolationTests
    {
        [Fact]
        public void MapManagerUpdate_AFailingMapDoesNotStopOtherMapsFromBeingSimulated()
        {
            var manager = new MapManager();
            var broken = new MapInstance(91);
            var healthy = new MapInstance(92);
            manager.ActiveMaps[91] = broken;
            manager.ActiveMaps[92] = healthy;
            broken.AddNPC(new ThrowingNpc(EntityIdAllocator.Next(Shared.Constants.EntityIdKind.Npc)) { Position = new Vector3(0, 0, 0) });
            var counter = new CountingNpc(EntityIdAllocator.Next(Shared.Constants.EntityIdKind.Npc)) { Position = new Vector3(0, 0, 0) };
            healthy.AddNPC(counter);

            manager.Update(); // must not throw, whatever order the maps are visited in

            Assert.Equal(1, counter.Updates);
        }
    }

    public class LogThrottleTests
    {
        [Fact]
        public void ShouldLog_AllowsTheFirstLine_ThenSuppressesRepeatsWithinTheInterval()
        {
            string key = "test." + Guid.NewGuid();

            Assert.True(LogThrottle.ShouldLog(key, 60));
            Assert.False(LogThrottle.ShouldLog(key, 60));
            Assert.True(LogThrottle.ShouldLog("other." + key, 60)); // keys are independent
        }

        [Fact]
        public void ShouldLog_AllowsAgainOnceTheIntervalHasPassed()
        {
            string key = "test." + Guid.NewGuid();

            Assert.True(LogThrottle.ShouldLog(key, 0.05));
            Thread.Sleep(80);
            Assert.True(LogThrottle.ShouldLog(key, 0.05));
        }
    }
}
