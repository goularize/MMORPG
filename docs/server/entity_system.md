# World Entity System

The Entity System is the mathematical representation of all physical objects that exist in the game world. This logic is strictly authoritative and resides in the `Server.World.Entities` namespace.

## 1. Class Hierarchy (OOP)

To keep the codebase clean and avoid duplicating stats across different objects, the server uses classic Object-Oriented inheritance.

### The Base: `Entity`
Every moving or interactable object in the game is an `Entity`. 
It holds the universal data that all creatures share:
- **Core Identity:** `Id`, `Name`
- **Position:** `Vector3 Position` (X, Y, Z float coordinates). For 2D games, Z is simply ignored or used for depth-sorting.
- **Vitals:** `Health`, `MaxHealth`, `Mana`, `MaxMana`
- **Base RPG Stats:** `Strength`, `Intelligence`, `Constitution`, `Knowledge`

### The Player: `Player : Entity`
Inherits all stats from `Entity`, but represents a real human connected to the server.
- **`AccountId`:** A foreign key linking this character back to the database account that logged in. (1 Account can have many Characters).
- **`Connection`:** A reference to the `ClientConnection` class. This allows the World Server to instantly send network packets directly to this specific player.
- **Progression:** `Level`, `Exp`, `StatPoints`

### The NPC: `NPC : Entity`
Inherits all stats from `Entity`, but is controlled by the server.
- **`SpawnId`:** Tracks which map spawner created this monster.
- *Future implementation will include AI States (e.g., Patrol, Chase, Flee).*

## 2. The Entity Manager

The `EntityManager` (`Server/World/EntityManager.cs`) is the central database *in RAM* for the running game.

It uses `ConcurrentDictionary` to safely store references to every active Player and NPC. Thread safety is critical here, because the Networking threads (receiving packets) and the GameLoop thread (moving players) might try to access the same player at the same time.

## 3. The Update Cascade (Tick Rate)

Entities do not update themselves, nor do they instantly move when a network packet arrives. Instead, they wait for the server's authoritative heartbeat.

This is the exact flow of data 30 times a second:
1. `Program.cs` starts `GameLogic.Start()`.
2. Every 33.33ms, `GameLogic` fires its `Update()` method.
3. `GameLogic` calls `EntityManager.Update()`.
4. `EntityManager` loops through all thousands of active players and NPCs, calling `Update()` on each one individually.

This strict synchronous pipeline prevents race conditions (e.g., two players attacking the exact same monster at the exact same millisecond).
