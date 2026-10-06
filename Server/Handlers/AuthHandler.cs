using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shared.Constants;
using Shared.Network;
using Server.Database;
using Server.Database.Models;
using Server.Network;

namespace Server.Handlers
{
    public static class AuthHandler
    {
        /// <summary>
        /// Binds the database account to the network session. An account has one session at a time: a previous
        /// connection of the same account is disconnected (this also frees accounts held by a dead connection).
        /// </summary>
        private static void BindSession(IClientConnection client, int accountId)
        {
            if (client.AccountId.HasValue && client.AccountId.Value != accountId)
            {
                SessionRegistry.Release(client.AccountId.Value, client);
            }

            client.AccountId = accountId;
            SessionRegistry.Bind(accountId, client);
        }

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

            if (client.PlayerId.HasValue)
            {
                using Packet inWorld = new Packet(OpCode.SignUpResponse);
                inWorld.Write(false);
                inWorld.Write("You are already in the world.");
                client.Send(inWorld);
                return;
            }

            string username = packet.ReadString();
            string password = packet.ReadString();

            Console.WriteLine($"[Client {client.Id}] Requested Sign Up with Username: '{username}'");
            
            bool isSuccess = false;
            string message;

            string? invalid = InputRules.ValidateUsername(username) ?? InputRules.ValidatePassword(password);
            if (invalid != null)
            {
                message = invalid;
            }
            else
            {
                using var db = AppDbContext.Factory();
                
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
                    
                    var newAccount = new Account
                    {
                        Username = username,
                        PasswordHash = hash,
                        CharacterSlots = ServerConfig.DefaultCharacterSlots
                    };
                    
                    db.Accounts.Add(newAccount);
                    try
                    {
                        db.SaveChanges();

                        isSuccess = true;
                        message = "Account created successfully!";
                        BindSession(client, newAccount.Id);
                        Console.WriteLine($"[Client {client.Id}] Account created for '{username}'.");
                    }
                    catch (DbUpdateException)
                    {
                        // Lost a race with a concurrent sign-up of the same name (unique index)
                        message = "Username is already taken.";
                    }
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

            if (client.PlayerId.HasValue)
            {
                using Packet inWorld = new Packet(OpCode.SignInResponse);
                inWorld.Write(false);
                inWorld.Write("You are already in the world.");
                client.Send(inWorld);
                return;
            }

            string username = packet.ReadString();
            string password = packet.ReadString();

            Console.WriteLine($"[Client {client.Id}] Requested Sign In with Username: '{username}'");
            
            bool isSuccess = false;
            string message = "Invalid credentials.";

            // Cheap bounds before touching the database or BCrypt; the message stays generic on purpose
            bool plausible = username.Length <= InputRules.UsernameMaxLength
                && System.Text.Encoding.UTF8.GetByteCount(password) <= InputRules.PasswordMaxBytes;

            using var db = AppDbContext.Factory();
            
            // Find the user by username
            var account = plausible ? db.Accounts.FirstOrDefault(a => a.Username.ToLower() == username.ToLower()) : null;
            
            if (account != null)
            {
                // Verify the hashed password
                if (BCrypt.Net.BCrypt.Verify(password, account.PasswordHash))
                {
                    isSuccess = true;
                    message = $"Welcome to the game, {username}!";
                    BindSession(client, account.Id);
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
