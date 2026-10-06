# Static Data Loading (`DataManager`)

Items, recipes, loot tables, NPC templates and spawners are JSON files in `Server/Data/`, loaded once at startup by
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
