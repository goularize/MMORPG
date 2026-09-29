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

## 4. Area of Interest (AoI)

To prevent the server from sending the positions of every single monster in the world to every single player, the `EntityManager` implements **Area of Interest (AoI)** calculations.

### How it Works
At the end of every tick, `EntityManager.ProcessAreaOfInterest()` runs:
1. It loops through all `Player`s.
2. It calculates the **Euclidean Distance** between the `Player` and every other `Entity` (`Vector3.Distance`). Because we use 3 axes (X, Y, Z), this math naturally works flawlessly for both full 3D games and 2D games (where Z simply remains 0).
3. If an Entity is within **50.0f units** of the Player, it is considered "in range".

### Network State & Synchronization
To tell the client when to spawn or destroy GameObjects, the Server needs to track state. Every `Player` has a `HashSet<int> KnownEntities` property.
* **`EntitySpawn`**: If a nearby entity's ID is *not* in `KnownEntities`, they just walked into range. The server adds them to the set and sends an `EntitySpawn` packet with their full data so the client can instantiate the model.
* **`EntityPositionUpdate`**: If the entity is *already* in `KnownEntities`, the server just sends an `EntityPositionUpdate` packet with the new `Vector3` coordinates.
* **`EntityDespawn`**: If an ID was in `KnownEntities` but is no longer within the 50.0f radius, they walked away. The server removes them from the set and sends an `EntityDespawn` packet so the client can destroy the model.
