# NPC Dialogue, Progression and Quests

Status: **server and shared code implemented** (#233 to #238). The Unity side (dialogue window, quest log, quest
markers, authoring tools) is still open, see the [Issue map](#issue-map).

Talking to an NPC, having it react to what the player has done, and handing out quests are one problem: *the server
decides what an NPC says and offers, based on persistent per-character progress.* This document describes the pieces
and how they fit the server (authoritative, single game thread, write-behind persistence, fail-fast static data).

## Goals

- NPC text and choices are **data** (JSON), validated at startup like every other static table.
- What an NPC says depends on the **player's progress** (quests, flags, kills, level, items).
- The server stays **authoritative**: the client only asks to talk to an NPC and to pick one of the options the
  server just offered.
- One **condition system** shared by dialogue and quests (and, later, loot, spawns and vendors).
- Progress is **persistent** through the existing write-behind queue.

Non-goals (for now): voice acting, cutscenes, branching narrative tooling, timed world events, party-shared quests,
repeatable quests.

## Overview

```
                         Dialogues.json   Quests.json      (static data, DataValidator, fail-fast)
                               │               │
 client ── Interact ──►  InteractHandler ──► DialogueService ──► ConditionEvaluator ──► PlayerProgress
        ◄─ DialogueOpen ──┘                    │      ▲                                     ▲
 client ── DialogueChoose ──────────────────►  │      │ (offered option ids,               │ updated by
        ◄─ DialogueOpen / DialogueClose        │      │  per-player session)               │
                                               ▼      │                                    │
                                   action: OpenShop / BindPoint / SetFlag /          GameEvents (NpcKilled,
                                           ClearFlag / GotoNode / StartQuest /       NpcTalked, ItemGained/Lost,
                                           TurnInQuest / Close ─► QuestService ◄──── QuestChanged)
                                                                                            ▲
                                                                  combat, loot, crafting, inventory, dialogue
```

Everything runs on the **game thread** (`World` lane, see `server-threading.md`), so conditions read progress with
no locks. Persistence goes through `PersistenceService`, never inline (see `persistence.md`).

## 1. Player progress (`PlayerProgress`) — #233

Plain collections owned by `Player.Progress`, touched only by the game thread (same rule as `Inventory`).

| Data | Shape | Used for |
| :--- | :--- | :--- |
| Flags | `HashSet<string>` (1-64 chars: letters, digits, `_ - .`) | One-off story beats set by dialogue options ("heard_slime_history"). |
| Kill counts | `Dictionary<int templateId, int>` | `KillCount` conditions, statistics. Every kill counts, quest or not. |
| Quest log | `Dictionary<int questId, QuestProgress>` | State `Active` / `ReadyToTurnIn` / `Rewarded` plus objective counters. |

Tables `CharacterFlags`, `CharacterKillCounts` and `CharacterQuests` (composite keys, cascade delete) are written by
the write-behind queue and loaded at character select. Details in `persistence.md`.

## 2. Conditions (`ConditionEvaluator`) — #234

`Shared.Data.Condition` (so the Unity authoring tools can use the same model) with a closed set of types:

| Type | Fields | Holds when |
| :--- | :--- | :--- |
| `KillCount` | `Id` (NPC template), `Min` | the character killed at least `Min` of that template |
| `QuestState` | `Id` (quest), `State` | the quest is in that state (`Available` = not in the log) |
| `Flag` | `Name` | the flag is set |
| `Level` | `Min`, `Max` (0 = no limit) | the level is within the range |
| `HasItem` | `Id` (item template), `Min` | the backpack plus equipped items hold at least `Min` |

`Negate: true` inverts one condition. A list of conditions is an **AND**; OR is expressed with several greetings or
options. `ConditionEvaluator.Evaluate(player, conditions)` is pure (it only reads), and an unknown type fails closed.

## 3. Dialogue data (`Dialogues.json`) — #235

An NPC template points at a dialogue with `"DialogueId"` (this replaced the old `Interactions` list in `Npcs.json`).

```json
{
  "Id": "town_blacksmith",
  "Greetings": [
    { "Priority": 20,
      "Conditions": [ { "Type": "KillCount", "Id": 101, "Min": 10 },
                      { "Type": "Flag", "Name": "heard_slime_history", "Negate": true } ],
      "Text": "Hi {{playerName}}, I can buy and sell equipment. ... Want to know how they got here?",
      "Options": [
        { "Label": "Show me your shop",        "Action": "OpenShop", "Value": 15 },
        { "Label": "Tell me about the slimes", "Action": "GotoNode", "Node": "slime_history" },
        { "Label": "Goodbye",                  "Action": "Close" } ] },
    { "Priority": 0, "Text": "Hi {{playerName}}, ...", "Options": [ ... ] }
  ],
  "Nodes": {
    "slime_history": {
      "Text": "Long ago ...",
      "Options": [ { "Label": "Interesting.", "Action": "SetFlag", "Name": "heard_slime_history", "Node": "$greeting" } ]
    }
  }
}
```

- **Greeting selection:** the highest `Priority` whose conditions hold. One greeting must have no conditions (the
  fallback), and priorities are unique.
- **Options** have optional `Conditions` (hidden while false), an `Action`, an optional `Value` (shop id or quest id),
  `Name` (flag) and `Node`.
- **Where it goes next:** `GotoNode` shows `Node` (a node of the dialogue, or `$greeting` for the NPC's greeting).
  Every other action runs and then continues at `Node` when set, or ends the conversation when not.
- **Actions** (`Shared.Enums.InteractAction`): `Close`, `GotoNode`, `OpenShop` (waits for the vendor system, #151,
  and currently answers `NotAvailable`), `BindPoint`, `SetFlag`, `ClearFlag`, `StartQuest`, `TurnInQuest`.
- **Text tokens:** `{{playerName}}`, `{{playerLevel}}`, `{{npcName}}`. Unknown or unbalanced tokens fail validation.
  Text is sent already rendered by the server; every text has room for a later locale table.
- **Limits** (`Shared.Constants.DialogueRules`): text 600 chars, labels 80, at most 8 authored options per screen
  (the server sends at most 12 including generated quest options).
- **Validator rules** (`DataValidator`): unique dialogue ids, a fallback greeting, unique priorities, known tokens,
  options with a valid action and target, every screen can reach an option that ends the conversation, node names
  cannot start with `$` or `quest:`, conditions reference existing NPCs/quests/items, an NPC's `DialogueId` exists.

## 4. Conversation protocol and session — #236

| Packet | Direction | Payload |
| :--- | :--- | :--- |
| `EntityInteractRequest` | C→S | `targetId` (unchanged) |
| `DialogueOpen` | S→C | `npcId`, `npcName`, `text`, `optionCount` (byte), `{ optionId (int), label, InteractAction (byte) }[]` |
| `DialogueChoose` | C→S | `npcId`, `optionId` |
| `DialogueClose` | S→C | `npcId`, `DialogueCloseReason` (byte) |
| `DialogueClose` | C→S | `npcId` (the window was closed) |

`EntityInteractResponse` is now only sent for failures (`NotFound`, `TooFar`, `TargetDead`, `NothingToDo`) and for
`OpenShop` (`NotAvailable`).

The server keeps a `DialogueSession` on the player: the NPC, the screen and **exactly which options were offered**.
`DialogueChoose` is re-validated every time:

1. A session with that NPC exists, and `optionId` is one of the offered ids (so the client cannot pick something it
   was not shown; ids are per-screen indexes and say nothing about hidden branches).
2. The player is alive and the NPC exists, is alive and within 1.5 × the interact range (a small leeway so a
   conversation survives a step back).
3. The option's conditions still hold (they may have changed since the screen was shown).

The session also ends, with a `DialogueClose`, on the per-tick check in `Player.Update` (NPC died or gone, player died
or walked away), and silently on logout, respawn or disconnect (`MapInstance.RemovePlayer`).

## 5. Game events (`GameEvents`) — #237

A plain in-process dispatcher on the game thread. A failing subscriber is logged and does not stop the others.

| Event | Raised by | Consumers |
| :--- | :--- | :--- |
| `NpcKilled(player, templateId)` | `NPC.Die`, once per kill, to the killer only | kill counts (first), kill objectives |
| `NpcTalked(player, templateId)` | `DialogueService.Open` | talk objectives |
| `ItemGained` / `ItemLost` | loot, crafting, refinement, dropping, consumables, `InventoryOps` | collect objectives |
| `QuestChanged(player, questId)` | `QuestService` | (reserved for quest markers, #240) |

Known limit: equipping or unequipping moves an item between backpack and equipment without an event. Collect
objectives count the backpack only, they are re-counted when a conversation opens and always at turn-in, so the
turn-in itself is never wrong. Party-shared kill credit comes with the Party system.

## 6. Quest engine (`Quests.json`) — #238

```json
{
  "Id": 1, "Name": "The Slime Menace", "MinLevel": 1,
  "GiverNpcTemplateId": 200, "TurnInNpcTemplateId": 200,
  "Prerequisites": [],
  "Objectives": [ { "Type": "Kill", "Id": 101, "Count": 10 } ],
  "Rewards": { "Exp": 120, "Gold": 50, "Items": [ { "ItemTemplateId": 2001, "Quantity": 3 } ] },
  "Text": { "Offer": "...", "Progress": "...", "Complete": "..." }
}
```

- **States:** `Available` (not in the log, level and prerequisites met), `Active`, `ReadyToTurnIn`, `Rewarded`
  (final, no repeats). At most 20 quests in the log (`ProgressRules.MaxActiveQuests`).
- **Objectives:** `Kill` (counts from the moment of acceptance), `Talk` (each conversation), `Collect` (backpack
  count, already-held items count at acceptance). Counters are capped; all done means `ReadyToTurnIn`; a `Collect`
  quest drops back to `Active` if items are lost.
- **Dialogue integration:** the greeting of a giver/turn-in NPC gets generated options `[New]`, `[In progress]` and
  `[Complete]`, leading to server-built screens (`quest:offer:<id>`, `quest:progress:<id>` with the objective lines,
  `quest:complete:<id>`) with Accept / Complete quest buttons. Authors never write these.
- **Turn-in:** re-counts the held items, checks the whole turn-in fits the backpack (`InventoryOps.CanFit`, which
  counts the slots freed by handed-in items) and refuses with nothing changed otherwise. Then, in one batch: the quest
  becomes `Rewarded`, collected items are taken, gold and items granted, EXP last (its urgent save flushes the whole
  batch). A second turn-in finds the quest `Rewarded` and does nothing, so rewards are never granted twice.
- **Packets:** `QuestLogSync` (whole log, sent when entering the world), `QuestUpdate` (one quest; `Available` means
  it left the log) and `QuestAbandonRequest` (C→S). Accepting and turning in happen through dialogue options only.
- **Validator rules:** unique positive ids, giver and turn-in NPCs exist and **have a dialogue**, at least one and at
  most six objectives with existing targets, positive counts, rewards reference existing items within their
  `MaxStack`, texts valid, prerequisites valid and not self-referential.

## 7. Client (not implemented) — #172, #239, #240

The Unity client must render what the server sends and send nothing else: the dialogue window (`DialogueOpen` /
`DialogueChoose` / `DialogueClose`, click-to-interact with range feedback), the quest log and tracker (`QuestLogSync`,
`QuestUpdate`), and the `!` / `?` markers. The client holds no quest or dialogue rules. The opcodes are in
`Shared/Network/OpCode.cs` (protocol `0.2.3-alpha`).

## 8. Tooling and content (not implemented) — #225, #241, #242

Content is authored in Unity and exported to `Server/Data/`; the JSON stays the source of truth and the server's
`DataValidator` stays the final check (same rules as #225).

- **ScriptableObjects** `DialogueDefinition` and `QuestDefinition`. Conditions and actions are polymorphic lists
  (`[SerializeReference]`) with a type dropdown. NPC, quest and item targets are asset references, never typed ids.
- **Flag registry** asset: flags come from a dropdown, with a warning for flags that are set but never read (or the
  reverse).
- **Inline validation** in the Inspector mirrors `DataValidator` (`Shared.Constants.DialogueRules` and
  `ProgressRules` are shared for that purpose).
- **One export menu**, `MMORPG/Export All Static Data`: validates, then writes the files with Newtonsoft JSON; it
  refuses to write on any error.
- **Dialogue preview window**: pick a test state (kills, flags, level, quest states) and see which greeting and
  options the player would get. A visual node-graph editor is out of scope for v1.
- **Standalone validation command** (`DataManager`/`DataValidator` without sockets or database) runs in CI on changes
  to `Server/Data/**` (#242).

## 9. Security and abuse

- No client-supplied text, node ids or action names are trusted (only an `optionId` from the offered set).
- `DialogueChoose` goes through the per-client command backlog cap like every world packet; a conversation holds one
  small session object.
- Quest rewards and kill credit are decided server-side from events; a quest can only be accepted from its giver and
  turned in to its turn-in NPC, whatever dialogue the option came from.

## Issue map

| Order | Issue | Scope | Status |
| :--- | :--- | :--- | :--- |
| 1 | #233 S6-G34 Player progress state (flags, kill counts, quest log) | Server, Shared | done |
| 1 | #234 S6-G35 Condition system (`ConditionEvaluator`) | Server, Shared | done |
| 2 | #235 S6-G36 `Dialogues.json`, validation, `Npcs.json` migration | Server, Shared | done |
| 2 | #236 S6-G37 Dialogue session and protocol | Server, Shared | done |
| 2 | #172 S6-G8 NPC interaction, dialogue window and bind flow (client half) | Client | open |
| 3 | #237 S6-G38 Game events and kill credit | Server | done |
| 3 | #151 S6-G5 NPC vendor economy (`OpenShop` action) | Core | open |
| 4 | #238 S6-G39 Quest engine | Server, Core | done |
| 5 | #239 S6-G40 Quest log, tracker and quest windows | Client | open |
| 5 | #240 S6-G41 Quest markers over NPCs | Server, Client | open |
| 2 | #241 S6-G42 Unity authoring for dialogues and quests (export to JSON), builds on #225 | Tooling, Client | open |
| 2 | #242 S6-G43 Standalone static data validation command in CI | Tooling | open |

Related content and tooling: #232 (Blacksmith / Innkeeper prefabs and spawners, needed to meet them in the world).

## Open questions

- Party/shared kill credit for quests (depends on the Party system, Milestone 5).
- Repeatable and daily quests (reset rules, server-time vs player-time).
- Whether flags need an expiry or namespace to avoid unbounded growth (leaning: namespaced by quest/dialogue id).
- Text localization source (JSON per locale vs. a table) once a second language is needed.
- Equip/unequip events for collect objectives, if a quest ever asks for equippable items the player is likely to wear.
