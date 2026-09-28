# AI Agent Guidelines

If you are an AI assistant or agent working on this codebase, please adhere to the following guidelines:

## Project Overview
- **Type**: MMORPG (Massively Multiplayer Online Role-Playing Game).
- **Tech Stack**: C# 12, .NET 8.
- **Client**: Unity (to be developed in the `Client/` directory).

## Architecture Rules
1. **Shared Logic**: Any code that needs to be understood by BOTH the `.NET Server` and the `Unity Client` (such as network packet definitions, game constants, enums, or math utilities) MUST be placed in the `Shared` class library.
2. **Server Logic**: The `Server` project is authoritative. It should contain all validation, database interactions, and state management. Never trust the client.
3. **Asynchronous Code**: The server must handle thousands of connections. Prefer `async/await` and thread-safe data structures (`ConcurrentDictionary`, `ConcurrentQueue`) over blocking calls or manual locks wherever possible.

## Networking & Packets
- The server uses raw TCP sockets (`System.Net.Sockets`). Do not use WebSockets unless specifically requested for a WebGL client build.
- **Packet Framing**: All network messages are framed with a 2-byte length header, followed by a 2-byte `OpCode` header, followed by the payload.
- **Packet Utility**: Always use the `Shared.Network.Packet` utility to read/write binary data to avoid string allocation overhead.
- **Routing**: Do not put business logic inside `PacketHandler.cs`. It is strictly a router. Business logic must be encapsulated in specific handler classes within the `Server/Handlers/` directory and registered in `PacketHandler.Initialize()`.

## Game Loop & World State
- The server runs an authoritative, fixed-timestep Game Loop at **30 Ticks Per Second** (`Server.World.GameLogic`).
- **Critical Rule**: Network handlers (like `AuthHandler` or `MovementHandler`) should generally just queue actions or update intention states. Actual physics, movement, and combat math MUST be processed synchronously inside the `GameLogic.Update()` loop to prevent race conditions and maintain a stable server heartbeat.

## Database & Authentication
- **ORM**: We use Entity Framework Core 8 with PostgreSQL (`Npgsql`).
- **Secrets**: The connection string is loaded securely via `DotNetEnv` from `Server/.env`. If you need a new secret, put it in `.env` and `.env.example`.
- **Security**: Never store passwords in plaintext. Always use `BCrypt.Net-Next` to hash and verify passwords in the database.
