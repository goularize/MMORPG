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

## 3. Server Roles & Infrastructure (Hybrid Topology)
Instead of a single monolith, the backend is split into specialized services to allow for horizontal scalability.

### A. Login & Authentication Server
- **Role:** A lightweight gateway server.
- **Mechanics:** When a client connects, they send a `LoginRequest`. Passwords are cryptographically hashed and checked. If valid, the server generates a secure session token, provides the client with a Server List (or Gateway address), and hands them off.

### B. World Server(s)
- **Role:** Heavyweight authoritative servers running the actual game. Can be deployed as a classic Realm list or distributed map layers (e.g., City Server, Forest Server).
- **Game Loop (Tick Rate):** Runs a continuous loop (e.g., 30 ticks per second) to process physics, movement, and combat.
- **Entity System & AoI:** Players and NPCs are "Entities". To save bandwidth, the server uses Area of Interest (AoI) to only send updates about entities physically *near* the player.

### C. Database Cluster
- **Role:** Persistent storage using PostgreSQL or MySQL (via Entity Framework Core or Dapper).
- **Mechanics:** Accessed by the Login Server (for accounts) and World Servers (for character saving/loading). All queries are strictly asynchronous to avoid pausing the game loops.

## 4. Game Systems (Future)
- Combat & Stats Calculation (Server authoritative).
- Inventory & Item Management.
- Chat System (Global, Local, Whispers).
- Guilds & Parties.
