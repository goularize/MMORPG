# MMORPG Base Architecture Design

This document outlines the high-level design and roadmap for the foundational systems required to run the MMORPG server and client.

## 1. Base Networking (✅ Done)
- **Technology:** Raw Asynchronous TCP Sockets (`System.Net.Sockets`).
- **Goal:** Establish a persistent, bi-directional, and non-blocking connection between the Unity Client and the .NET Server.
- **Components:** `GameServer` (Listener) and `ClientConnection` (Session Manager).

## 2. Messaging Protocol & Packet System (🚧 Next)
To handle thousands of messages scalably, we cannot send raw strings or heavy objects (like JSON or Dictionaries). We need a fast, low-allocation binary protocol.

- **Packet Structure:** Every message sent over the network will be a "Packet". A packet typically consists of:
  - **Length (2 bytes):** How big the packet is (so the receiver knows when it has read the whole thing).
  - **OpCode / Packet ID (2 bytes):** A unique `ushort` or `enum` identifying the type of message (e.g., `1 = LoginRequest`, `2 = MovePacket`).
  - **Payload (Variable):** The actual data (e.g., Username/Password strings, or X/Y/Z float coordinates).
- **Serialization Strategy:** We will use `BinaryReader` and `BinaryWriter` (or modern `.NET Span<T>` / `Memory<T>`) to pack and unpack data directly into bytes.
- **Routing:** A central `PacketHandler` dictionary will map `OpCodes` to specific functions (e.g., `OpCode.Login` triggers `HandleLogin()`), ensuring O(1) routing speed.

## 3. Login & Authentication
- **Handshake:** When a client connects, they send a `LoginRequest` packet with credentials.
- **Security:** Passwords must be hashed. If using tokens, a session token is generated.
- **Flow:** If valid, the server replies with a `LoginSuccess` packet containing the player's character data and spawns them into the world.

## 4. World State & Entity Management
- **Game Loop (Tick Rate):** The server needs a continuous loop (e.g., 20 or 30 ticks per second) to process physics, movement, and combat, rather than processing them instantly upon receiving a packet.
- **Entity System:** Players, monsters, and NPCs are all "Entities" with a unique ID.
- **Area of Interest (AoI):** To save bandwidth, the server only sends updates about entities that are *near* the player, not everyone in the entire world.

## 5. Data Persistence (Database)
- **Technology:** PostgreSQL or MySQL using Entity Framework Core or Dapper.
- **Goal:** Save player progression, inventory, and stats.
- **Async Db Calls:** Database queries must be strictly asynchronous so they don't pause the Game Loop while waiting for the hard drive.

## 6. Game Systems (Future)
- Combat & Stats Calculation (Server authoritative).
- Inventory & Item Management.
- Chat System (Global, Local, Whispers).
- Guilds & Parties.
