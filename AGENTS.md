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

## Networking
- The server uses raw TCP sockets (`System.Net.Sockets`).
- Do not use WebSockets unless specifically requested for a WebGL client build.
- Prioritize low-allocation code in hot paths (like the network receive loop) to avoid Garbage Collection spikes. (e.g., use `Span<T>`, `Memory<T>`, or `ArrayPool<T>` when parsing packets).
