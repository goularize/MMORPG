# Static Data Loading (`DataManager`)

Items, recipes, loot tables, NPC templates, dialogues, quests, spawners and resource nodes are JSON files in `Server/Data/`, loaded once at startup by
`DataManager.Initialize()`.

## Fail fast

Loading is all-or-nothing. If any file is missing, malformed or inconsistent, `Initialize` throws a
`DataLoadException` that lists **every** problem found, the server does not start, and the previously loaded tables
(if any) are left untouched. A server never runs on empty or half-loaded data.

## What `DataValidator` checks

| Area | Rule |
| :--- | :--- |
| Ids | No duplicate `TemplateId` / `RecipeId` / loot-table `NpcTemplateId` (nothing is silently dropped). |
| Items | Positive id, non-empty name, a known `Type`, `MaxStack >= 1`. |
| NPCs | `Behavior` must parse as a `MobBehaviorType` (no silent fallback to Passive); `LevelRange` / `StatVarianceRange` are `[min, max]`. |
| Loot tables | Entries reference existing items; chances in `[0, 1]`; `1 <= Min <= Max` quantities; sane gold range. |
| Recipes | Result and ingredients reference existing items; quantities `>= 1`; at least one ingredient. |
| Character creation | `CharacterCreation.json`: vitals, base stats, gold, start map/position and starter items; starter items must exist, fit their `MaxStack` and the backpack. |
| Resource nodes | `ResourceNodes.json`: positive unique id, name, `MaxHealth >= 1`, drops reference existing items with valid chance and quantity range (no drops is only a warning). `ResourceSpawns.json` entries must reference an existing node. |
| Spawners | Reference an existing NPC template; `Amount >= 1`; non-negative radius. |

A loot table whose NPC does not exist yet is only a **warning** (planned content, see S4-G5): it is reported at
startup and ignored.

Tests: `DataManagerTests` (throwaway data directories) and `ShippedData_PassesValidation`, which keeps the real
files valid.

## Character creation

`CharacterCreation.json` defines what a new character starts with (map, position, HP/MP, base stats, gold and the
starter kit placed in backpack slots 0, 1, 2... in file order). `CharacterHandler` stores the character and its kit
in a **single** `SaveChanges`, so a failure can never leave a kit-less character; if a starter item cannot be built
the creation is refused and nothing is saved.

## Maps (`MapManager`)

Map files live in `Server/Data/Maps/<id>_<Name>.json` and are read from `Data/Maps` next to the executable (they are
copied to the build and publish output, so `dotnet publish` and Docker work from any working directory). Loading is
fail-fast, like the tables above: a missing folder, no map files, a file name without a map id, a duplicate id,
malformed JSON or a collider with fewer than 3 points throws a `MapLoadException` listing every problem. The server
no longer invents empty, collider-free maps.

Data that names a map is checked against the loaded maps at startup: the `StartMapId` in `CharacterCreation.json` and
every spawner's `MapId`. Spawners are routed by their own `MapId` (they are not tied to map 1).

## Resource nodes

Harvestable nodes (trees, ore veins) are data-driven. `ResourceNodes.json` defines the node types (health, defense,
respawn time and a `Drops` list shaped like a loot table entry) and `ResourceSpawns.json` places them
(`TemplateId`, `MapId`, `X/Y/Z`). `MapManager` creates one `Resource` per spawn at startup, routed by its `MapId`; a
spawn naming a missing map stops startup like NPC spawners do. When a node is depleted it drops a loot satchel rolled
from its template's `Drops`, replacing the old hardcoded Iron Ore / Oak Wood ids. A `Resource` built without a template
drops nothing. Spawn positions are checked to be walkable by `ShippedResourceNodes_AreSpawnedOnTheirMapsOnWalkableGround`.

## NPC templates

`Npcs.json` entries are applied to each spawned NPC by `NPC.ApplyTemplate`:

- **Level**: rolled per spawn, uniformly inside `LevelRange`. It feeds the derived stats and the mitigation formula
  (`40 * Level`), and the EXP yield is `BaseExp * Level`.
- **Stat variance**: each base stat is multiplied by its own factor rolled inside `StatVarianceRange`, so spawns of one
  template differ slightly.
- **Type**: `Enemy` or `Friendly` (anything else fails startup). A `Friendly` template always gets the `Friendly`
  behavior, which makes it unattackable, and is sent to clients as `EntityType.Npc` instead of `Enemy`.
- **DialogueId**: the `Dialogues.json` entry shown when a player interacts with the NPC (see "Dialogues" below); an NPC
  without one answers `NothingToDo`. `InteractHandler` checks existence, area of interest, range and that the target is
  alive, then opens the conversation; failures are answered with an `EntityInteractResponse` (`targetId`,
  `InteractOutcome`, `InteractAction`): `NotFound`, `TooFar`, `TargetDead` or `NothingToDo`. `SetBindPointRequest` on
  its own is only honored within `InteractRange` of a living NPC whose dialogue offers a `BindPoint` option (the Town
  Innkeeper, id 201); otherwise it answers `success = false` with the old bind point.

Not done yet (tracked on #166): the Goblin Looter, Town Blacksmith and Town Innkeeper have no spawner because their `Entity_Goblin` / `Entity_HumanBlacksmith` / `Entity_HumanInnkeeper` client prefabs do not exist.

## Dialogues and quests

`Dialogues.json` and `Quests.json` (both required, `[]` is fine) hold NPC conversations and quests. The full model,
the conversation protocol and the quest rules are in `docs/npc-dialogue-and-quests.md`; this is the startup validation.

| Area | Rule |
| :--- | :--- |
| Dialogues | Unique `Id` (1-64 chars: letters, digits, `_ - .`). At least one greeting, one of them without conditions (the fallback), unique `Priority`. |
| Text | Greeting, node, option label and quest text are non-empty, at most 600 (labels 80) characters, and only use the known `{{playerName}}`, `{{playerLevel}}`, `{{npcName}}` tokens, balanced. |
| Options | 1 to 8 per screen. `Action` must be set; `GotoNode` needs a `Node`; `Close` cannot have one; `SetFlag`/`ClearFlag` need a valid `Name`; `StartQuest`/`TurnInQuest` must reference an existing quest; a `Node` must be a node of the dialogue or `$greeting`. |
| Reachability | Every greeting and node must be able to reach an option that ends the conversation. Node names cannot be empty or start with `$` or `quest:` (reserved for the server). |
| NPCs | A `DialogueId` must exist. A dialogue no NPC uses is a warning. |
| Conditions | `Type` known; `KillCount` NPC exists and `Min >= 1`; `QuestState` quest exists; `Flag` name valid; `Level` `Min >= 1` and `Max` 0 or `>= Min`; `HasItem` item exists and `Min >= 1`. |
| Quests | Unique positive `Id`, name, `MinLevel >= 1`. Giver and turn-in NPCs exist and have a `DialogueId`. 1 to 6 objectives (`Kill`/`Talk` target an NPC template, `Collect` an item, `Count >= 1`). Rewards: `Exp`/`Gold >= 0`, items exist and their quantity fits `MaxStack`. Offer/Progress/Complete texts valid. Prerequisites valid and not about the quest itself. |

Tests: `DialogueDataValidationTests`, plus `ShippedData_PassesValidation` for the real files.
