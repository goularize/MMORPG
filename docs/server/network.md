# Server Network Architecture

The MMORPG server uses a custom, asynchronous raw TCP socket architecture to handle real-time communication with game clients.

## Core Components

The networking logic is located in the `Server` project and revolves around two main classes:

### 1. `GameServer`
- **Location:** `Server/GameServer.cs`
- **Port:** `7777`
- **Role:** The `GameServer` is responsible for initializing a `TcpListener` on port 7777 and constantly listening for incoming connection requests.
- **Concurrency:** It uses an asynchronous loop (`AcceptClientsAsync`) to accept new connections without blocking the main thread. Connected players are stored in a thread-safe `ConcurrentDictionary` and assigned a unique Client ID.

### 2. `ClientConnection`
- **Location:** `Server/ClientConnection.cs`
- **Role:** Represents a single connected player (client). 
- **Data Reading:** Wraps a `TcpClient` and its `NetworkStream`. It continuously reads incoming byte arrays into a buffer (`ReceiveAsync`) as long as the client is connected. 
- **Disconnection:** It automatically detects when a client drops (when `ReadAsync` returns 0 bytes) or throws an exception, and cleans up the stream.

## Future Plans (Packets)
Currently, the server only reads raw byte arrays. In the future, these bytes will be deserialized into structured **Packets** (defined in the `Shared` project) using a `PacketParser` so the server can understand specific game actions (e.g., Login, Move, Attack).
