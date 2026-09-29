# Unity Client Architecture

The Unity Client is the visual representation of the MMORPG. It is strictly a "dumb" client—it renders the world, accepts player inputs, and sends requests to the server. **It never trusts itself.**

## Directory Structure
- `Assets/Scripts/Network/`: Contains the `NetworkManager` and the packet routing system.
- `Assets/Scripts/UI/`: Contains scripts that bind to Unity Canvas elements (e.g., `SignInUI`, `CharacterSelectionUI`).
- `Assets/Scripts/Shared/`: **[SYMLINK]** This is not a real folder! It is a Windows Junction (or Unix Symlink) pointing directly to the backend `.NET` `Shared/` project. This guarantees 100% synchronization of network packets, opcodes, and math utilities between the client and server.

## Networking Lifecycle

### 1. The NetworkManager Singleton
The `NetworkManager.cs` is a persistent `MonoBehaviour` marked with `DontDestroyOnLoad()`. It spawns in the SignIn Scene and survives all scene transitions, keeping the TCP socket open to the server continuously.

### 2. Thread Safety
Because standard C# `System.Net.Sockets` operate on background threads, they **cannot** interact with Unity's Main Thread APIs (like `Instantiate()` or `UI.Text`).
To solve this safely:
1. The background thread receives the raw bytes and frames them into a complete packet.
2. The packet is pushed onto a thread-safe `ConcurrentQueue<byte[]>`.
3. During Unity's `Update()` loop (which runs on the Main Thread), the queue is dequeued.
4. The packet is passed to `PacketHandler.HandlePacket()`, allowing the handler to safely interact with Unity GameObjects.

### 3. Packet Routing
Incoming server messages are routed via `PacketHandler.cs`. For example, when the server sends an `OpCode.SignInResponse`, the `PacketHandler` looks up the registered route and invokes `Handlers.AuthHandler.HandleAuthResponse()`.

## Scene Flow
1. **SignInScene**: Initializes the `NetworkManager`, establishes the TCP connection, and presents the Sign In/Sign Up Canvas. Upon receiving a successful `AuthResponse`, the client loads the next scene.
2. **CharacterSelection**: (In Development) Requests the list of characters tied to the authenticated account and allows the player to spawn into the game world.
3. **WorldScene**: (Planned) The actual 2D/3D game environment where entities are spawned and controlled.
