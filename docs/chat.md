# Chat

Chat is server-routed: the client sends what the player typed and renders what the server broadcasts. The server
filters (profanity), limits (`InputRules.ChatMaxMessageLength`), rate-limits per player and routes by channel; the
client never decides who receives a message.

## Protocol (`Shared/Network/OpCode.cs`, protocol `0.2.6-alpha`)

- `ChatMessageRequest` (C→S): `ChatChannel` (byte), message, and the target name when the channel is `Whisper`.
- `ChatMessageBroadcast` (S→C): `ChatChannel` (byte), `senderId` (int), `senderName`, message.
  - `Local`: players within `GameRules.ChatLocalRadius` of the sender, the sender included.
  - `Global`: everybody online. `Whisper`: the target, plus an echo to the sender named `To <target>`.
  - `System`: notices from the server (limits, "player is not online").
  - `senderId` is the sender's entity id, so the client can put a speech bubble over the right character. It is `0`
    for `System` messages and for the whisper echo.

## Client (#173)

- `ChatHandler` (`Client/Assets/Scripts/Network/Handlers/`) sends and parses commands, keeps the last 200 messages and
  raises `OnMessage`. Plain text is Local; `/g text` is Global, `/l text` Local, `/w name text` (or `/t`) a whisper.
  Reset on logout.
- `ChatUI`: one mixed history with a colour per channel (System lines are prefixed `[System]`) and an input line.
  Enter focuses the input, Enter again sends and gives the keyboard back to the game, Esc cancels. The history is
  scrollable with the mouse wheel (a Scroll Rect around the text): it follows the newest message unless the player
  scrolled up, and keeps the last 200 messages. Other players' text is escaped so it cannot inject rich text.
- `HoverFade` (a reusable component) fades the chat window: partly transparent until the mouse is over it, and opaque
  while typing. The values are Inspector fields.
- `InputFocus.IsTyping` is true while a text field has focus; `PlayerController` stops moving and the quest log
  ignores its hotkey while it is.
- `ChatBubbleController`: Local messages appear as a bubble over the sender's head, replacing their previous one; it
  fades after a time that grows with the length. Position, look, length cap and durations are Inspector fields.
  Global and whispers have no bubble.
