using System;
using Shared.Network;

namespace Server.Handlers
{
    public static class AuthHandler
    {
        public static void HandleLoginRequest(ClientConnection client, Packet packet)
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
