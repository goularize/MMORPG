# AI Agent Guidelines

If you are an AI assistant or agent working on this codebase, please adhere to the following guidelines and consult [CONTRIBUTING.md](CONTRIBUTING.md) for overall project standards, workflows, and Conventional Commit conventions.

## Project Overview
- **Type**: MMORPG (Massively Multiplayer Online Role-Playing Game) Open-Source Blueprint.
- **Tech Stack**: C# 12, .NET 8.
- **Client**: Unity (developed in the `Client/` directory).

## Git & Version Control Rules
1. **NEVER COMMIT WITHOUT PERMISSION**: Do not use `git add` or `git commit` unless the user explicitly tells you to commit the current changes.
2. **NEVER PUSH WITHOUT PERMISSION**: Being asked to commit is **NOT** permission to push. Do not use `git push` unless the user explicitly types the words instructing you to push to the remote repository.
3. **Conventional Commits & Scopes**: When authorized to commit, always strictly follow the Conventional Commits specification and tier scopes outlined in [CONTRIBUTING.md](CONTRIBUTING.md) (e.g., `feat(server/combat): ...`, `fix(client/ui): ...`).

## Architecture Rules
1. **Shared Logic**: Any code that needs to be understood by BOTH the `.NET Server` and the `Unity Client` (such as network packet definitions, game constants, enums, or math utilities) MUST be placed in the `Shared` class library.
2. **Client-Server Link**: The Unity Client shares the backend `Shared/` library natively via OS-level directory junctions inside `Client/Assets/Scripts/Shared/`. **Never duplicate `.cs` files between the client and server.**
3. **Server Logic**: The `Server` project is authoritative. It should contain all validation, database interactions, and state management. Never trust the client.
4. **Asynchronous Code**: The server must handle thousands of connections. Prefer `async/await` and thread-safe data structures (`ConcurrentDictionary`, `ConcurrentQueue`) over blocking calls or manual locks wherever possible.

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

## Anti-"Vibecoding" Engineering Guardrails
1. **No Phantom Code**: Never add packets, models, or systems without their corresponding end-to-end integration and server loop validation.
2. **Automated Test Coverage**: Every new system, formula, or packet serializer must have accompanying tests in `Tests/Server.Tests/`.
3. **Continuous Verification**: Always execute `dotnet build MMORPG.sln` and run tests to guarantee that the solution compiles and passes cleanly before concluding any task.
4. **Living Documentation & Project Tracking**: Technical implementation must strictly follow `ROADMAP.md`. Features and tasks must be tracked with granular, detailed GitHub Issues that are always linked to the GitHub Project **"MMORPG"**. Whenever introducing, modifying, or refactoring a subsystem, update the corresponding technical document in `docs/` and synchronize milestone progress in `ROADMAP.md`.
5. **Standards Compliance**: All architectural implementations, code conventions, and pull request submissions must adhere to the community standards in [CONTRIBUTING.md](CONTRIBUTING.md).
