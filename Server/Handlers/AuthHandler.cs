using System;
using System.Linq;
using Shared.Network;
using Server.Database;
using Server.Database.Models;
using Server.Network;

namespace Server.Handlers
{
    public static class AuthHandler
    {
        public static void HandleSignUpRequest(IClientConnection client, Packet packet)
        {
            string clientVersion = packet.ReadString();
            if (clientVersion != ServerConfig.GameVersion)
            {
                using Packet errResponse = new Packet(OpCode.SignUpResponse);
                errResponse.Write(false);
                errResponse.Write($"Version mismatch! Server is running {ServerConfig.GameVersion}.");
                client.Send(errResponse);
                return;
            }

            string username = packet.ReadString();
            string password = packet.ReadString();

            Console.WriteLine($"[Client {client.Id}] Requested Sign Up with Username: '{username}'");
            
            bool isSuccess = false;
            string message;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                message = "Username and password cannot be empty.";
            }
            else
            {
                using var db = new AppDbContext();
                
                // Check if user exists
                bool exists = db.Accounts.Any(a => a.Username.ToLower() == username.ToLower());
                if (exists)
                {
                    message = "Username is already taken.";
                }
                else
                {
                    // Hash the password for security
                    string hash = BCrypt.Net.BCrypt.HashPassword(password);
                    
                    int defaultSlots = 3;
                    if (int.TryParse(Environment.GetEnvironmentVariable("DEFAULT_CHARACTER_SLOT"), out int envSlots))
                    {
                        defaultSlots = envSlots;
                    }

                    var newAccount = new Account
                    {
                        Username = username,
                        PasswordHash = hash,
                        CharacterSlots = defaultSlots
                    };
                    
                    db.Accounts.Add(newAccount);
                    db.SaveChanges();
                    
                    isSuccess = true;
                    message = "Account created successfully!";
                    Console.WriteLine($"[Client {client.Id}] Account created for '{username}'.");
                }
            }

            using Packet response = new Packet(OpCode.SignUpResponse);
            response.Write(isSuccess);
            response.Write(message);
            client.Send(response);
        }

        public static void HandleSignInRequest(IClientConnection client, Packet packet)
        {
            string clientVersion = packet.ReadString();
            if (clientVersion != ServerConfig.GameVersion)
            {
                using Packet errResponse = new Packet(OpCode.SignInResponse);
                errResponse.Write(false);
                errResponse.Write($"Version mismatch! Server is running {ServerConfig.GameVersion}.");
                client.Send(errResponse);
                return;
            }

            string username = packet.ReadString();
            string password = packet.ReadString();

            Console.WriteLine($"[Client {client.Id}] Requested Sign In with Username: '{username}'");
            
            bool isSuccess = false;
            string message = "Invalid credentials.";

            using var db = new AppDbContext();
            
            // Find the user by username
            var account = db.Accounts.FirstOrDefault(a => a.Username.ToLower() == username.ToLower());
            
            if (account != null)
            {
                // Verify the hashed password
                if (BCrypt.Net.BCrypt.Verify(password, account.PasswordHash))
                {
                    isSuccess = true;
                    message = $"Welcome to the game, {username}!";
                    client.AccountId = account.Id; // Bind the database account to the network session
                    Console.WriteLine($"[Client {client.Id}] Sign In Successful for Account ID {account.Id}.");
                }
            }
            
            if (!isSuccess)
            {
                Console.WriteLine($"[Client {client.Id}] Sign In Failed.");
            }

            using Packet response = new Packet(OpCode.SignInResponse);
            response.Write(isSuccess);
            response.Write(message);
            
            client.Send(response);
        }
    }
}
