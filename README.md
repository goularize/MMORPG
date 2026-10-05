# ⚔️ MMORPG

[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black.svg)](https://unity.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue.svg)](https://www.postgresql.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

An open-source, production-oriented reference architecture and development blueprint for building authoritative Massively Multiplayer Online Role-Playing Games (MMORPGs) using **C# 12 / .NET 8** and **Unity**.

---

## 🏛️ Architecture Overview

The repository is organized into three core tiers designed for zero-duplication networking:

- **[`Server/`](Server/)**: Authoritative .NET 8 Console Application running a fixed-timestep 30 TPS synchronous game loop, handling database persistence, combat validation, spatial partitioning, and client sessions.
- **[`Shared/`](Shared/)**: .NET 8 Class Library containing binary packet encoders/decoders, OpCodes, game models, combat formulas, and math utilities.
- **[`Client/`](Client/)**: Unity game client natively linked to `Shared/` via OS directory junctions, guaranteeing protocol synchronization with zero manual code duplication.

---

## ✨ Key Features Implemented

The MMORPG Blueprint is an end-to-end reference implementation covering the full spectrum of multiplayer game architecture:

### 🏛️ Authoritative Core & Networking
* **Fixed-Tick Authoritative Game Loop**: 30 Ticks Per Second (`GameLogic.cs`) synchronous heartbeat isolating game simulation from asynchronous socket threads.
* **High-Throughput Binary Protocol**: Custom 4-byte header framing (`[Length: 2B][OpCode: 2B]`) with zero-allocation binary reader/writer (`Packet.cs`).
* **Zero-Duplication Unity Integration**: OS-level directory junctions (`setup_client.sh` / `setup_client.bat`) seamlessly compiling backend `Shared/` contracts directly inside Unity.
* **Anti-Cheat Speed & Movement Validation**: Authoritative distance verification per tick with automated client rubberbanding on speed-hack detection.

### 🗺️ World Engine, Spatials & Collisions
* **Multi-Map Routing & Instances**: Centralized `MapManager` and isolated `MapInstance` zones routing players and entities via `MapId`.
* **Area of Interest (AoI) Culling**: Distance-based spatial observer management dynamically broadcasting entity spawn, despawn, and state deltas.
* **Hybrid Collision System**: Server-authoritative 2D/3D collision checks with `BoxCollider`, `CircleCollider`, and `PolygonCollider` support.
* **Static JSON Data Pipeline**: Centralized `DataManager` loading JSON templates for Items, Recipes, Mobs, Spawners, and Loot Tables.

### ⚔️ RPG Combat, Progression & Mob AI
* **Authoritative Combat Pipeline**: Attack range verification, weapon reach validation, swing cooldowns, and hit/dodge/crit resolution.
* **Primary & Derived Attributes**: STR, AGI, INT, and STA directly driving physical damage, attack speed, evasion, maximum mana, and armor mitigation.
* **Formula-Driven Progression**: Configurable exponential EXP curves, monster kill EXP yield, level-up broadcasts, and unspent stat point distribution.
* **Advanced Mob AI State Machine**: Passive, Neutral, Aggressive, and Pack-Alert behaviors (calling nearby packmates into combat), with leashing and respawn loops.
* **Death & Bind Point Respawn Flow**: Innkeeper interaction, persistent character bind coordinates, and client death/revival state machines.

### 🎒 Inventory, Equipment, Crafting & Loot
* **Paperdoll Equipment Architecture**: Dedicated equipment slots (Head, Chest, MainHand, OffHand, etc.) with real-time stat aggregation.
* **Grid Backpack Inventory**: Full support for moving items, splitting stacks, using consumables, and dropping items into the world.
* **Item Refinement & Upgrades**: Multi-tier item upgrading mechanics with progressive stat improvements.
* **Recipe Learning & Crafting**: Crafting system consuming material requirements and registering learned character recipes.
* **Interactive World Loot Satchels**: Dropped ground loot bags with 30s killer ownership protection and 120s (configurable) world decay timers.
* **Resource Node Harvesting**: World gathering nodes (e.g., Oak Trees, Mining veins) with harvest charges, depleted states, and respawn cycles.

### 💬 Chat, Lobby & Account Security
* **Multi-Channel Chat**: Spatial Local chat (AoI filtered), Global announcements, and direct private Whispers.
* **Chat Moderation & Auditing**: Runtime profanity filtering and configurable PostgreSQL chat logging for audit trails.
* **Full Account & Character Lifecycle**: End-to-end BCrypt registration/sign-in, character slot limits, character creation, selection, and deletion.

### 🎮 Unity Client & Editor Tooling
* **Comprehensive UI Suite**: Sign-In / Sign-Up, Character Creation, Character Selection, Loading Screen overlay (masking async scene loading), Player HUD, and Experience Bar.
* **Floating Combat Text**: Real-time visual feedback for damage numbers, critical strikes, and dodges/misses.
* **Remote Entity Interpolation**: Smooth position smoothing and rotation tracking across network ticks.
* **Custom Editor Tools**: Unity Map Exporter tool, Pixel Art asset importer setup, and automated Enemy Prefab generator.

### 🧪 Automated Testing Suite
* **Comprehensive Test Coverage**: 53+ automated unit and integration tests (`Tests/Server.Tests/`) covering database persistence, combat formulas, mob AI, chat channels, loot tables, and inventory actions.

---

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker & Docker Compose](https://www.docker.com/) (for PostgreSQL database)
- [Unity Hub & Unity 2022.3 LTS or newer](https://unity.com/)

### 1. Running the Database
```bash
cp Server/.env.example Server/.env
docker-compose up -d
```

### 2. Running the Server
```bash
cd Server
dotnet run
```

### 3. Setting Up the Unity Client
Run the setup script once to link the shared backend logic into Unity's Asset hierarchy:

- **Linux / macOS**:
  ```bash
  chmod +x setup_client.sh
  ./setup_client.sh
  ```
- **Windows**:
  ```cmd
  setup_client.bat
  ```

Open the `Client/` folder in Unity Hub and press Play!

---

## 🗺️ Roadmap & Project Management

Check out our [Development Roadmap](ROADMAP.md) to see completed milestones and upcoming systems:
- World Engine, Map Exporting & Spatial Grid Partitioning
- Skill/Spell Systems & Status Effects (Buffs/Debuffs)
- Social Infrastructure (Parties, Guilds, Player Trading)
- Multi-node Distributed Clustering & Anti-Cheat Hardening

---

## 🤝 Contributing

We welcome contributions from the community! Please read our [Contributing Guidelines](CONTRIBUTING.md) and [Code of Conduct](CODE_OF_CONDUCT.md) before submitting pull requests.

For AI assistants and agents, please refer to [AGENTS.md](AGENTS.md).

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
