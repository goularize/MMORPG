# Persistence (write-behind)

The game thread never touches the database. Changes are queued as **immutable snapshots** and written in the
background by `PersistenceService` (`Server/Persistence`).

```
game thread: player.QueueSave() / InventoryHandler.SaveDbItem() / QueueRecipe() / QueueChatLog()
                 │  (snapshot taken here; returns immediately)
                 ▼
     per-character WriteState  ── coalesced: a newer snapshot replaces an unflushed older one
                 │  owned by worker  (characterId % PERSISTENCE_WORKERS)
                 ▼
     worker thread: ONE SaveChanges per character  (character row + items + recipes, atomic)
```

## Guarantees

- **Ordering / no write races:** a character's writes always go through the same worker, so they are strictly
  ordered and no two writers touch the same rows (last queued state wins).
- **Snapshots:** workers never read live entities (`CharacterState`, cloned `CharacterItem`s), so the game thread can keep
  changing them without data races.
- **Atomic batches:** gold, the crafted item and the learned recipe of one change reach the database together or not at all.
- **Coalescing:** the write-behind delay counts from the first unflushed change, so many changes in that window become one write.
- **Retries:** a failed flush keeps the data queued and retries with backoff (0.5 s, 1 s, 2 s ... up to 30 s). After
  `PERSISTENCE_MAX_ATTEMPTS` the batch is dropped and logged (e.g. the character row no longer exists).
- **Read-your-writes:** character select waits (up to 3 s) for that character's pending writes before loading it,
  so a quick relog never reads state older than its last save.
- **Shutdown:** `Program` queues a save of every online player and calls `PersistenceService.Stop(10 s)`, which flushes
  everything and drains the chat lane. Writes queued after `Stop` are applied synchronously, so nothing is lost.
- **Chat logs** use a separate bounded lane (`CHAT_LOG_QUEUE_SIZE`) that drops the oldest lines under pressure and
  never blocks the game.

## What is durable (flushed immediately instead of waiting for the delay)

Events that create, destroy or transfer value, plus lifecycle: crafting, looting (satchels only live in memory),
dropping/destroying items, refining, learning a recipe, level-up, bind, respawn, disconnect and shutdown.
Everything else (inventory moves, equipping, stat points, vitals) uses the normal ~2 s write-behind, and every online
player is also autosaved every ~60 s (staggered), which is also what persists health, mana and map during play.
A crash can therefore lose at most the last write-behind window of non-durable changes.

## Configuration (`Server/.env`)

| Key | Default | Meaning |
| :--- | :---: | :--- |
| `WRITE_BEHIND_FLUSH_SECONDS` | 2 | Delay before a non-durable change is written |
| `AUTOSAVE_SECONDS` | 60 | Period of the per-player safety-net save |
| `PERSISTENCE_WORKERS` | 4 | Writer threads (characters are spread across them) |
| `PERSISTENCE_MAX_ATTEMPTS` | 8 | Flush attempts before a batch is dropped |
| `CHAT_LOG_QUEUE_SIZE` | 10000 | Capacity of the chat log lane |

## Rules for contributors

1. Never call `AppDbContext` from a world handler. Queue through `Player.QueueSave()`,
   `InventoryHandler.SaveDbItem/DeleteDbItem` or `PersistenceService.Instance`.
2. After changing an item, call `SaveDbItem(item)` again: the queue holds a snapshot, not the live object.
3. If a handler creates/destroys value, mark it durable: `QueueSave(urgent: true)` or `Expedite(player.Id)`.
4. Account/lobby handlers (sign-up, character create/delete) stay synchronous: they are transactional, run on the
   connection's read loop (not the game thread) and need their result immediately.
5. Tests run the shared instance inline (`PersistenceService.Instance.Inline`, set in `TestAssemblyInit`), so DB
   assertions right after a handler are deterministic; use your own service instance to test asynchronous behaviour.
