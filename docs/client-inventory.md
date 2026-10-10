# Client Inventory and Paperdoll Window — #148

The Unity client shows the backpack, the equipped items and the gold, and sends item actions. It holds no item rules:
every action is a request, and the window changes only when the server's answer arrives. Opcodes are in
`Shared/Network/OpCode.cs` (protocol `0.2.7-alpha`).

## Protocol

| Direction | OpCode | Payload |
| --- | --- | --- |
| Server -> Client | `InventorySync` | gold (`long`), slotCount, itemCount, `Item[]` (at world entry, after loot-all, after a quest reward) |
| Server -> Client | `InventorySlotUpdate` | bag, slot, hasItem (bool), `Item` when hasItem (no item = the slot is empty) |
| Server -> Client | `EquippedItemsSync` | count, { `EquipmentSlot` (byte), `Item` }[] (the whole paperdoll, resent on every change) |
| Client -> Server | `MoveInventoryItemRequest` | fromBag, fromSlot, toBag, toSlot (moves, stacks or swaps) |
| Client -> Server | `SplitItemStackRequest` | bag, slot, amount, targetBag, targetSlot (target must be empty) |
| Client -> Server | `UseConsumableItemRequest` | bag, slot |
| Client -> Server | `EquipItemRequest` | bag, slot, `EquipmentSlot` (byte) |
| Client -> Server | `UnequipItemRequest` | `EquipmentSlot` (byte); the server picks the first free backpack slot |
| Client -> Server | `DropItemRequest` | bag, slot, quantity (destroys the items; nothing is put in the world) |

`Item` (`InventoryHandler.WriteItemData`, layout pinned by `PacketLayoutTests`): id, templateId, bag, slot, quantity,
upgradeLevel, rarity, the 8 rolled primary stats, the 5 rolled secondary floats, health and mana regeneration, then the
**display data of the template**: name, description, `ItemType`, `EquipmentSlot`, requiredLevel, maxStack, basePrice,
isTwoHanded, icon. The client has no item catalog; each item carries what its window and tooltip show.
Only the main backpack (bag 0) exists for now. Backpack contents, equipment and gold are cleared on logout.

## Client pieces (`Client/Assets/Scripts/`)

- `Network/Handlers/InventoryHandler.cs` — the mirror (`ItemView`, backpack by slot, equipment by slot, gold), the
  `OnInventoryChanged` event and the request methods.
- `UI/InventoryUI.cs` — the window (**I** toggles, Esc closes): grid, paperdoll cells, gold, used slots, a feedback line,
  drag and drop, and a destroy confirmation.
- `UI/InventorySlotUI.cs` — one cell; reports clicks, drags and hovers to `InventoryUI`.
- `UI/ItemTooltipUI.cs`, `UI/ItemFormat.cs` — the tooltip that follows the mouse, rarity colours, stat lines.

## Controls

| Action | Input |
| --- | --- |
| Move / swap / stack | drag a cell onto another |
| Split a stack in half | Shift + drag onto an empty cell |
| Equip | drag the item onto its paperdoll slot, or right click / double click it |
| Unequip | drag the equipped item onto the backpack, or right click / double click it |
| Use a consumable | right click / double click |
| Destroy | drag outside the window, then confirm |
| Tooltip | hover |

The client pre-checks only what saves a round trip (item fits the slot, character level, backpack full) and shows a
short message; the server validates everything again.

## Icons

Icons are looked up by the template's `Icon` name as `Resources/ItemIcons/<Icon>` (a Sprite). Without a sprite the cell
shows the item's initials on its rarity colour, so the window works before any art exists.

## Not done yet

Extra bags, hotbar, sorting, item refinement and crafting (#175), loot window (ROADMAP: Client Ground Loot).
