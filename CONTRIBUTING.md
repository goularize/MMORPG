# Contributing to the MMORPG

Thank you for your interest in contributing! This project is being crafted as an **open-source architectural guideline and reference implementation** for modern, authoritative 2D/3D MMORPGs built with **.NET 8** and **Unity**.

Whether you are fixing a bug, improving documentation, designing a new combat mechanic, or optimizing packet serialization, we welcome your contributions.

---

## Core Architectural Principles

Before writing code, please familiarize yourself with the non-negotiable architectural foundations of this project:

1. **Authoritative Server Model (Never Trust the Client)**:
   - The server is the single source of truth for all game state, positions, health, stats, inventory, combat calculations, and physics boundaries.
   - The client only sends **player intentions** (e.g., target selection, movement direction input, attack request). The server validates and executes them.

2. **Fixed-Timestep Authoritative Game Loop (30 TPS)**:
   - Network packet handlers (`Server/Handlers/`) **never** execute gameplay logic, combat math, or movement directly on the network thread.
   - Network handlers queue requests or update intention flags on entities.
   - All spatial movement, vitals regeneration, AI behaviors, and combat interactions are executed synchronously inside `GameLogic.Update()` at a steady 30 ticks per second.

3. **Zero-Duplication Shared Assembly**:
   - Network packet definitions, binary serializers, game constants, enums, and shared mathematical models live exclusively in `Shared/`.
   - The Unity client natively links to `Shared/` via OS-level directory junctions (`setup_client.sh` / `setup_client.bat`).
   - **Never duplicate `.cs` files between the client and server.**

4. **Zero-Allocation Binary Packet Framing**:
   - Packets are framed with: `[2-byte Length][2-byte OpCode][Payload bytes]`.
   - Utilize `Shared.Network.Packet` for reading and writing primitive binary buffers rather than text, JSON, or reflection-based serializers.

---

## Development Setup

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker & Docker Compose](https://www.docker.com/) (for PostgreSQL database)
- [Unity Hub & Unity Editor](https://unity.com/) (compatible with Unity 2022.3 LTS or Unity 6)

### First-Time Workspace Setup
1. **Clone the repository**:
   ```bash
   git clone https://github.com/goularize/MMORPG.git
   cd MMORPG
   ```

2. **Link the Shared library to the Unity Client**:
   - **Linux / macOS**:
     ```bash
     chmod +x setup_client.sh
     ./setup_client.sh
     ```
   - **Windows**:
     ```cmd
     setup_client.bat
     ```

3. **Configure Environment Secrets**:
   ```bash
   cp Server/.env.example Server/.env
   ```

4. **Start the Database**:
   ```bash
   docker-compose up -d
   ```

5. **Build and Test the Server**:
   ```bash
   dotnet build MMORPG.sln
   dotnet test Tests/Server.Tests/Server.Tests.csproj
   ```

---

## Branching & Git Workflow

- **`main`**: Production-ready, stable codebase. Direct pushes to `main` are restricted.
- **`feature/<short-desc>`**: For new gameplay mechanics, network systems, or client features.
- **`fix/<short-desc>`**: For bug fixes, race condition patches, or regression fixes.
- **`docs/<short-desc>`**: For documentation, diagrams, and architectural RFCs.

### Conventional Commits
All commit messages must follow the [Conventional Commits](https://www.conventionalcommits.org/) specification (`<type>(<scope>): <description>`):

#### Commit Types
- `feat:` A new feature or gameplay mechanic
- `fix:` A bug fix or race condition patch
- `docs:` Documentation updates, diagrams, or architecture guides
- `refactor:` Code change that neither fixes a bug nor adds a feature
- `perf:` Performance optimization or allocation reduction
- `test:` Adding or updating automated tests
- `chore:` Maintenance tasks, dependency updates, tooling

#### Scopes
Always declare the affected tier in the scope `(<tier>)` or hierarchical subsystem `(<tier>/<subsystem>)`:
- `server` (e.g., `server/combat`, `server/ai`, `server/db`, `server/aoi`)
- `client` (e.g., `client/ui`, `client/network`, `client/rendering`, `client/movement`)
- `shared` (e.g., `shared/network`, `shared/models`, `shared/math`)
- `docs`, `infra`, `tooling`

#### Real-World Examples:
* `feat(server/combat): implement critical strike roll calculation`
* `fix(server/aoi): resolve off-by-one entity despawn boundary`
* `feat(client/ui): add loading screen with async scene progress bar`
* `fix(client/movement): eliminate interpolation jitter during rotation`
* `refactor(shared/network): pool binary packet byte buffers`
* `docs(readme): add 7-tier key features matrix`

> **Best Practice Note**: While Git commits must use standard parenthetical scopes (`feat(server): ...`) to remain fully compatible with automated semantic changelog tools (`release-please`, `commitlint`), bracket tags like `[Server]`, `[Client]`, and `[Shared]` are encouraged for **GitHub Issue and Pull Request titles** for fast visual triage (e.g., `[Client] Loading Screen & Scene Transition UI`).

---

## Coding Guidelines & Best Practices

### C# 12 & .NET 8 Standards
- Enable nullable reference types (`<Nullable>enable</Nullable>`).
- Use file-scoped namespaces (`namespace Server.Handlers;`).
- Prefer pattern matching, records for immutable contracts, and target-typed `new()`.
- Avoid blocking calls (`.Result`, `.Wait()`) on asynchronous tasks; use `async/await` properly in networking and I/O pipelines.
- In high-frequency game loop code, minimize heap allocations: prefer `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, and object pooling over allocating new objects on each tick.

### Handlers & Packet Routing
- Do **not** place business logic inside `PacketHandler.cs`. It is strictly a dispatcher/router.
- Handlers in `Server/Handlers/` must inherit clean separation of concerns and register their OpCodes in `Initialize()`.

---

## Submitting a Pull Request

1. Ensure your code builds with zero errors or warnings (`dotnet build MMORPG.sln`).
2. Run existing automated tests and add new tests covering your changes (`dotnet test`).
3. Fill out the **Pull Request Template** completely.
4. Link the relevant issue or RFC.
5. PRs require review and approval before merging.
