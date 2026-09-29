using System;
using System.Linq;
using Shared.Network;
using Server.Database;
using Server.Database.Models;
using Server.Network;
using Server.World;
using Server.World.Entities;

namespace Server.Handlers
{
    public static class CharacterHandler
    {
        public static void HandleListRequest(IClientConnection client, Packet packet)
        {
            if (client.AccountId == null)
            {
                Console.WriteLine($"[Client {client.Id}] Unauthorized CharacterListRequest.");
                return;
            }

            using var db = AppDbContext.Factory();
            var characters = db.Characters.Where(c => c.AccountId == client.AccountId).ToList();

            using Packet response = new Packet(OpCode.CharacterListResponse);
            response.Write(characters.Count);

            foreach (var c in characters)
            {
                response.Write(c.Id);
                response.Write(c.Name);
                response.Write(c.AppearanceId);
                response.Write(c.Level);
            }

            client.Send(response);
        }

        public static void HandleCreateRequest(IClientConnection client, Packet packet)
        {
            if (client.AccountId == null) return;

            string name = packet.ReadString();
            int appearanceId = packet.ReadInt();

            bool isSuccess = false;
            string message;

            if (string.IsNullOrWhiteSpace(name))
            {
                message = "Name cannot be empty.";
            }
            else
            {
                using var db = AppDbContext.Factory();

                bool exists = db.Characters.Any(c => c.Name.ToLower() == name.ToLower());
                if (exists)
                {
                    message = "Name is already taken.";
                }
                else
                {
                    var newChar = new Character
                    {
                        AccountId = client.AccountId.Value,
                        Name = name,
                        AppearanceId = appearanceId,
                        Level = 1,
                        X = 0f,
                        Y = 0f,
                        Z = 0f,
                        Health = 100,
                        Mana = 50,
                        Strength = 10,
                        Intelligence = 10,
                        Constitution = 10,
                        Knowledge = 10
                    };

                    db.Characters.Add(newChar);
                    db.SaveChanges();

                    isSuccess = true;
                    message = "Character created successfully!";
                }
            }

            using Packet response = new Packet(OpCode.CharacterCreateResponse);
            response.Write(isSuccess);
            response.Write(message);
            client.Send(response);
        }

        public static void HandleDeleteRequest(IClientConnection client, Packet packet)
        {
            if (client.AccountId == null) return;

            int characterId = packet.ReadInt();
            bool isSuccess = false;

            using var db = AppDbContext.Factory();
            var character = db.Characters.FirstOrDefault(c => c.Id == characterId && c.AccountId == client.AccountId);

            if (character != null)
            {
                db.Characters.Remove(character);
                db.SaveChanges();
                isSuccess = true;
            }

            using Packet response = new Packet(OpCode.CharacterDeleteResponse);
            response.Write(isSuccess);
            client.Send(response);
        }

        public static void HandleSelectRequest(IClientConnection client, Packet packet)
        {
            if (client.AccountId == null) return;

            int characterId = packet.ReadInt();
            bool isSuccess = false;

            using var db = AppDbContext.Factory();
            var characterData = db.Characters.FirstOrDefault(c => c.Id == characterId && c.AccountId == client.AccountId);

            if (characterData != null)
            {
                isSuccess = true;

                // Create the live runtime Player entity based on the database data!
                var player = new Player(characterData.Id, characterData.Name, client)
                {
                    AccountId = characterData.AccountId,
                    Level = characterData.Level,
                    Exp = characterData.Exp,
                    StatPoints = characterData.StatPoints,
                    Health = characterData.Health,
                    MaxHealth = characterData.Constitution * 10, // Example stat formula
                    Mana = characterData.Mana,
                    MaxMana = characterData.Knowledge * 10,
                    Strength = characterData.Strength,
                    Intelligence = characterData.Intelligence,
                    Constitution = characterData.Constitution,
                    Knowledge = characterData.Knowledge,
                    Position = new Shared.Math.Vector3(characterData.X, characterData.Y, characterData.Z)
                };

                // Add to the World
                GameLogic.EntityMgr.AddPlayer(player);
                Console.WriteLine($"[Client {client.Id}] Selected character '{player.Name}' and entered the world.");
            }

            using Packet response = new Packet(OpCode.CharacterSelectResponse);
            response.Write(isSuccess);
            
            if (isSuccess)
            {
                // Send starting coordinates so client can load scene
                response.Write(characterData!.X);
                response.Write(characterData.Y);
                response.Write(characterData.Z);
            }

            client.Send(response);
        }
    }
}
