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

                var account = db.Accounts.FirstOrDefault(a => a.Id == client.AccountId.Value);
                if (account == null)
                {
                    message = "Account not found.";
                }
                else
                {
                    int currentCount = db.Characters.Count(c => c.AccountId == client.AccountId.Value);
                    if (currentCount >= account.CharacterSlots)
                    {
                        message = $"You cannot create more than {account.CharacterSlots} characters.";
                    }
                    else
                    {
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
                                MapId = 1, // Start at MapId 1 (Starting Village)
                                X = 0f,
                                Y = 0f,
                                Z = 0f,
                                BindMapId = 1,
                                BindX = 0f,
                                BindY = 0f,
                                BindZ = 0f,
                                Health = 100,
                                Mana = 50,
                                Strength = 10,
                                Intelligence = 10,
                                Constitution = 10,
                                Knowledge = 10,
                                Gold = 100,
                                InventorySlots = ServerConfig.DefaultBackpackSlots
                            };

                            db.Characters.Add(newChar);
                            db.SaveChanges();

                            // Starter Kit
                            var starterSword = ItemFactory.CreateItem(1001, newChar.Id, 1);
                            if (starterSword != null) { starterSword.SlotIndex = 0; db.CharacterItems.Add(starterSword); }

                            var starterTunic = ItemFactory.CreateItem(1301, newChar.Id, 1);
                            if (starterTunic != null) { starterTunic.SlotIndex = 1; db.CharacterItems.Add(starterTunic); }

                            var starterBoots = ItemFactory.CreateItem(1401, newChar.Id, 1);
                            if (starterBoots != null) { starterBoots.SlotIndex = 2; db.CharacterItems.Add(starterBoots); }

                            var hpPotions = ItemFactory.CreateItem(2001, newChar.Id, 5);
                            if (hpPotions != null) { hpPotions.SlotIndex = 3; db.CharacterItems.Add(hpPotions); }

                            var mpPotions = ItemFactory.CreateItem(2002, newChar.Id, 3);
                            if (mpPotions != null) { mpPotions.SlotIndex = 4; db.CharacterItems.Add(mpPotions); }

                            var weaponStones = ItemFactory.CreateItem(3004, newChar.Id, 3);
                            if (weaponStones != null) { weaponStones.SlotIndex = 5; db.CharacterItems.Add(weaponStones); }

                            var armorStones = ItemFactory.CreateItem(3005, newChar.Id, 3);
                            if (armorStones != null) { armorStones.SlotIndex = 6; db.CharacterItems.Add(armorStones); }

                            db.SaveChanges();

                            isSuccess = true;
                            message = "Character created successfully!";
                        }
                    }
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

            // A character that is in the world (or entering it) cannot be deleted from under its own session
            if (character != null && client.PlayerId == characterId)
            {
                Console.WriteLine($"[Client {client.Id}] Refused to delete character {characterId}: it is in the world.");
                character = null;
            }

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

            // One character per connection: a second select would orphan the first character in the world
            if (client.PlayerId.HasValue)
            {
                Console.WriteLine($"[Client {client.Id}] Refused to select character {characterId}: character {client.PlayerId} is already selected.");
                SendSelectFailure(client);
                return;
            }

            // Read-your-writes: a character that was just saved (e.g. a quick re-login) must not be loaded from
            // the database before its pending write-behind changes have landed.
            if (!Server.Persistence.PersistenceService.Instance.Flush(characterId, TimeSpan.FromSeconds(3)))
            {
                Console.WriteLine($"[Persistence] Timed out waiting for pending saves of character {characterId} before loading it.");
            }

            using var db = AppDbContext.Factory();
            var characterData = db.Characters.FirstOrDefault(c => c.Id == characterId && c.AccountId == client.AccountId);

            if (characterData != null && !Shared.Constants.EntityIds.IsValidPlayerId(characterData.Id))
            {
                // The character ID would collide with other entity kinds; refuse to make it a live entity.
                Console.WriteLine($"[Error] Character {characterData.Id} is outside the valid player entity ID range.");
                characterData = null;
            }

            if (characterData != null)
            {
                // Create the live runtime Player entity based on the database data!
                var player = new Player(characterData.Id, characterData.Name, client)
                {
                    AccountId = characterData.AccountId,
                    Level = characterData.Level,
                    Exp = characterData.Exp,
                    StatPoints = characterData.StatPoints,
                    Health = characterData.Health,
                    Mana = characterData.Mana,
                    Strength = characterData.Strength,
                    Intelligence = characterData.Intelligence,
                    Constitution = characterData.Constitution,
                    Knowledge = characterData.Knowledge,
                    Gold = characterData.Gold,
                    InventorySlots = characterData.InventorySlots > 0 ? characterData.InventorySlots : ServerConfig.DefaultBackpackSlots,
                    MapId = characterData.MapId,
                    Position = new Shared.Math.Vector3(characterData.X, characterData.Y, characterData.Z),
                    BindMapId = characterData.BindMapId,
                    BindPosition = new Shared.Math.Vector3(characterData.BindX, characterData.BindY, characterData.BindZ)
                };

                // Load Inventory & Equipment
                var dbItems = db.CharacterItems.Where(ci => ci.CharacterId == characterId).ToList();
                foreach (var item in dbItems)
                {
                    if (item.IsEquipped && item.EquippedSlot.HasValue)
                    {
                        player.EquippedItems[item.EquippedSlot.Value] = item;
                    }
                    else
                    {
                        player.Inventory.Add(item);
                    }
                }

                // Load Learned Recipes
                var dbRecipes = db.CharacterLearnedRecipes.Where(clr => clr.CharacterId == characterId).Select(clr => clr.RecipeId).ToList();
                player.LearnedRecipes.UnionWith(dbRecipes);

                // Calculate all derived stats correctly with gear scaling
                player.CalculateDerivedStats();

                // Cap health/mana to max if somehow they exceeded it (or for new characters)
                if (player.Health > player.MaxHealth) player.Health = player.MaxHealth;
                if (player.Mana > player.MaxMana) player.Mana = player.MaxMana;

                // The world entry and its replies must run on the game thread. Posting here keeps it ordered
                // before anything this client sends next, and before its disconnect.
                // The character is claimed for this connection right away (not only once it is in the world), so a
                // second select or a delete arriving before the world entry runs is refused too.
                client.PlayerId = player.Id;
                GameLogic.Commands.Post(() => EnterWorld(client, player));
                return;
            }

            SendSelectFailure(client);
        }

        private static void SendSelectFailure(IClientConnection client)
        {
            using Packet failure = new Packet(OpCode.CharacterSelectResponse);
            failure.Write(false);
            client.Send(failure);
        }

        /// <summary>Runs on the game thread: puts a loaded character into the world and syncs its state to the client.</summary>
        private static void EnterWorld(IClientConnection client, Player player)
        {
            // The client may have dropped while its character was loading
            if (!client.IsConnected) return;

            if (!GameLogic.MapMgr.AddPlayer(player))
            {
                // Missing map, or the character is somehow already online: do not report success for a player that
                // is not in the world, and let the client pick again.
                client.PlayerId = null;
                SendSelectFailure(client);
                return;
            }

            Console.WriteLine($"[Client {client.Id}] Selected character '{player.Name}' and entered the world.");

            using Packet response = new Packet(OpCode.CharacterSelectResponse);
            response.Write(true);
            // Send starting coordinates so client can load scene
            response.Write(player.MapId);
            response.Write(player.Position.X);
            response.Write(player.Position.Y);
            response.Write(player.Position.Z);
            client.Send(response);

            var activePlayer = GameLogic.MapMgr.GetPlayer(player.Id);
            if (activePlayer != null)
            {
                using Packet statsPacket = new Packet(OpCode.StatsUpdate);
                statsPacket.Write(activePlayer.MaxHealth);
                statsPacket.Write(activePlayer.MaxMana);
                statsPacket.Write(activePlayer.Attack);
                statsPacket.Write(activePlayer.MagicAttack);
                statsPacket.Write(activePlayer.Defense);
                statsPacket.Write(activePlayer.MagicDefense);
                client.Send(statsPacket);

                using Packet vitalsPacket = new Packet(OpCode.VitalsUpdate);
                vitalsPacket.Write(activePlayer.Id);
                vitalsPacket.Write(activePlayer.Health);
                vitalsPacket.Write(activePlayer.Mana);
                client.Send(vitalsPacket);

                // Sync Progression & Base Attributes
                long expToNextLevel = ServerConfig.GetExpForNextLevel(activePlayer.Level);
                using Packet progPacket = new Packet(OpCode.PlayerProgressionSync);
                progPacket.Write(activePlayer.Level);
                progPacket.Write(activePlayer.Exp);
                progPacket.Write(expToNextLevel);
                progPacket.Write(activePlayer.StatPoints);
                progPacket.Write(activePlayer.Strength);
                progPacket.Write(activePlayer.Intelligence);
                progPacket.Write(activePlayer.Constitution);
                progPacket.Write(activePlayer.Knowledge);
                client.Send(progPacket);

                // Sync Exp Bar specifically
                using Packet expPacket = new Packet(OpCode.PlayerExpUpdate);
                expPacket.Write(activePlayer.Id);
                expPacket.Write(activePlayer.Exp);
                expPacket.Write(expToNextLevel);
                client.Send(expPacket);

                // Sync Inventory & Equipment
                InventoryHandler.SendInventorySync(activePlayer);
                EquipmentHandler.SendEquippedItemsSync(activePlayer);
            }

        }
    }
}
