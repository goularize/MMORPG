# MMORPG Base Architecture Design

This document outlines the high-level design and roadmap for the foundational systems required to run the MMORPG server and client.

## 1. Base Networking (✅ Done)
- **Technology:** Raw Asynchronous TCP Sockets (`System.Net.Sockets`).
- **Goal:** Establish a persistent, bi-directional, and non-blocking connection between the Unity Client and the .NET Server.
- **Components:** `GameServer` (Listener) and `ClientConnection` (Session Manager).

## 2. Messaging Protocol & Packet System (✅ Done)
To handle thousands of messages scalably, we cannot send raw strings or heavy objects (like JSON or Dictionaries). We need a fast, low-allocation binary protocol.

- **Packet Structure:** Every message sent over the network will be a "Packet". A packet typically consists of:
  - **Length (2 bytes):** How big the packet is (so the receiver knows when it has read the whole thing).
  - **OpCode / Packet ID (2 bytes):** A unique `ushort` or `enum` identifying the type of message (e.g., `1 = SignInRequest`, `2 = MovePacket`).
  - **Payload (Variable):** The actual data (e.g., Username/Password strings, or X/Y/Z float coordinates).
- **Serialization Strategy:** We will use `BinaryReader` and `BinaryWriter` (or modern `.NET Span<T>` / `Memory<T>`) to pack and unpack data directly into bytes.
- **Routing:** A central `PacketHandler` dictionary will map `OpCodes` to specific functions (e.g., `OpCode.SignIn` triggers `HandleSignIn()`), ensuring O(1) routing speed.

## 3. Server Roles & Infrastructure (Hybrid Topology)
Instead of a single monolith, the backend is split into specialized services to allow for horizontal scalability.

### A. SignIn & Authentication Server (✅ Done)
- **Role:** A lightweight gateway server.
- **Mechanics:** When a client connects, they send a `SignInRequest`. Passwords are cryptographically hashed using BCrypt. If valid, the server logs the user in. Registration (`SignUpRequest`) is also fully supported.

### B. World Server(s)
- **Role:** Heavyweight authoritative servers running the actual game. Can be deployed as a classic Realm list or distributed map layers (e.g., City Server, Forest Server).
- **Game Loop & Tick Rate (✅ Done):** Runs a continuous, fixed-timestep loop (30 ticks per second) to process physics, movement, and combat independently of network events.
- **Entity System & AoI (✅ Done):** Players and NPCs are "Entities" managed by an `EntityManager`. The server calculates Euclidean distance to determine the Area of Interest (AoI) and dynamically broadcasts `EntitySpawn`, `EntityDespawn`, and `EntityPositionUpdate` packets to clients to save bandwidth.

### C. Database Cluster (✅ Done)
- **Role:** Persistent storage using PostgreSQL.
- **Mechanics:** We use **Entity Framework Core** with the `Npgsql` provider. The database connection string is securely loaded from a `.env` file via `DotNetEnv`. Passwords are NEVER stored in plaintext (BCrypt is used).

## 4. Game Systems (Future)
- Combat & Stats Calculation (Server authoritative).
- Inventory & Item Management.
- Chat System (Global, Local, Whispers).
- Guilds & Parties.
