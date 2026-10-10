using System;
using System.Collections.Generic;
using Shared.Constants;
using Shared.Network;

namespace Client.Network.Handlers
{
    /// <summary>One line of the chat history exactly as the server sent it (or a local notice).</summary>
    public readonly struct ChatMessage
    {
        public readonly ChatChannel Channel;

        /// <summary>The sender's entity id; 0 for System messages and the echo of a whisper you sent.</summary>
        public readonly int SenderId;

        public readonly string SenderName;
        public readonly string Text;

        public ChatMessage(ChatChannel channel, int senderId, string senderName, string text)
        {
            Channel = channel;
            SenderId = senderId;
            SenderName = senderName;
            Text = text;
        }
    }

    /// <summary>
    /// Chat. The client sends what the player typed (the server filters, limits and routes it) and keeps what the
    /// server broadcasts. The UI and the speech bubbles subscribe to <see cref="OnMessage"/>.
    /// </summary>
    public static class ChatHandler
    {
        private const int HistoryCapacity = 200;

        private static readonly List<ChatMessage> _history = new();

        public static IReadOnlyList<ChatMessage> History => _history;

        public static event Action<ChatMessage> OnMessage;

        /// <summary>Back to an empty history (called when returning to character select).</summary>
        public static void ResetSession()
        {
            _history.Clear();
        }

        /// <summary>
        /// Sends what the player typed. Plain text goes to the Local channel; "/g text" to Global, "/l text" to Local and
        /// "/w name text" (also "/t") is a whisper. Returns false, with an explanation in <paramref name="error"/>,
        /// when nothing was sent because the input is not valid; empty input is ignored without an error.
        /// </summary>
        public static bool TrySend(string input, out string error)
        {
            error = null;

            input = input?.Trim() ?? string.Empty;
            if (input.Length == 0) return false;

            var channel = ChatChannel.Local;
            string target = null;
            string message = input;

            if (input[0] == '/')
            {
                int space = input.IndexOf(' ');
                string command = (space < 0 ? input : input.Substring(0, space)).ToLowerInvariant();
                string rest = space < 0 ? string.Empty : input.Substring(space + 1).Trim();

                switch (command)
                {
                    case "/l":
                    case "/local":
                        message = rest;
                        break;

                    case "/g":
                    case "/global":
                        channel = ChatChannel.Global;
                        message = rest;
                        break;

                    case "/w":
                    case "/t":
                    case "/whisper":
                        int nameEnd = rest.IndexOf(' ');
                        if (nameEnd <= 0)
                        {
                            error = "Usage: /w name message";
                            return false;
                        }
                        channel = ChatChannel.Whisper;
                        target = rest.Substring(0, nameEnd);
                        message = rest.Substring(nameEnd + 1).Trim();
                        break;

                    default:
                        error = $"Unknown command {command}. Try /g, /l or /w name.";
                        return false;
                }
            }

            if (message.Length == 0) return false;

            if (message.Length > InputRules.ChatMaxMessageLength)
            {
                error = $"Messages are limited to {InputRules.ChatMaxMessageLength} characters.";
                return false;
            }

            using (Packet packet = new Packet(OpCode.ChatMessageRequest))
            {
                packet.Write((byte)channel);
                packet.Write(message);
                if (channel == ChatChannel.Whisper) packet.Write(target);
                NetworkManager.Instance.SendPacket(packet);
            }
            return true;
        }

        /// <summary>Adds a line that did not come from the server (a usage hint, for example).</summary>
        public static void AddNotice(string text)
        {
            Add(new ChatMessage(ChatChannel.System, 0, "System", text));
        }

        public static void HandleChatBroadcast(Packet packet)
        {
            var channel = (ChatChannel)packet.ReadByte();
            int senderId = packet.ReadInt();
            string senderName = packet.ReadString();
            string text = packet.ReadString();

            Add(new ChatMessage(channel, senderId, senderName, text));
        }

        private static void Add(ChatMessage message)
        {
            _history.Add(message);
            if (_history.Count > HistoryCapacity) _history.RemoveAt(0);

            OnMessage?.Invoke(message);
        }
    }
}
