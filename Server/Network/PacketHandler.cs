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
        private static readonly Dictionary<OpCode, Action<IClientConnection, Packet>> _handlers = new();

        /// <summary>
        /// Registers all packet handlers. This should be called once when the server starts.
        /// </summary>
        public static void Initialize()
        {
            // Map OpCodes to their specific controller/handler methods
            _handlers.Add(OpCode.SignInRequest, AuthHandler.HandleSignInRequest);
            _handlers.Add(OpCode.SignUpRequest, AuthHandler.HandleSignUpRequest);

            // Character Lobby
            _handlers.Add(OpCode.CharacterListRequest, CharacterHandler.HandleListRequest);
            _handlers.Add(OpCode.CharacterCreateRequest, CharacterHandler.HandleCreateRequest);
            _handlers.Add(OpCode.CharacterDeleteRequest, CharacterHandler.HandleDeleteRequest);
            _handlers.Add(OpCode.CharacterSelectRequest, CharacterHandler.HandleSelectRequest);

            // Movement
            _handlers.Add(OpCode.PlayerMoveRequest, MovementHandler.HandleMoveRequest);

            // Chat
            _handlers.Add(OpCode.ChatMessageRequest, ChatHandler.HandleChatMessage);

            // Targeting & Actions
            _handlers.Add(OpCode.EntityInteractRequest, InteractHandler.HandleInteractRequest);
            _handlers.Add(OpCode.EntityAttackRequest, CombatHandler.HandleAttackRequest);
            // Binding
            _handlers.Add(OpCode.SetBindPointRequest, BindHandler.HandleSetBindPointRequest);

            // Death & Respawn
            _handlers.Add(OpCode.PlayerRespawnRequest, PlayerActionHandler.HandleRespawnRequest);

            // RPG Progression
            _handlers.Add(OpCode.AllocateStatPointRequest, ProgressionHandler.HandleAllocateStatPoint);

            // Items & Inventory
            _handlers.Add(OpCode.MoveInventoryItemRequest, InventoryHandler.HandleMoveItem);
            _handlers.Add(OpCode.SplitItemStackRequest, InventoryHandler.HandleSplitStack);
            _handlers.Add(OpCode.UseConsumableItemRequest, InventoryHandler.HandleUseConsumable);
            _handlers.Add(OpCode.DropItemRequest, InventoryHandler.HandleDropItem);

            // Paperdoll Equipment
            _handlers.Add(OpCode.EquipItemRequest, EquipmentHandler.HandleEquipItem);
            _handlers.Add(OpCode.UnequipItemRequest, EquipmentHandler.HandleUnequipItem);

            // Refinement (+1..+N)
            _handlers.Add(OpCode.UpgradeItemRequest, RefinementHandler.HandleUpgradeItem);

            // Crafting & Recipes
            _handlers.Add(OpCode.LearnRecipeRequest, CraftingHandler.HandleLearnRecipe);
            _handlers.Add(OpCode.CraftItemRequest, CraftingHandler.HandleCraftItem);

            // Loot Satchels & Harvesting
            _handlers.Add(OpCode.OpenLootSatchelRequest, LootHandler.HandleOpenLootSatchel);
            _handlers.Add(OpCode.LootItemRequest, LootHandler.HandleLootItem);
            _handlers.Add(OpCode.LootAllRequest, LootHandler.HandleLootAll);

            Console.WriteLine($"Initialized PacketHandler with {_handlers.Count} routes.");
        }

        /// <summary>
        /// Routes an incoming packet to the correct handler method based on its OpCode.
        /// </summary>
        public static void HandlePacket(IClientConnection client, byte[] data)
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
