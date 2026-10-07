# Static Data Loading (`DataManager`)

Items, recipes, loot tables, NPC templates, spawners and resource nodes are JSON files in `Server/Data/`, loaded once at startup by
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
- **Interactions**: each entry's `Action` must be an `InteractAction` (`None`, `BindPoint`, `OpenShop`; anything else
  fails startup). `InteractHandler` runs the first non-`None` action of the target and always answers with an
  `EntityInteractResponse` (`targetId`, `InteractOutcome`, `InteractAction`): `Success`, `NotFound`, `TooFar`,
  `TargetDead`, `NothingToDo` or `NotAvailable`. `BindPoint` binds the player where they stand and also sends the usual
  `SetBindPointResponse`. `SetBindPointRequest` on its own is only honored within `InteractRange` of a living NPC whose
  template offers `BindPoint` (the Town Innkeeper, id 201); otherwise it answers `success = false` with the old bind
  point. `OpenShop` answers `NotAvailable` until the vendor system lands (#151). `ConditionType` is not evaluated yet.

Not done yet (tracked on #166): the Goblin Looter, Town Blacksmith and Town Innkeeper have no spawner because their `Entity_Goblin` / `Entity_HumanBlacksmith` / `Entity_HumanInnkeeper` client prefabs do not exist.
