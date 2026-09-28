using System;
using System.Collections.Generic;
using Shared.Network;

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
            _handlers.Add(OpCode.LoginRequest, HandleLoginRequest);
            
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

        // --- HANDLER METHODS ---

        private static void HandleLoginRequest(ClientConnection client, Packet packet)
        {
            // Read the data from the incoming packet
            string username = packet.ReadString();
            string password = packet.ReadString();

            Console.WriteLine($"[Client {client.Id}] Requested Login with Username: '{username}'");
            
            // Mock authentication logic
            bool isSuccess = false;
            string message = "Invalid credentials.";

            // Very simple mock check: allow any username that isn't empty, as long as password is "123"
            if (!string.IsNullOrWhiteSpace(username) && password == "123")
            {
                isSuccess = true;
                message = $"Welcome to the game, {username}!";
                Console.WriteLine($"[Client {client.Id}] Login Successful.");
            }
            else
            {
                Console.WriteLine($"[Client {client.Id}] Login Failed.");
            }

            // Create and send the response packet back to the client
            using Packet response = new Packet(OpCode.LoginResponse);
            response.Write(isSuccess);
            response.Write(message);
            
            client.Send(response);
        }
    }
}
