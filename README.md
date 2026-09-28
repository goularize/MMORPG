# MMORPG Project

A cross-platform MMORPG project built with C# and .NET 8.

## Architecture

This repository contains the backend and shared logic for the game. The Unity client will be developed in the future and placed in the `Client/` directory.

- **`Server/`**: A .NET Console Application acting as the authoritative game server.
- **`Shared/`**: A .NET Class Library containing shared code (models, network packets, enums) used by both the Server and the Unity Client.
- **`Client/`**: Placeholder for the future Unity project.

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

## Documentation

- [Base Architecture](docs/design/base_architecture.md)
- [Network Architecture](docs/server/network.md)
- [Entity System](docs/server/entity_system.md)
- [Database Setup](docs/server/database.md)
