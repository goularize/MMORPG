# Messaging Protocol

The MMORPG uses a custom binary protocol to send data back and forth between the Server and the Unity Client.

## 1. Packet Structure

To avoid the overhead of strings or JSON, every message sent over the network is serialized into a raw byte array called a **Packet**.

Every packet follows a strict structural contract:

| Header (Length) | Header (OpCode) | Payload (Variable) |
| :--- | :--- | :--- |
| `ushort` (2 bytes) | `ushort` (2 bytes) | *Variable bytes* depending on the packet |

- **Length:** The total size of the packet in bytes (including the header itself). This is required because TCP is a continuous stream, and the server needs to know when one packet ends and another begins (Packet Framing).
- **OpCode:** An `enum` value identifying what the packet does (e.g., `1 = SignInRequest`, `2 = MovePacket`).

## 2. Using the `Packet` Utility

The `Shared.Network.Packet` class wraps standard .NET binary reading and writing, making it incredibly easy to create and consume packets.

### Creating and Sending a Packet

When you want to send data to the server or client:

```csharp
// 1. Create a packet for a specific OpCode
using Packet packet = new Packet(OpCode.SignInRequest);

// 2. Write your payload data in order
packet.Write("MyUsername");
packet.Write("MyPassword123");

// 3. Get the final byte array and send it over the NetworkStream
byte[] rawBytes = packet.ToArray();
_stream.Write(rawBytes, 0, rawBytes.Length);
```

### Reading an Incoming Packet

When the `ClientConnection` receives a complete packet, it routes it to the `PacketHandler`. You can then read the data in the exact same order it was written:

```csharp
public static void HandleSignInRequest(ClientConnection client, Packet packet)
{
    // The OpCode (Header) was already read by the router. 
    // Start reading the payload directly:
    string username = packet.ReadString();
    string password = packet.ReadString();
    
    // ... validate SignIn and reply using client.Send(responsePacket)
}
```

## 3. The Router (`PacketHandler`)

The `PacketHandler` class in the `Server` project acts as a central switchboard. It holds a dictionary mapping `OpCode` values to specific handler functions. This provides `O(1)` routing performance.

To avoid a massive monolith file, actual logic is separated into specific Handler classes inside the `Server/Handlers/` folder (e.g., `AuthHandler.cs`).

To add a new packet feature:
1. Add a new ID to `Shared/Network/OpCode.cs`.
2. Create a handler method in a specialized class (e.g., `MovementHandler.cs`).
3. Register the mapping inside `PacketHandler.Initialize()`.
