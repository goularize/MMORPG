# MMORPG Project

A cross-platform MMORPG project built with C# and .NET 8.

## Architecture

This repository contains the backend and shared logic for the game. The Unity client will be developed in the future and placed in the `Client/` directory.

- **`Server/`**: A .NET Console Application acting as the authoritative game server.
- **`Shared/`**: A .NET Class Library containing shared code (models, network packets, enums) used by both the Server and the Unity Client.
- **`Client/`**: The Unity Game Client natively linked to the Shared backend logic.

## Features Currently Implemented

* **Live Unity Client**: Natively compiling the `.NET 8` Shared logic via OS-level directory junctions for 0-duplication networking.
* **Secure Authentication**: End-to-end Registration and Sign-in backed by PostgreSQL and BCrypt password hashing.
* **Database Integration**: Entity Framework Core 8 with Code-First Migrations and DotNetEnv integration.
* **Custom Binary Protocol**: Lightweight TCP packet serialization/deserialization for high-throughput networking.
* **Authoritative Game Loop**: Fixed-timestep 30 TPS synchronous update loop to prevent race conditions.
* **Entity System**: Complete OOP hierarchy for Players and NPCs managed by an RAM-based EntityManager.
* **Area of Interest (AoI)**: Distance-based entity culling to limit bandwidth usage and dynamically trigger Spawn/Despawn packets for clients.

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/) (For local database hosting)

### Running the Database
1. Copy the environment variables: `cp Server/.env.example Server/.env`
2. Start the PostgreSQL container:
```bash
docker-compose up -d
```

### Running the Server
```bash
cd Server
dotnet run
```

### Running the Unity Client
Because the Client and Server share the exact same networking and math code, we use directory symlinks to avoid duplicating files. Before opening the Unity project for the first time, run the setup script for your OS from the root of the repository:

**Windows:**
Double-click `setup_client.bat` or run:
```cmd
.\setup_client.bat
```

**Mac / Linux:**
```bash
chmod +x setup_client.sh
./setup_client.sh
```
After running the script, open the `Client/` folder using Unity Hub.

## Documentation

- [Base Architecture](docs/design/base_architecture.md)
- [Network Architecture](docs/server/network.md)
- [Entity System](docs/server/entity_system.md)
- [Database Setup](docs/server/database.md)
