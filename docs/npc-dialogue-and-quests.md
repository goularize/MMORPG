# NPC Dialogue, Progression and Quests (architecture proposal)

Status: **proposal** (nothing here is implemented yet). Tracking issues are listed in [Issue map](#issue-map).

Talking to an NPC, having it react to what the player has done, and handing out quests are one problem: *the server
decides what an NPC says and offers, based on persistent per-character progress.* This document defines the pieces
and how they fit the existing server (authoritative, single game thread, write-behind persistence, fail-fast static
data).

## Goals

- NPC text and choices are **data** (JSON), validated at startup like every other static table.
- What an NPC says depends on the **player's progress** (quests, flags, kills, level, items).
- The server stays **authoritative**: the client only asks to open a conversation and to pick one of the options the
  server just offered.
- One **condition system** shared by dialogue, quests and, later, loot, spawns and vendors.
- Progress is **persistent** through the existing write-behind queue.

Non-goals (for now): voice acting, cutscenes, branching narrative tooling, timed world events, party-shared quests.

## Current state

| Piece | Today |
| :--- | :--- |
| `Npcs.json` `Interactions` | List of `{ConditionType, ConditionValue, Action, ActionValue}`. Conditions are **ignored**; `InteractHandler` runs the first interaction with a known action (`BindPoint` works, `OpenShop` answers `NotAvailable`). |
| `InteractHandler` | Validates existence, AoI, range, alive, then replies `EntityInteractResponse(targetId, outcome, action)`. No conversation, no text. |
| Client | No NPC window; `EntityInteractRequest` has no client caller (#172). |
| Player progress | Level, EXP, stats, bind point, inventory, recipes. **No flags, kill counters or quest state.** |
| Quests | Roadmap item only (Milestone 5), no issue before this proposal. |

## Overview

```
                         Dialogues.json   Quests.json      (static data, DataValidator, fail-fast)
                               │               │
 client ── Interact ──►  InteractHandler ──► DialogueService ──► ConditionEvaluator ──► PlayerProgress
        ◄─ DialogueOpen ──┘                    │      ▲                                     ▲
 client ── DialogueChoose ──────────────────►  │      │ (offered option ids,               │ updated by
        ◄─ DialogueOpen / DialogueClose        │      │  per-player session)               │
                                               ▼                                            │
                                   action: OpenShop / BindPoint / SetFlag /          GameEvents (NpcKilled,
                                           GotoNode / StartQuest / CompleteQuest      ItemGained, QuestChanged)
                                                                                            ▲
                                                                       GameLogic.Update() (combat, loot, ...)
```

Everything on the left runs on the **game thread** (`World` lane, see `server-threading.md`), so conditions read
progress with no locks. Persistence happens through `PersistenceService`, never inline.

## 1. Player progress (`PlayerProgress`)

Plain collections owned by `Player`, touched only by the game thread (same rule as `Inventory`).

| Data | Shape | Used for |
| :--- | :--- | :--- |
| Flags | `HashSet<string>` (max 64 chars each) | "met_augustos", "heard_slime_history": one-off story beats set by dialogue/quest actions. |
| Kill counts | `Dictionary<int templateId, int>` | `KillCount` conditions, kill objectives, future statistics/achievements. |
| Quest log | `Dictionary<int questId, QuestProgress>` | State `Active` / `ReadyToTurnIn` / `Rewarded` plus objective counters. |

Database (EF migration): `CharacterFlags(CharacterId, Flag)`, `CharacterKillCounts(CharacterId, NpcTemplateId, Count)`,
`CharacterQuests(CharacterId, QuestId, State, ProgressJson, UpdatedAt)`, all with a composite key and cascade delete.
`CharacterState` snapshots gain these collections so the existing per-character, coalesced, atomic flush covers them.
They are loaded in `CharacterSelectRequest` (off-thread) before the `Player` is posted to the game thread, like
inventory and recipes. Quest completion and rewards use `Expedite` (value-bearing change), kill counts do not.

## 2. Conditions (`ConditionEvaluator`)

One small closed set, in `Shared` so the client can show requirements, evaluated only by the server.

```json
{ "Type": "KillCount", "Id": 101, "Min": 10 }
{ "Type": "QuestState", "Id": 5, "State": "Rewarded" }
{ "Type": "Flag", "Name": "heard_slime_history", "Negate": true }
{ "Type": "Level", "Min": 10 }
{ "Type": "HasItem", "Id": 15, "Min": 1 }
```

- A list of conditions is an **AND**. OR is expressed with several greetings/options.
- `Negate` inverts one condition (instead of a `FlagNotSet` type per case).
- Types are a C# enum shared with the client; an unknown name fails startup (`DataValidator`), like `InteractAction`.
- Referenced ids (NPC templates, quests, items) are validated to exist.

The evaluator is pure (`Evaluate(Player, IReadOnlyList<Condition>) -> bool`), so it is trivially unit-testable.

## 3. Dialogue data (`Dialogues.json`)

An NPC template references a dialogue by id; `Interactions` in `Npcs.json` is replaced by `DialogueId`
(a migration step converts the shipped Blacksmith/Innkeeper).

```json
{
  "Id": "augustos_blacksmith",
  "Greetings": [
    { "Priority": 20,
      "Conditions": [ { "Type": "KillCount", "Id": 101, "Min": 10 },
                      { "Type": "Flag", "Name": "heard_slime_history", "Negate": true } ],
      "Text": "Hi {{playerName}}, I can buy and sell equipment. Want to see my shop? I also heard you killed 10 slimes. Want to know how they got here?",
      "Options": [
        { "Label": "Show me your shop",         "Action": "OpenShop",  "Value": 15 },
        { "Label": "Tell me about the slimes",  "Action": "GotoNode",  "Node": "slime_history" },
        { "Label": "Goodbye",                   "Action": "Close" } ] },
    { "Priority": 0,
      "Text": "Hi {{playerName}}, I can buy and sell equipment. Want to see my shop?",
      "Options": [
        { "Label": "Show me your shop", "Action": "OpenShop", "Value": 15 },
        { "Label": "Goodbye",           "Action": "Close" } ] }
  ],
  "Nodes": {
    "slime_history": {
      "Text": "Long ago, the slimes ...",
      "Options": [ { "Label": "Interesting.", "Action": "SetFlag", "Name": "heard_slime_history" },
                   { "Label": "Back",         "Action": "GotoNode", "Node": "$greeting" } ] }
  }
}
```

- **Greeting selection:** highest `Priority` whose conditions pass; ties are rejected by the validator.
- **Options** may carry `Conditions` (hidden when false) and optionally a `Disabled` display mode later.
- **Actions** (closed enum, `Shared`): `Close`, `GotoNode`, `OpenShop` (#151), `BindPoint` (existing `BindHandler`),
  `SetFlag`, `ClearFlag`, `StartQuest`, `TurnInQuest`. Adding one means adding a case, a validator rule and a test.
- **Text tokens:** `{{playerName}}`, `{{playerClass}}`, `{{npcName}}`. Unknown tokens fail validation.
- **Localization-ready:** every text has an implicit key (`<dialogueId>.<node>.<n>`); v1 sends the rendered string,
  a later locale table can replace the source without a protocol change.
- **Validator rules:** unique ids, every `GotoNode` target exists, no node without a way out (`Close` reachable),
  referenced NPC/quest/item ids exist, all tokens known, an NPC's `DialogueId` exists, a `Friendly` NPC with a dialogue
  is not attackable (already enforced by the Friendly type).

## 4. Conversation protocol and session

New opcodes (names indicative): `DialogueOpen` (S→C), `DialogueChoose` (C→S), `DialogueClose` (both).
`EntityInteractRequest` keeps its role as "talk to this entity"; `EntityInteractResponse` keeps reporting failures
(`NotFound`, `TooFar`, `TargetDead`, `NothingToDo`).

```
C→S  EntityInteractRequest   int targetId
S→C  DialogueOpen            int npcId, string npcName, string text,
                             byte optionCount, { int optionId, string label, byte icon }[ ],
                             (quest section later: available / in-progress quest ids)
C→S  DialogueChoose          int npcId, int optionId
S→C  DialogueOpen | DialogueClose(reason) | the effect of the action (shop window, bind result, quest update)
```

Server state: `Player.DialogueSession { NpcId, NodeId, HashSet<int> OfferedOptionIds }`, created when a dialogue
opens, replaced by each new node, cleared on close.

Validation on **every** `DialogueChoose`:
1. The player is alive and has a session with that `npcId`.
2. `optionId` is in `OfferedOptionIds` (the client cannot choose what it was not shown).
3. The NPC still exists and the player is still within `InteractRange` (else `DialogueClose(TooFar)`).
4. Option conditions are re-evaluated (state may have changed since the node was shown).

The session is also closed on death, map change, logout and disconnect. Option ids are per-session indexes, never
database or node names, so the client learns nothing about hidden branches.

## 5. Game events (`GameEvents`)

Progress is updated by events published from the systems that already own the facts, always on the game thread:

| Event | Published by | Consumers |
| :--- | :--- | :--- |
| `NpcKilled(player, templateId)` | `GameLogic` combat/death resolution (killer credit; party credit later) | kill counts, kill objectives |
| `ItemGained(player, itemId, qty)` / `ItemLost` | `ItemFactory`/inventory handlers, loot, crafting | collect objectives |
| `NpcTalked(player, templateId)` | `DialogueService` | deliver/talk objectives |
| `QuestChanged(player, questId)` | quest engine | quest markers, UI sync |

Keeping this as a plain in-process dispatcher (a list of subscribers invoked synchronously) avoids any async
complexity and keeps ordering deterministic inside the tick.

## 6. Quest engine

`Quests.json` defines static quests; per-character progress is `PlayerProgress` above.

```json
{
  "Id": 5, "Name": "The Slime Menace", "MinLevel": 1,
  "GiverNpcTemplateId": 200, "TurnInNpcTemplateId": 200,
  "Prerequisites": [ { "Type": "QuestState", "Id": 4, "State": "Rewarded" } ],
  "Objectives": [ { "Type": "Kill", "Id": 101, "Count": 10 } ],
  "Rewards": { "Exp": 200, "Gold": 50, "Items": [ { "ItemId": 7, "Quantity": 1 } ] },
  "Text": { "Offer": "...", "Progress": "...", "Complete": "..." }
}
```

- States: `Available` (computed from prerequisites), `Active`, `ReadyToTurnIn`, `Rewarded`.
- Objectives: `Kill`, `Collect` (counts items held), `Talk`/`Deliver`. Progress is capped per objective.
- Turning in re-validates objectives and inventory space, consumes collect items, then grants rewards atomically
  (single mutation + `Expedite`), so a crash cannot grant twice or lose items.
- Quest text for offer/progress/complete uses the same token and localization rules as dialogue.
- Dialogue integration: the greeting of a quest giver lists the quests it can offer/accept/turn in as extra options,
  generated by the server from `Quests.json` (the dialogue author does not hand-write them).

## 7. Client

- **Dialogue window:** NPC name, text, option buttons, close on `DialogueClose` or when walking away (#172).
- **Click-to-interact** with range feedback; options with an icon (bubble, shop, anvil, bed).
- **Quest log and tracker** (objective counters updated by `QuestChanged`), **quest markers** (`!` available,
  `?` ready to turn in) over NPC heads, driven by a per-NPC marker byte on entity spawn/update.
- The client holds no quest or dialogue rules: it renders what the server sends.

## 8. Tooling and content

Content is authored in Unity and exported to `Server/Data/`; the JSON stays the source of truth and the server's
`DataValidator` stays the final check (same rules as #225).

- **ScriptableObjects** `DialogueDefinition` and `QuestDefinition`. Conditions and actions are polymorphic lists
  (`[SerializeReference]`) with a type dropdown. NPC, quest and item targets are asset references (`NpcDefinition`,
  item definitions), never typed ids.
- **Flag registry** asset: flags come from a dropdown, with a warning for flags that are set but never read (or the
  reverse).
- **Inline validation** in the Inspector mirrors `DataValidator` (unreachable `Close`, missing `GotoNode` target,
  unknown token, priority ties, duplicate ids).
- **One export menu**, `MMORPG/Export All Static Data`: validates, then writes `Dialogues.json`, `Quests.json` and the
  other authored files with Newtonsoft JSON; it refuses to write on any error.
- **Dialogue preview window**: pick a test state (kills, flags, level, quest states) and see which greeting and
  options the player would get.
- **One-time import** of the shipped `Npcs.json` interactions into dialogue assets.
- A visual node-graph editor is out of scope for v1.
- **Standalone validation command** (`DataManager`/`DataValidator` without sockets or database) runs in CI on changes
  to `Server/Data/**`, so broken content cannot be merged unnoticed.
- `docs/static-data.md` gets a section for each new file; the validator rules above are covered by `DataManagerTests`
  plus `ShippedData_PassesValidation`.

## 9. Security and abuse

- No client-supplied text, node ids or action names are trusted (only an `optionId` from the offered set).
- Rate limit `DialogueChoose` per client with the existing command backlog cap; a conversation holds no long-lived
  server resources beyond a small session object.
- Quest rewards and kill credit are decided server-side from events; killing credit follows the existing loot
  ownership rules.

## Worked example: Augustos

1. First visit: no flags, 0 slime kills. Greeting priority 0 matches. The player sees the shop offer.
2. The player kills 10 slimes: each `NpcKilled(101)` increments the kill count (persisted by the write-behind queue).
3. Next visit: priority 20 matches (`KillCount(101) >= 10`, flag `heard_slime_history` not set). The extra option shows.
4. The player picks "Tell me about the slimes": `GotoNode slime_history`, then "Interesting." runs `SetFlag`.
5. Afterwards priority 20 no longer matches, so the NPC falls back to the plain greeting.

The same beat could instead be driven by the quest *The Slime Menace* (`QuestState 5 = Rewarded`), which also gives
a log entry and rewards. Both use the same evaluator.

## Issue map

| Order | Issue | Scope |
| :--- | :--- | :--- |
| 1 | #233 S6-G34 Player progress state (flags, kill counts, quest log) | Server, Shared |
| 1 | #234 S6-G35 Condition system (`ConditionEvaluator`) | Server, Shared |
| 2 | #235 S6-G36 `Dialogues.json`, validation, `Npcs.json` migration | Server, Shared |
| 2 | #236 S6-G37 Dialogue session and protocol | Server, Shared |
| 2 | #172 S6-G8 NPC interaction, dialogue window and bind flow (client half) | Client |
| 3 | #237 S6-G38 Game events and kill credit | Server |
| 3 | #151 S6-G5 NPC vendor economy (`OpenShop` action) | Core |
| 4 | #238 S6-G39 Quest engine | Server, Core |
| 5 | #239 S6-G40 Quest log, tracker and quest windows | Client |
| 5 | #240 S6-G41 Quest markers over NPCs | Server, Client |
| 2 | #241 S6-G42 Unity authoring for dialogues and quests (export to JSON), builds on #225 | Tooling, Client |
| 2 | #242 S6-G43 Standalone static data validation command in CI | Tooling |

Related content and tooling: #232 (Blacksmith / Innkeeper prefabs and spawners), #225 (NPC authoring). #116 was
already fixed by #230 and can be closed.

## Open questions

- Party/shared kill credit for quests (depends on the Party system, Milestone 5).
- Repeatable and daily quests (reset rules, server-time vs player-time).
- Whether flags need an expiry or namespace to avoid unbounded growth (leaning: namespaced by quest/dialogue id).
- Text localization source (JSON per locale vs. a table) once a second language is needed.
