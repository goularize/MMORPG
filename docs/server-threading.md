# Server Threading Model

The server is **authoritative and single-writer for world state**: only the game thread (`GameLogic`, 30 TPS)
reads or mutates players, NPCs, resources, loot and maps. Network threads never touch world state directly.

## Flow

```
client socket ──► ClientConnection read loop (thread pool, one per client)
                    │  frames bytes into packets
                    ▼
              PacketHandler.Receive(client, bytes)
                    │
        ┌───────────┴────────────┐
   Session lane              World lane
   (runs right here)         GameCommandQueue.EnqueuePacket ──► ConcurrentQueue
                                                                     │
                              GameLogic.Update():  Commands.Drain() ◄┘   (start of every tick)
                                                   MapMgr.Update()       (simulation)
```

## Lanes (`PacketLane`, declared where each handler is registered in `PacketHandler.Initialize()`)

| Lane | Packets | Runs on | Why |
| :--- | :--- | :--- | :--- |
| `Session` | SignIn, SignUp, CharacterList/Create/Delete/Select | the client's read loop | Database-only work that never touches world state. Keeping it off the game thread means DB latency can never stall a tick. |
| `World` | everything else (movement, combat, chat, inventory, crafting, loot, ...) | the game thread | Reads/changes world state, so it must run where the simulation runs. |

A new handler must be registered with an explicit lane. Default to `World`; use `Session` only for handlers that
touch nothing but the database and the sender's own connection.

`CharacterSelectRequest` is a Session handler that loads the character from the DB and then **posts** the world
entry (`EnterWorld`) to the game thread. Other handlers needing a slow I/O step should follow the same
"load off-thread, apply on the game thread" shape.

## Guarantees and safeguards (`GameCommandQueue`)

- **Ordering:** one FIFO queue, so a client's packets are handled in arrival order, and its disconnect
  (`GameServer.OnClientDisconnect` posts the player removal) runs after everything it already queued.
- **Backlog cap per client** (`MAX_PENDING_COMMANDS_PER_CLIENT`, default 128): a client that overflows it is
  disconnected, so one flooder cannot grow the queue without bound or crowd out others.
- **Time budget per tick** (`COMMAND_BUDGET_MS`, default 10): a burst of commands cannot stretch a tick; the rest
  waits for the next tick. At least one command always runs, so the queue always makes progress.
- **Fault isolation:** an exception in one command is logged and does not stop the others or the loop.
- **Stale work:** queued world entry is skipped if the connection closed while the character was loading
  (`IClientConnection.IsConnected`).

## Sessions

- **One session per account** (`SessionRegistry`): signing in (or up) binds the connection to the account and
  disconnects the account's previous connection. This also frees accounts stuck behind a half-open connection the
  server has not noticed is dead.
- **One character per connection:** `CharacterSelectRequest` claims the character for the connection immediately
  (`PlayerId` is set before the world entry runs on the game thread), so a second select, a sign-in, or a delete of
  that character is refused even while the entry is still queued. If entering the world fails (missing map,
  character already online) the claim is released and the client gets a failure response.
- **Disconnects remove only their own player** (`MapManager.RemovePlayerOwnedBy`), so a late disconnect of an old
  connection can never remove the player of a newer session of the same character.
- `ClientConnection.AccountId`/`PlayerId` are single atomic ints (0 = none): they are written by one thread and read
  by others, and a `Nullable<int>` can tear.

## The game loop (`GameLogic`)

- **Fixed timestep:** `FixedTimestepClock` turns real elapsed time into whole 1/30 s steps, so every `Update()` is
  exactly one fixed step (NPC movement and timers rely on that) and the average rate does not drift.
- **Catch-up with a cap:** after a stall it runs at most 5 steps back to back and drops the rest, so an overloaded
  server degrades (slower world) instead of spiralling into ever longer ticks.
- **Exception guard:** an exception in a tick is logged and the loop keeps going; one failing map does not stop the
  others (`MapManager.Update`). Repeated errors are rate-limited by `LogThrottle` (once per 5 s per kind).
- **Lag reporting:** ticks over the 33 ms budget and dropped steps are counted (`SlowTickCount`,
  `DroppedStepCount`) and logged.

## Rules for contributors

1. Never mutate world state from a network thread or a `Task.Run` continuation; post/queue it instead.
2. Do not block the game thread on I/O. All world-handler persistence goes through the write-behind queue
   (see `docs/persistence.md`).
3. Tests call handlers directly. When a flow goes through the queue (e.g. character select), call
   `GameLogic.Commands.DrainAll()` (test helper) to run what was posted.

## Process lifecycle

`Program.Main` has no stdin dependency: it waits on the `ShutdownCoordinator` token (SIGTERM, SIGINT/Ctrl+C, SIGHUP,
or `q` on an interactive console). Shutdown order is: stop the game loop, stop the listener and disconnect clients,
save every online player, then flush the persistence queue (see `docs/persistence.md`). The listen port comes from
`SERVER_PORT`.
