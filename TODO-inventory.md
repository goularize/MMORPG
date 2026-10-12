# Inventory work: what is left

Written 2026-10-12, after PR #259 (inventory window) was merged.

## Waiting on you (branch `feat/client-hud-buttons`, 5 commits, not pushed)
- [ ] Push the branch (`git push -u origin feat/client-hud-buttons`) and open the PR into `develop`.
  - The PR title must follow Conventional Commits, e.g. `feat(client/ui): HUD buttons and item icon folder`. CI ("PR Rules") rejects `[Client] ...` titles.
  - Use `Fixes #148` in the PR body if you consider the inventory window finished, otherwise `Refs #148`.
- [ ] Assign the three buttons on `HudButtonsUI` in the scene (Inventory, Quest Log, Logout) if not done yet.

## Item icons
- [ ] Add the missing icon sprites to `Client/Assets/Resources/ItemIcons/` (only `sword_iron` exists). Names from `Items.json`: `sword_steel`, `staff_wood`, `shield_wood`, `helmet_leather`, `tunic_novice`, `boots_leather`, `ring_copper`, `amulet_bone`, `potion_health_minor`, `potion_mana_minor`, `ore_iron`, `ingot_iron`, `wood_oak`, `gem_radiant`, `scroll_recipe`, `stone_weapon_upgrade`, `stone_armor_upgrade`.
- [ ] Editor tool that lists every `Icon` name in `Items.json` with no sprite in the folder.
- [ ] Check the `InventorySlot` prefab with real icons: icon margins, and whether the rarity tint (`ItemFormat.RarityColor`, applied to the cell background) looks right over your frame sprite. Maybe tint a separate border instead.

## Inventory window polish
- [ ] Tooltip is one rich-text block (`ItemFormat.Tooltip`). Split it into header / stats / description objects if you want separate styling.
- [ ] Check the tooltip stat units. Crit chance, crit damage, dodge, movement speed and attack speed are shown as "%" or plain numbers, but I never confirmed what the server's rolled values mean.
- [ ] Hotkey and button feedback: no sound, no highlight on the HUD buttons when the window is open.
- [ ] Sorting / auto-stack button, and a search or filter.

## Server limits that show in the client
- [ ] Drag-outside "Destroy" really deletes the item (server `HandleDropItem`). Decide if it should drop a loot satchel into the world instead.
- [ ] `UnequipItemRequest` cannot name a target slot; the server uses the first free one.
- [ ] Only bag 0 exists. Extra bags need server and client changes (`Player.InventorySlots` is the size).
- [ ] Existing server issues: #138 (inventory thread-safety), #140 (`HandleLootAll` partial stack merges).

## Next features (ROADMAP, Milestone 2)
- [ ] Client Ground Loot & Harvesting UI: loot window with "Loot" and "Loot All". The server already sends `LootSatchelSync`; item data in it now carries the display fields too.
- [ ] Crafting, recipe learning and refinement UI (#175).
- [ ] Character sheet and stat point allocation UI (#174).
- [ ] Gold & NPC vendor economy (buy/sell dialogue, repair costs).

## Housekeeping
- [ ] `CONTRIBUTING.md` says `[Client]` style tags are encouraged for PR titles, but CI rejects them. Fix the wording.
- [ ] Tests: the Unity client has no automated tests. Only the packet layout is pinned (`PacketLayoutTests`).
- [ ] If you want these tracked the project way, turn each section into GitHub issues linked to the "MMORPG" project.
