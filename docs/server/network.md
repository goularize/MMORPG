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
- **Data Reading & Framing:** Wraps a `TcpClient` and its `NetworkStream`. It continuously reads incoming byte arrays into a buffer (`ReceiveAsync`). It performs **Packet Framing** by reading the 2-byte length header of the incoming stream, extracting complete packets, and forwarding them to the `PacketHandler`.
- **Data Writing:** Provides a `Send(Packet packet)` method to convert packets back into raw bytes and fire them over the network to the client.
- **Disconnection:** Automatically detects when a client drops (when `ReadAsync` returns 0 bytes) or throws an exception, and cleans up the stream.
