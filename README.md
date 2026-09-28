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

### Running the Server
```bash
cd Server
dotnet run
```

## Documentation

- [Network Architecture](docs/server/network.md)
