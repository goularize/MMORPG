# Chat System Architecture

The MMORPG chat system is handled by the `ChatHandler` on the server and relies on binary packet serialization to broadcast text efficiently.

## Network Protocol

The chat system uses two primary OpCodes:
- **`ChatMessageRequest` (17):** Sent from the Client to the Server when a player presses Enter to send a message.
- **`ChatMessageBroadcast` (18):** Sent from the Server to one or more Clients to display a message on their screen.

### Packet Structures

**Client -> Server (`ChatMessageRequest`)**
| Type | Value | Description |
|------|-------|-------------|
| `ushort` | `Length` | Total packet length (handled automatically by `Packet.cs`) |
| `ushort` | `17` | `OpCode.ChatMessageRequest` |
| `byte` | `Channel` | The target `ChatChannel` enum value. |
| `string` | `Message` | The actual text message. |
| `string` | `TargetName` | *(Only if Channel == Whisper)* The name of the player to whisper. |

**Server -> Client (`ChatMessageBroadcast`)**
| Type | Value | Description |
|------|-------|-------------|
| `ushort` | `Length` | Total packet length |
| `ushort` | `18` | `OpCode.ChatMessageBroadcast` |
| `byte` | `Channel` | The `ChatChannel` enum value defining the message color/type. |
| `string` | `SenderName`| The name of the player who sent it (or "System"). |
| `string` | `Message` | The actual text message. |

---

## Chat Channels

The system uses the `ChatChannel` enum (byte) to determine routing logic and client-side UI coloring.

### 1. Global (`ChatChannel.Global = 1`)
- **Routing:** Iterates through every active player inside `GameLogic.EntityMgr.Players.Values`.
- **Usage:** World-wide announcements, looking for group (LFG), or trade chat.

### 2. Local (`ChatChannel.Local = 0`)
- **Routing:** Uses `Vector3.Distance(sender.Position, target.Position)`.
- **Radius:** Currently hardcoded to `50.0f` units. Only players within this Euclidean distance will receive the packet.
- **Usage:** General spatial chatter. Matches the Area of Interest (AoI) radius so you only hear people you can physically see.

### 3. Whisper (`ChatChannel.Whisper = 2`)
- **Routing:** Performs a fast `FirstOrDefault` lookup on active players by `Name` (case-insensitive).
- **Behavior:** 
  - If found: Sends the message to the target, and sends an `Echo` back to the sender (e.g., `"To Bob: Hello"`).
  - If not found: Generates a `System` error message and returns it to the sender.
- **Usage:** Private 1-on-1 conversations.

### 4. System (`ChatChannel.System = 3`)
- **Routing:** Exclusively used by the Server to send critical alerts or errors directly to a specific client.
- **Usage:** "Player not found", "Inventory Full", "Server restarting in 5 minutes".

---

## Database Logging

The server supports asynchronous saving of chat history to the PostgreSQL database via Entity Framework. To save bandwidth and storage, this is configured on a per-channel basis inside the Server's `.env` file.

**Configuration (`Server/.env`):**
```env
CHAT_LOG_LOCAL=false
CHAT_LOG_GLOBAL=true
CHAT_LOG_WHISPER=true
CHAT_LOG_SYSTEM=false
```

When enabled for a channel, the `ChatHandler` spawns a detached background `Task` to log the UTC Timestamp, Channel, SenderName, TargetName (if whisper), and the Message payload to the `ChatLogs` database table without blocking the 30-TPS server cycle.

---

## Future Enhancements
- **Profanity Filter:** Add a regex scrubber before the `switch(channel)` block in `ChatHandler.cs`.
- **Mute / Ignore List:** Validate `sender.Name` against a target's local blocklist before calling `target.Connection.Send`.
- **Guild / Party Chat:** Add channels `4` and `5` that lookup players by their active Party ID or Guild ID in the database.
