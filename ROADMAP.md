# 🗺️ MMORPG Blueprint: Development Roadmap

> **Context & Reality Check ("From Vibecoding to Blueprint")**:  
> Early development was an intense, high-energy sprint driven by the excitement of rapidly prototyping a fully custom MMORPG. While this produced an incredible amount of working code across both the .NET 8 server and the Unity client, the sheer complexity of authoritative multiplayer architecture quickly revealed the limits of uncontrolled "vibecoding".  
> 
> Recognizing that high complexity requires discipline, we intentionally **paused, took a breath, deleted all 68 legacy exploratory issues, established architectural guidelines, and set strict anti-vibecoding guardrails (`AGENTS.md`)**. This roadmap is our official blueprint to guide the project forward with engineering rigor, clean contracts, and steady milestones.

---

## Milestone Status Overview

| Milestone | Focus Area | Status | Target Version |
| :--- | :--- | :---: | :---: |
| **Milestone 0** | Core Engine Foundation & Architecture | Completed | `v0.1.0-alpha` |
| **Milestone 1** | World Engine, Instances & Collisions | Near Completion | `v0.2.0-alpha` |
| **Milestone 2** | RPG Features & UI/UX | In Progress | `v0.3.0-alpha` |
| **Milestone 3** | Unity Client Polish | Planned | `v0.4.0-alpha` |
| **Milestone 4** | Server Polish & Architecture Hardening | Planned | `v0.5.0-alpha` |
| **Milestone 5** | Expanding MMORPG Features | Planned | `v0.6.0-beta` |
| **Milestone 6** | Production Hardening, Anti-Cheat & Scalability | Planned | `v1.0.0-rc` |

---

## Detailed Milestones

### Milestone 0: Core Engine Foundation (`v0.1.0-alpha`)
*The foundational building blocks of the authoritative multiplayer engine.*
- [x] **Raw TCP Socket Infrastructure**: High-performance binary stream parsing (`ClientConnection.cs`).
- [x] **Packet Framing & OpCodes**: 4-byte header framing (`[Length: 2B][OpCode: 2B]`) with zero-allocation `Packet` utility.
- [x] **Authoritative 30 TPS Game Loop**: Fixed-timestep heartbeat (`GameLogic.cs`) decoupled from I/O threads.
- [x] **Zero-Duplication Unity Integration**: Directory junction pipeline sharing `Shared/` natively with Unity without duplicate code files.
- [x] **Database & Auth Integration**: PostgreSQL + EF Core 8 with BCrypt password hashing and code-first migrations.
- [x] **Area of Interest (AoI)**: Distance-based entity culling to limit network throughput.
- [x] **Multi-Channel Chat System**: Local (AoI culling), Global broadcast, and Whisper channels with profanity filtering and optional DB logging.

> *Plus emerging features, architectural refinements, and quality-of-life improvements identified during active development sprints.*

---

### Milestone 1: World Engine & Spatial Partitioning (`v0.2.0-alpha`)
*Multi-zone routing, collision math, map extraction, and world data pipelines.*
- [x] **Multi-Map Routing**: `MapManager` and `MapInstance` zones routing players and entities via `MapId`.
- [x] **Hybrid Server Collision System**: Server-authoritative geometry checks (`BoxCollider`, `CircleCollider`, `PolygonCollider`).
- [x] **Bind Locations & Inns**: Innkeeper interaction (`BindPoint` NPC interaction, range-validated, answered by `EntityInteractResponse`), persistent character bind coordinates, and respawn flow (`BindHandler`, `InteractHandler`). *(The Innkeeper has no spawner until its client prefab exists, tracked in #166)*
- [x] **Unity Map Exporter Tool**: Editor tool (`MapExporter.cs`) exporting `CompositeCollider2D` polygons directly to server JSON. *(Verified: Completed)*
- [x] **Static JSON Data Pipeline**: Centralized `DataManager` loading JSON templates for Items, Recipes, Mobs, Spawners, and Loot Tables. *(Verified: Completed)*
- [x] **Multi-Map Spawner Routing**: `SpawnerTemplate.MapId` routes each spawner to its map (defaults to Map 1 for legacy data; unknown maps are skipped with a log line). *(Server side completed; the Unity spawner exporter still needs to write `MapId`, tracked in #187)*
- [ ] **Dynamic Timed Wave Controllers**: Time-driven spawner waves on top of the per-map spawner routing.
- [ ] **Spatial Grid / Quadtree Partitioning**: Implement a 2D spatial grid within `MapInstance` to replace $O(N)$ linear scans for high-density maps (1,000+ entities per map). *(Missing / Next up)*

> *Plus emerging features, architectural refinements, and quality-of-life improvements identified during active development sprints.*

---

### Milestone 2: RPG Features & UI/UX (`v0.3.0-alpha`)
*Authoritative combat mechanics, character progression, inventory pipelines, and interactive UI.*
- [x] **Experience Curve & Leveling**: Configurable exponential EXP curves (`EXP_BASE * Level^EXP_GROWTH_RATE`), mob EXP yields, level-up broadcasts, and unspent stat point allocation (`ProgressionHandler`, `ExpBarUI`).
- [x] **Authoritative 3-Pillar Combat Math**: Weapon reach validation, swing cooldowns, hit/dodge/crit rolls, and armor mitigation calculations.
- [x] **World Loot Satchels & Harvesting**: Dropped loot bags with 30s killer ownership protection and 120s world decay timers (`LootSatchel.cs`), plus depletable resource nodes with respawn timers, spawned from `ResourceNodes.json` / `ResourceSpawns.json` with data-driven drops.
- [x] **Backend Inventory & Paperdoll Equipment**: Full server-side inventory slots, stack splitting, equipment stat aggregation, item refinement, and recipe crafting.
- [ ] **Client Paperdoll & Inventory UI**: Drag-and-drop inventory bag grid, equipment slot panel, and item tooltip displays.
- [ ] **Client Ground Loot & Harvesting UI**: Interactive loot popup window displaying satchel contents with "Loot" and "Loot All" buttons.
- [ ] **Spell & Skill Casting Pipeline**: Cast times, cooldown trackers, channel interrupts, and mana consumption.
- [ ] **Status Effects & Auras**: Buffs, Debuffs, Damage-over-Time (DoT), Heal-over-Time (HoT), and crowd control (stuns, slows).
- [ ] **Gold & NPC Vendor Economy**: Gold currency drops, vendor buy/sell trade dialogues, and repair costs.

> *Plus emerging features, architectural refinements, and quality-of-life improvements identified during active development sprints.*

---

### Milestone 3: Unity Client Polish (`v0.4.0-alpha`)
*Enhancing the player experience with smooth rendering, audio, overhead nametags, and visual feedback.*
- [x] **Connection & Session Dispatcher**: Robust client socket handling in `NetworkManager.cs` and `PacketHandler.cs`.
- [x] **Lobby & Character Management Screens**: Complete Sign-In, Sign-Up, Character Creation, and Character Selection interfaces.
- [x] **Authentication Scene**: `AuthenticationScene` / `AuthenticationUI` with separate Sign In and Sign Up panels (email and password confirmation on sign-up), client-side pre-validation with the shared `InputRules`, a pending state with response timeout and Enter-to-submit. Sign-up now requires a unique email (protocol `0.2.1-alpha`).
- [x] **Character Selection Scene**: `CharacterSelectionScene` with code-wired buttons, a pending state with response timeout, creation-name pre-validation with the shared `InputRules`, an empty-list hint and visible delete/select failures. No delete confirmation or appearance picker yet.
- [x] **Loading Screen & Scene Transition Masking**: Asynchronous scene loading progress bar overlay with smooth fade-out (`LoadingScreenUI.cs`, `GameManager.cs`). *(Verified: Completed)*
- [x] **Remote Entity Interpolation**: Smooth position smoothing and direction tracking across network ticks via `Vector3.Lerp` (`NetworkEntity.cs`). *(Verified: Completed)*
- [x] **Floating Combat Text**: Real-time visual popups for damage numbers, critical hits, and dodge notifications (`FloatingText.cs`, `EntityManager.cs`). *(Verified: Completed)*
- [ ] **Overhead Health Bar Nametags**: Attach floating overhead health and mana bars to remote players and monsters.
- [ ] **Client Movement Prediction & Authoritative Reconciliation**: Local prediction with server correction rollback buffer to eliminate movement stutter on high-latency connections.
- [ ] **Audio Manager**: Sound effects for weapon swings, impacts, level-up fanfares, UI clicks, and ambient zone music.
- [ ] **Visual Polish & Camera Boundaries**: Sprite sorting layer order enforcement, level-up particle VFX, and camera clamp boundaries.

> *Plus emerging features, architectural refinements, and quality-of-life improvements identified during active development sprints.*

---

### Milestone 4: Server Polish & Architecture Hardening (`v0.5.0-alpha`)
*Optimizing server performance, garbage collection, and state synchronization.*
- [x] **Single-Threaded World Mutation (Command Queue)**: Network threads only frame and enqueue; the 30 TPS game loop drains a per-client-capped, time-budgeted `GameCommandQueue` (see `docs/server-threading.md`).
- [x] **Resilient Fixed-Timestep Game Loop**: Exception-guarded ticks, catch-up with a step cap, and lag reporting (`FixedTimestepClock`, see `docs/server-threading.md`).
- [x] **Network I/O Hardening**: Serialized `Send` with a write timeout, inbound frame length validation (min header / max cap) and partial-packet / pre-sign-in read timeouts (`ClientConnection`, see `docs/server-threading.md`).
- [x] **Input Validation**: Shared `InputRules` (username/password/character-name format and length, BCrypt 72-byte cap, appearance range) enforced by the auth and character handlers; unique-index races on sign-up/creation answer "already taken" instead of dropping the client.
- [x] **Static Data Validation (fail fast)**: `DataManager` loads all-or-nothing and `DataValidator` reports every duplicate id, bad value and dangling item/NPC reference at startup (see `docs/static-data.md`).
- [x] **Abuse Protection**: Per-address sign-in/sign-up failure lockout (`FailureThrottle`), plus chat length cap and per-player token-bucket rate limit (see `docs/server-threading.md`). TLS stays in its own RFC.
- [x] **Atomic, Data-Driven Character Creation**: Starting vitals, stats, gold, spawn and starter kit come from `CharacterCreation.json` and are stored together in one save (see `docs/static-data.md`).
- [x] **Fail-Fast Map Loading**: Maps load from the output folder (works for publish/Docker), any missing or malformed map stops startup with a full error list, and spawners / the start point must reference loaded maps (see `docs/static-data.md`).
- [ ] **Zero-Allocation Packet Pooling**: Implement `ArrayPool<byte>` socket buffer pooling to eliminate GC pressure during heavy network traffic.
- [ ] **State Delta Compression**: Pack entity position updates and omit unchanged fields to minimize bandwidth.
- [x] **Graceful Disconnect & Combat Logging Protection**: A disconnect while tagged in combat leaves the character in the world, defenseless, for a linger period; a reconnect resumes it (`Player.BeginLinger`, see `docs/server-threading.md`).
- [x] **Database Write-Behind Caching**: Snapshot-based, per-character ordered, coalesced and retried persistence queue with autosave and flush on shutdown (`PersistenceService`, see `docs/persistence.md`).

> *Plus emerging features, architectural refinements, and quality-of-life improvements identified during active development sprints.*

---

### Milestone 5: Expanding MMORPG Features (`v0.6.0-beta`)
*Multiplayer social structures and cooperative game content.*
- [ ] **Party / Group System**: Group formation (up to 5 players), shared combat experience distribution, and party health HUD frames.
- [ ] **Player-to-Player Secure Trading**: Two-way trade window with simultaneous "Lock" and "Confirm" verification steps.
- [ ] **Guilds & Clans**: Guild creation, rank hierarchies, shared guild bank vaults, and guild chat channels.
- [ ] **Social Lists**: Friends list, online status notifications, and player block/ignore lists.
- [ ] **Quest & Mission Engine**: Static quest definitions (kill, collect, deliver), NPC dialogue windows, and reward distribution.
- [ ] **Instanced Dungeons**: Private dungeon instances with dedicated boss state machines and loot chests.

> *Plus emerging features, architectural refinements, and quality-of-life improvements identified during active development sprints.*

---

### Milestone 6: Polish for Production & Scalability (`v1.0.0-rc`)
*Production readiness, multi-node clustering, and deployment automation.*
- [ ] **Multi-Node Cluster Architecture**: Separate Gateway/Login servers, World Nodes, and Database proxy clusters.
- [ ] **Stress-Testing Simulation Suite**: Automated headless bot clients simulating 1,000–5,000 concurrent players.
- [ ] **Comprehensive Anti-Cheat Hardening**: Speed-hack rubberbanding, wall-clip collision raycasting, and action frequency rate limiting.
- [ ] **Containerized Production Orchestration**: Docker Compose and Kubernetes manifests with automated health-check probes.
- [ ] **Continuous Integration (CI/CD)**: GitHub Actions workflow building solution and running all 53+ unit tests on every pull request.

> *Plus emerging features, architectural refinements, and quality-of-life improvements identified during active development sprints.*
