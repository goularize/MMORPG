using System;
using System.Collections.Generic;
using Shared.Network;

namespace Server.Network
{
    public static class PacketHandler
    {
        // Maps an OpCode to a specific handler function.
        // The handler function takes the Client ID and the Packet itself.
        private static readonly Dictionary<OpCode, Action<int, Packet>> _handlers = new();

        /// <summary>
        /// Registers all packet handlers. This should be called once when the server starts.
        /// </summary>
        public static void Initialize()
        {
            _handlers.Add(OpCode.LoginRequest, HandleLoginRequest);
            
            Console.WriteLine($"Initialized PacketHandler with {_handlers.Count} routes.");
        }

        /// <summary>
        /// Routes an incoming packet to the correct handler method based on its OpCode.
        /// </summary>
        public static void HandlePacket(int clientId, byte[] data)
        {
            using Packet packet = new Packet(data);
            
            if (_handlers.TryGetValue(packet.PacketId, out var handler))
            {
                // Execute the handler
                handler(clientId, packet);
            }
            else
            {
                Console.WriteLine($"Received unknown packet OpCode: {packet.PacketId} from Client {clientId}");
            }
        }

        // --- HANDLER METHODS ---

        private static void HandleLoginRequest(int clientId, Packet packet)
        {
            // Example of reading the data from the packet
            string username = packet.ReadString();
            string password = packet.ReadString();

            Console.WriteLine($"[Client {clientId}] Requested Login with Username: '{username}'");
            
            // TODO: Validate password, create session, and send LoginResponse back
        }
    }
}
