# Requirement Specification: Character Management

This document outlines the specific requirements to fulfill the "Character Management" milestone from Phase 2 of our classic MMORPG roadmap.

## 1. Overview
Currently, an `Account` only contains a Username and a PasswordHash. To play the game, an account must be able to create, list, delete, and select a `Character`. The character represents their actual avatar in the 3D world.

## 2. Database Models

We need a new Entity Framework model: `Character.cs`.
It must have a Foreign Key relation to the `Account`.

**Proposed Fields:**
* `Id` (Primary Key)
* `AccountId` (Foreign Key -> Account)
* `Name` (String, Must be globally unique across all accounts)
* `ClassType` (Enum: e.g., Warrior, Mage, Archer)
* `Level` (Integer, Default: 1)
* `AppearanceData` (String/JSON or simple Int representing hair/face choice)
* `PositionX`, `PositionY`, `PositionZ` (Floats, representing where they last logged out)

## 3. Network Protocol (OpCodes)

We need to expand `Shared.Network.OpCode` to handle the Lobby/Character Screen phase:

* **Client -> Server**
  * `CharacterListRequest`: Sent automatically after a successful sign in.
  * `CharacterCreateRequest`: Sent with Name, Class, and Appearance.
  * `CharacterDeleteRequest`: Sent with CharacterId.
  * `CharacterSelectRequest`: Sent when the player clicks "Enter World" with a specific CharacterId.

* **Server -> Client**
  * `CharacterListResponse`: Contains an array/list of the account's characters.
  * `CharacterCreateResponse`: Success/Fail boolean and a message (e.g., "Name taken").
  * `CharacterDeleteResponse`: Success/Fail boolean.
  * `CharacterSelectResponse`: Tells the client to load the 3D game scene and supplies the starting X/Y/Z coordinates.

## 4. System Flow & Logic

### Flow A: The Lobby (Character Screen)
1. Client logs in successfully via `AuthHandler`.
2. Client sends `CharacterListRequest`.
3. Server queries `db.Characters.Where(c => c.AccountId == session.AccountId)` and sends the list.
4. Client renders the 3D models of the characters in a lobby scene.

### Flow B: Creation
1. Client sends `CharacterCreateRequest("Gandalf", ClassType.Mage)`.
2. Server validates name uniqueness in the database.
3. Server saves the new `Character` with default starting coordinates (e.g., X:0, Y:0, Z:0).
4. Server replies with `CharacterCreateResponse(Success)`.

### Flow C: Entering the World (World Spawning)
1. Client sends `CharacterSelectRequest(CharacterId: 5)`.
2. Server validates that Character 5 belongs to the authenticated Account.
3. Server transitions the network session from "Lobby State" to "In-Game State".
4. Server instantiates a new `Player : Entity` object using the DB data.
5. Server adds the `Player` to `EntityManager.Players`.
6. The Area of Interest (AoI) system naturally detects the new player and broadcasts `EntitySpawn` to nearby clients!
