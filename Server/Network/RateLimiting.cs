using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Server.Network
{
    /// <summary>
    /// Locks a key (a remote address) out for a while after too many failures inside a sliding window.
    /// Thread-safe: sign-in runs on every client's read loop.
    /// </summary>
    public sealed class FailureThrottle
    {
        private sealed class Entry
        {
            public readonly Queue<DateTime> Failures = new();
            public DateTime LockedUntil;
        }

        private const int SweepThreshold = 1024;

        private readonly int _maxFailures;
        private readonly TimeSpan _window;
        private readonly TimeSpan _lockout;
        private readonly Func<DateTime> _now;
        private readonly ConcurrentDictionary<string, Entry> _entries = new();

        public FailureThrottle(int maxFailures, TimeSpan window, TimeSpan lockout, Func<DateTime>? now = null)
        {
            _maxFailures = maxFailures;
            _window = window;
            _lockout = lockout;
            _now = now ?? (() => DateTime.UtcNow);
        }

        public bool IsLocked(string key)
        {
            if (!_entries.TryGetValue(key, out var entry)) return false;
            lock (entry) return entry.LockedUntil > _now();
        }

        public void RecordFailure(string key)
        {
            var now = _now();
            var entry = _entries.GetOrAdd(key, _ => new Entry());
            lock (entry)
            {
                while (entry.Failures.Count > 0 && now - entry.Failures.Peek() > _window) entry.Failures.Dequeue();
                entry.Failures.Enqueue(now);
                if (entry.Failures.Count >= _maxFailures)
                {
                    entry.LockedUntil = now + _lockout;
                    entry.Failures.Clear();
                }
            }

            if (_entries.Count > SweepThreshold) Sweep(now);
        }

        public void Reset(string key) => _entries.TryRemove(key, out _);

        public void Clear() => _entries.Clear();

        // Drops entries that can no longer lock anything, so scanning many addresses cannot grow memory forever
        private void Sweep(DateTime now)
        {
            foreach (var pair in _entries)
            {
                lock (pair.Value)
                {
                    bool idle = pair.Value.LockedUntil <= now
                        && (pair.Value.Failures.Count == 0 || now - pair.Value.Failures.Peek() > _window);
                    if (idle) _entries.TryRemove(pair);
                }
            }
        }
    }

    /// <summary>Classic token bucket: bursts up to the capacity, then a sustained rate. Not thread-safe by design.</summary>
    public sealed class TokenBucket
    {
        private readonly double _capacity;
        private readonly double _refillPerSecond;
        private readonly Func<DateTime> _now;
        private double _tokens;
        private DateTime _last;

        public TokenBucket(double capacity, double refillPerSecond, Func<DateTime>? now = null)
        {
            _capacity = capacity;
            _refillPerSecond = refillPerSecond;
            _now = now ?? (() => DateTime.UtcNow);
            _tokens = capacity;
            _last = _now();
        }

        public bool TryTake()
        {
            var now = _now();
            _tokens = Math.Min(_capacity, _tokens + (now - _last).TotalSeconds * _refillPerSecond);
            _last = now;
            if (_tokens < 1) return false;
            _tokens -= 1;
            return true;
        }
    }
}
