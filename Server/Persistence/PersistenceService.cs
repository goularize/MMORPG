using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using Server.Database;
using Server.Database.Models;
using Server.World;

namespace Server.Persistence
{
    /// <summary>
    /// Write-behind persistence. The game thread never touches the database: it queues immutable snapshots and
    /// returns. Pending changes are coalesced per character (a newer snapshot replaces an unflushed older one),
    /// flushed in ONE SaveChanges per character, and always by the same worker (characterId % workers), so a
    /// character's writes are strictly ordered and no two writers ever touch the same rows.
    /// Failures are retried with backoff and the data stays queued; shutdown and character select can wait
    /// for pending writes (<see cref="Flush"/>, <see cref="Stop"/>).
    /// Chat logs use a separate bounded lane that drops the oldest entries instead of ever blocking.
    /// </summary>
    public sealed class PersistenceService
    {
        /// <summary>The process-wide instance used by the game.</summary>
        public static PersistenceService Instance { get; } = new PersistenceService();

        /// <summary>
        /// Test seam: when true every write is applied synchronously on the calling thread, which keeps tests that
        /// assert database state right after a handler deterministic and isolated from each other.
        /// </summary>
        public bool Inline { get; set; }

        private enum ItemOpKind { Upsert, Delete }

        private sealed class ItemOp
        {
            public ItemOpKind Kind;
            public CharacterItem Item = null!;
        }

        private sealed class WriteState
        {
            public readonly int CharacterId;
            public readonly object Gate = new();

            public CharacterState? Character;
            public Dictionary<Guid, ItemOp> Items = new();
            public HashSet<int> Recipes = new();

            public long Version;          // bumped by every queued change
            public long FlushedVersion;   // highest version that is safely in the database
            public DateTime DueUtc;       // not flushed before this time (write-behind delay)
            public DateTime RetryAtUtc;   // backoff after a failed flush
            public int Attempts;
            public bool Removed;          // retired by the worker once clean; enqueuers must fetch a fresh state

            public WriteState(int characterId) { CharacterId = characterId; }
            public bool IsDirty => Version != FlushedVersion;
        }

        private sealed class Worker
        {
            public readonly ConcurrentDictionary<int, WriteState> States = new();
            public readonly SemaphoreSlim Signal = new(0);
            public Thread? Thread;
        }

        private readonly Func<AppDbContext>? _dbFactory;
        private readonly double? _flushIntervalSeconds;
        private readonly int? _workerCount;
        private readonly int? _maxAttempts;
        private readonly object _startGate = new();

        private Worker[] _workers = Array.Empty<Worker>();
        private Channel<ChatLog>? _chat;
        private Thread? _chatThread;
        private volatile bool _started;
        private volatile bool _stopRequested;
        private volatile bool _stopped;

        private long _flushedBatches;
        private long _failedBatches;
        private long _droppedBatches;
        private long _droppedChatLogs;

        // Explicit values are for tests; by default the live ServerConfig values apply.
        public PersistenceService(Func<AppDbContext>? dbFactory = null, double? flushIntervalSeconds = null, int? workers = null, int? maxAttempts = null)
        {
            _dbFactory = dbFactory;
            _flushIntervalSeconds = flushIntervalSeconds;
            _workerCount = workers;
            _maxAttempts = maxAttempts;
        }

        private double FlushIntervalSeconds => _flushIntervalSeconds ?? ServerConfig.WriteBehindFlushSeconds;
        private int MaxAttempts => _maxAttempts ?? ServerConfig.PersistenceMaxAttempts;

        public long FlushedBatchCount => Volatile.Read(ref _flushedBatches);
        public long FailedFlushCount => Volatile.Read(ref _failedBatches);
        public long DroppedBatchCount => Volatile.Read(ref _droppedBatches);
        public long DroppedChatLogCount => Volatile.Read(ref _droppedChatLogs);

        /// <summary>Characters that still have unflushed changes.</summary>
        public int PendingCharacterCount => _workers.Sum(w => w.States.Count);

        // ---- Queueing (called from the game thread; never blocks on the database) ----

        /// <param name="urgent">Durable event: flush as soon as possible instead of waiting for the write-behind delay.</param>
        public void QueueCharacter(CharacterState state, bool urgent = false)
            => Mutate(state.CharacterId, s => s.Character = state, urgent);

        /// <summary>Queues an insert/update of the item. A snapshot is taken now, later changes to the live item need a new call.</summary>
        public void QueueItem(CharacterItem item, bool urgent = false)
        {
            var snapshot = CloneItem(item);
            Mutate(item.CharacterId, s => s.Items[snapshot.Id] = new ItemOp { Kind = ItemOpKind.Upsert, Item = snapshot }, urgent);
        }

        public void QueueItemDelete(CharacterItem item, bool urgent = false)
        {
            var snapshot = CloneItem(item);
            Mutate(item.CharacterId, s => s.Items[snapshot.Id] = new ItemOp { Kind = ItemOpKind.Delete, Item = snapshot }, urgent);
        }

        public void QueueRecipe(int characterId, int recipeId, bool urgent = false)
            => Mutate(characterId, s => s.Recipes.Add(recipeId), urgent);

        /// <summary>Marks the character's pending changes as durable (flush now). No effect when nothing is pending.</summary>
        public void Expedite(int characterId)
        {
            if (Inline || !_started) return;

            var worker = GetWorker(characterId);
            if (!worker.States.TryGetValue(characterId, out var state)) return;

            lock (state.Gate)
            {
                if (state.Removed || !state.IsDirty) return;
                state.DueUtc = DateTime.MinValue;
            }
            Wake(worker);
        }

        /// <summary>Queues a chat log line on the separate lane. Under pressure the oldest lines are dropped.</summary>
        public void QueueChatLog(ChatLog log)
        {
            if (Inline || _stopped)
            {
                WriteChatBatch(new List<ChatLog> { log });
                return;
            }

            EnsureStarted();
            _chat!.Writer.TryWrite(log);
        }

        // ---- Waiting ----

        /// <summary>
        /// Blocks until everything queued for this character so far is in the database (or the timeout passes).
        /// Used before loading a character so a quick re-login never reads state older than its last save.
        /// </summary>
        public bool Flush(int characterId, TimeSpan timeout)
        {
            if (Inline || !_started) return true;

            var worker = GetWorker(characterId);
            if (!worker.States.TryGetValue(characterId, out var state)) return true;

            long target;
            lock (state.Gate)
            {
                if (state.Removed || !state.IsDirty) return true;
                target = state.Version;
                state.DueUtc = DateTime.MinValue;
                state.RetryAtUtc = DateTime.MinValue;
            }
            Wake(worker);

            return WaitUntil(() => { lock (state.Gate) return state.Removed || state.FlushedVersion >= target; }, timeout);
        }

        /// <summary>Flushes every pending character right now (ignoring write-behind delay and retry backoff).</summary>
        public bool FlushAll(TimeSpan timeout)
        {
            if (Inline || !_started) return true;

            foreach (var worker in _workers)
            {
                foreach (var state in worker.States.Values)
                {
                    lock (state.Gate)
                    {
                        state.DueUtc = DateTime.MinValue;
                        state.RetryAtUtc = DateTime.MinValue;
                    }
                }
                Wake(worker);
            }

            return WaitUntil(() => PendingCharacterCount == 0, timeout);
        }

        /// <summary>
        /// Graceful shutdown: flushes everything, drains the chat lane and stops the workers. Writes queued after this
        /// point are applied synchronously, so nothing is lost. Returns false if some data could not be flushed in time.
        /// </summary>
        public bool Stop(TimeSpan timeout)
        {
            if (_stopped) return true;

            bool flushed = FlushAll(timeout);

            _chat?.Writer.TryComplete();
            if (_chatThread != null && !_chatThread.Join(timeout)) flushed = false;

            _stopRequested = true;
            foreach (var worker in _workers)
            {
                Wake(worker);
                worker.Thread?.Join(TimeSpan.FromSeconds(2));
            }

            _stopped = true;
            return flushed;
        }

        // ---- Internals ----

        private void Mutate(int characterId, Action<WriteState> change, bool urgent)
        {
            if (Inline || _stopped)
            {
                // Untracked one-shot state: written immediately on this thread
                var oneShot = new WriteState(characterId);
                change(oneShot);
                oneShot.Version = 1;
                FlushState(oneShot, null);
                return;
            }

            var worker = GetWorker(characterId);
            while (true)
            {
                var state = worker.States.GetOrAdd(characterId, id => new WriteState(id));
                lock (state.Gate)
                {
                    if (state.Removed) continue; // the worker just retired this state; fetch a fresh one

                    bool wasClean = !state.IsDirty;
                    change(state);
                    state.Version++;

                    // The write-behind delay counts from the first unflushed change, so latency stays bounded
                    if (wasClean) state.DueUtc = DateTime.UtcNow.AddSeconds(FlushIntervalSeconds);
                    if (urgent) state.DueUtc = DateTime.MinValue;
                }

                Wake(worker);
                return;
            }
        }

        private Worker GetWorker(int characterId)
        {
            EnsureStarted();
            return _workers[(characterId & int.MaxValue) % _workers.Length];
        }

        private void EnsureStarted()
        {
            if (_started) return;

            lock (_startGate)
            {
                if (_started) return;

                int count = Math.Max(1, _workerCount ?? ServerConfig.PersistenceWorkers);
                _workers = Enumerable.Range(0, count).Select(_ => new Worker()).ToArray();
                for (int i = 0; i < _workers.Length; i++)
                {
                    var worker = _workers[i];
                    worker.Thread = new Thread(() => WorkerLoop(worker)) { Name = $"Persistence-{i}", IsBackground = true };
                    worker.Thread.Start();
                }

                _chat = Channel.CreateBounded<ChatLog>(
                    new BoundedChannelOptions(Math.Max(100, ServerConfig.ChatLogQueueSize))
                    {
                        FullMode = BoundedChannelFullMode.DropOldest,
                        SingleReader = true
                    },
                    _ => Interlocked.Increment(ref _droppedChatLogs));
                _chatThread = new Thread(ChatLoop) { Name = "Persistence-Chat", IsBackground = true };
                _chatThread.Start();

                _started = true;
            }
        }

        private static void Wake(Worker worker)
        {
            if (worker.Signal.CurrentCount == 0) worker.Signal.Release();
        }

        private void WorkerLoop(Worker worker)
        {
            while (!_stopRequested)
            {
                worker.Signal.Wait(100);

                var now = DateTime.UtcNow;
                foreach (var state in worker.States.Values)
                {
                    bool due;
                    lock (state.Gate)
                    {
                        due = !state.Removed && state.IsDirty && state.DueUtc <= now && state.RetryAtUtc <= now;
                    }

                    if (due) FlushState(state, worker);
                }
            }
        }

        /// <summary>Writes one character's pending changes in a single SaveChanges. Called by the owning worker (or inline).</summary>
        private void FlushState(WriteState state, Worker? owner)
        {
            CharacterState? character;
            Dictionary<Guid, ItemOp> items;
            HashSet<int> recipes;
            long taken;

            lock (state.Gate)
            {
                character = state.Character;
                items = state.Items;
                recipes = state.Recipes;
                state.Character = null;
                state.Items = new Dictionary<Guid, ItemOp>();
                state.Recipes = new HashSet<int>();
                taken = state.Version;
            }

            bool ok = TryWrite(state.CharacterId, character, items, recipes, out string? error);

            lock (state.Gate)
            {
                if (ok)
                {
                    state.FlushedVersion = taken;
                    state.Attempts = 0;
                    state.RetryAtUtc = DateTime.MinValue;
                    Interlocked.Increment(ref _flushedBatches);
                }
                else
                {
                    Interlocked.Increment(ref _failedBatches);
                    state.Attempts++;

                    if (state.Attempts >= MaxAttempts)
                    {
                        // Give up on this batch (e.g. the character row no longer exists) instead of retrying forever
                        Interlocked.Increment(ref _droppedBatches);
                        Console.WriteLine($"[Persistence] Giving up on a batch for character {state.CharacterId} after {state.Attempts} attempts: {error}");
                        state.FlushedVersion = taken;
                        state.Attempts = 0;
                        state.RetryAtUtc = DateTime.MinValue;
                    }
                    else
                    {
                        // Put the failed batch back underneath anything queued while it was being written
                        state.Character ??= character;
                        foreach (var pair in items) state.Items.TryAdd(pair.Key, pair.Value);
                        state.Recipes.UnionWith(recipes);

                        double backoff = Math.Min(30.0, 0.5 * Math.Pow(2, state.Attempts - 1));
                        state.RetryAtUtc = DateTime.UtcNow.AddSeconds(backoff);
                        LogThrottle.Warn($"persistence.fail.{state.CharacterId}", $"[Persistence] Flush for character {state.CharacterId} failed (attempt {state.Attempts}), retrying in {backoff:F1}s: {error}");
                    }
                }

                if (!state.IsDirty)
                {
                    state.Removed = true;
                    owner?.States.TryRemove(new KeyValuePair<int, WriteState>(state.CharacterId, state));
                }
                else if (ok)
                {
                    // Changes arrived while writing: they get their own write-behind window
                    state.DueUtc = DateTime.UtcNow.AddSeconds(FlushIntervalSeconds);
                }
            }
        }

        private bool TryWrite(int characterId, CharacterState? character, Dictionary<Guid, ItemOp> items, HashSet<int> recipes, out string? error)
        {
            error = null;
            if (character == null && items.Count == 0 && recipes.Count == 0) return true;

            try
            {
                using var db = (_dbFactory ?? AppDbContext.Factory)();

                if (character != null)
                {
                    var row = db.Characters.Find(characterId);
                    if (row != null) Apply(row, character);
                }

                foreach (var op in items.Values)
                {
                    var existing = db.CharacterItems.Find(op.Item.Id);
                    if (op.Kind == ItemOpKind.Delete)
                    {
                        if (existing != null) db.CharacterItems.Remove(existing);
                    }
                    else if (existing != null)
                    {
                        db.Entry(existing).CurrentValues.SetValues(op.Item);
                    }
                    else
                    {
                        db.CharacterItems.Add(CloneItem(op.Item));
                    }
                }

                foreach (int recipeId in recipes)
                {
                    if (!db.CharacterLearnedRecipes.Any(r => r.CharacterId == characterId && r.RecipeId == recipeId))
                    {
                        db.CharacterLearnedRecipes.Add(new CharacterLearnedRecipe { CharacterId = characterId, RecipeId = recipeId, LearnedAt = DateTime.UtcNow });
                    }
                }

                db.SaveChanges(); // one call = one atomic commit for the whole batch
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void Apply(Character row, CharacterState s)
        {
            row.Level = s.Level;
            row.Exp = s.Exp;
            row.StatPoints = s.StatPoints;
            row.Health = s.Health;
            row.Mana = s.Mana;
            row.Strength = s.Strength;
            row.Intelligence = s.Intelligence;
            row.Constitution = s.Constitution;
            row.Knowledge = s.Knowledge;
            row.Gold = s.Gold;
            row.InventorySlots = s.InventorySlots;
            row.MapId = s.MapId;
            row.X = s.X;
            row.Y = s.Y;
            row.Z = s.Z;
            row.BindMapId = s.BindMapId;
            row.BindX = s.BindX;
            row.BindY = s.BindY;
            row.BindZ = s.BindZ;
        }

        private static CharacterItem CloneItem(CharacterItem item) => item.MemberwiseCloneForPersistence();

        // ---- Chat lane ----

        private void ChatLoop()
        {
            var reader = _chat!.Reader;
            var batch = new List<ChatLog>(200);

            while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                batch.Clear();
                while (batch.Count < 200 && reader.TryRead(out var log)) batch.Add(log);
                if (batch.Count > 0) WriteChatBatch(batch);
            }
        }

        private void WriteChatBatch(List<ChatLog> batch)
        {
            try
            {
                using var db = (_dbFactory ?? AppDbContext.Factory)();
                db.ChatLogs.AddRange(batch);
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                // Chat logs are not worth blocking or retrying forever for
                Interlocked.Add(ref _droppedChatLogs, batch.Count);
                LogThrottle.Warn("persistence.chat", $"[Persistence] Failed to write {batch.Count} chat log(s): {ex.Message}");
            }
        }

        private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                Thread.Sleep(2);
            }
            return condition();
        }
    }
}
