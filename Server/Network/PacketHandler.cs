using System;
using System.Collections.Generic;
using Shared.Network;
using Server.Handlers;

namespace Server.Network
{
    public static class PacketHandler
    {
        // Maps an OpCode to a specific handler function.
        // The handler function takes the ClientConnection and the Packet itself.
        private static readonly Dictionary<OpCode, Action<ClientConnection, Packet>> _handlers = new();

        /// <summary>
        /// Registers all packet handlers. This should be called once when the server starts.
        /// </summary>
        public static void Initialize()
        {
            // Map OpCodes to their specific controller/handler methods
            _handlers.Add(OpCode.LoginRequest, AuthHandler.HandleLoginRequest);
            _handlers.Add(OpCode.SignUpRequest, AuthHandler.HandleSignUpRequest);

            Console.WriteLine($"Initialized PacketHandler with {_handlers.Count} routes.");
        }

        /// <summary>
        /// Routes an incoming packet to the correct handler method based on its OpCode.
        /// </summary>
        public static void HandlePacket(ClientConnection client, byte[] data)
        {
            using Packet packet = new Packet(data);

            if (_handlers.TryGetValue(packet.PacketId, out var handler))
            {
                // Execute the handler
                handler(client, packet);
            }
            else
            {
                Console.WriteLine($"Received unknown packet OpCode: {packet.PacketId} from Client {client.Id}");
            }
        }
    }
}
