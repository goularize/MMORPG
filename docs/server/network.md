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

## Packet Pipeline (How Data Flows)

To keep the server scalable and the code organized, data follows a strict 3-step pipeline when it arrives at the server:

**`ClientConnection` ➔ `PacketHandler` ➔ `Specific Handler (e.g. AuthHandler)`**

1. **The Receiver (`ClientConnection`)**: 
   - A player's client sends a stream of bytes.
   - The `ClientConnection` buffers these bytes. When it realizes it has received a full packet (by checking the length header), it slices that exact byte array out and passes it up the chain.
   - *It does not know or care what the data means.*
   
2. **The Router (`PacketHandler`)**: 
   - Receives the raw, complete packet byte array from `ClientConnection`.
   - It reads the next 2 bytes to figure out the `OpCode` (e.g., "Ah, this is a LoginRequest (ID: 1)").
   - It looks at its internal Dictionary, finds the function mapped to ID 1, and fires it.
   - *It acts purely as a traffic cop, routing data in O(1) time without processing game logic.*

3. **The Logic Handler (`Server/Handlers/*.cs`)**: 
   - A specialized class (like `AuthHandler.cs` or `MovementHandler.cs`) receives the packet.
   - It extracts the specific payload (e.g., Username and Password).
   - It executes the actual game logic (e.g., validating the password).
   - It uses the `ClientConnection` reference it was passed to send a response packet directly back to the player.
