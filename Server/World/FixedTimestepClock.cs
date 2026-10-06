using System;

namespace Server.World
{
    /// <summary>
    /// Fixed-timestep accumulator: tells the game loop how many whole simulation steps are due for the real time
    /// that has passed. Every step is exactly <see cref="StepSeconds"/> long, so the simulation advances at the
    /// target rate even when ticks (or the OS sleep) run long. After a stall it catches up by at most
    /// <c>maxCatchUpSteps</c> steps and drops the rest, so a slow server cannot spiral into ever longer ticks.
    /// Pure logic (no timers, no sleeping), so it is unit-testable with a fake clock.
    /// </summary>
    public sealed class FixedTimestepClock
    {
        private readonly int _maxCatchUpSteps;
        private double _accumulator;

        public FixedTimestepClock(int ticksPerSecond, int maxCatchUpSteps = 5)
        {
            if (ticksPerSecond < 1) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            if (maxCatchUpSteps < 1) throw new ArgumentOutOfRangeException(nameof(maxCatchUpSteps));

            StepSeconds = 1.0 / ticksPerSecond;
            _maxCatchUpSteps = maxCatchUpSteps;
        }

        public double StepSeconds { get; }

        /// <summary>Real time still needed before the next step is due.</summary>
        public double SecondsUntilNextStep => Math.Max(0.0, StepSeconds - _accumulator);

        /// <summary>
        /// Adds elapsed real time and returns how many steps to run now. <paramref name="droppedSteps"/> is the
        /// number of steps that were due but skipped because the server fell too far behind (0 when keeping up).
        /// </summary>
        public int Advance(double elapsedSeconds, out int droppedSteps)
        {
            _accumulator += Math.Max(0.0, elapsedSeconds);

            int due = (int)(_accumulator / StepSeconds);
            int steps = Math.Min(due, _maxCatchUpSteps);
            droppedSteps = due - steps;

            _accumulator -= due * StepSeconds; // the dropped time is forgotten, not replayed later
            return steps;
        }
    }
}
