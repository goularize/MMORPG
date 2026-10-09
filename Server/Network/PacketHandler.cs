using System;
using System.Collections.Generic;
using Shared.Network;
using Server.Handlers;

namespace Server.Network
{
    /// <summary>Which thread a packet's handler runs on.</summary>
    public enum PacketLane
    {
        /// <summary>
        /// Account/lobby traffic that only touches the database and the sender's own connection. Runs on the
        /// connection's read loop (serial per client) so slow DB calls never block the game tick.
        /// </summary>
        Session,

        /// <summary>
        /// Anything that reads or changes world state. Queued by the network thread and executed by the game
        /// loop (see <see cref="Server.World.GameCommandQueue"/>).
        /// </summary>
        World
    }

    public static class PacketHandler
    {
        private readonly record struct Route(PacketLane Lane, Action<IClientConnection, Packet> Handler);

        // Maps an OpCode to its lane and handler. Written once at startup, read-only afterwards.
        private static readonly Dictionary<OpCode, Route> _routes = new();

        private static void Register(OpCode opCode, PacketLane lane, Action<IClientConnection, Packet> handler)
            => _routes[opCode] = new Route(lane, handler);

        public static IReadOnlyCollection<OpCode> RegisteredOpCodes => _routes.Keys;

        public static PacketLane? GetLane(OpCode opCode)
            => _routes.TryGetValue(opCode, out var route) ? route.Lane : null;

        /// <summary>
        /// Registers all packet handlers. This should be called once when the server starts.
        /// Business logic stays in Server/Handlers; this class is only the router.
        /// </summary>
        public static void Initialize()
        {
            _routes.Clear();

            // Account & lobby (database only, no world state)
            Register(OpCode.SignInRequest, PacketLane.Session, AuthHandler.HandleSignInRequest);
            Register(OpCode.SignUpRequest, PacketLane.Session, AuthHandler.HandleSignUpRequest);
            Register(OpCode.CharacterListRequest, PacketLane.Session, CharacterHandler.HandleListRequest);
            Register(OpCode.CharacterCreateRequest, PacketLane.Session, CharacterHandler.HandleCreateRequest);
            Register(OpCode.CharacterDeleteRequest, PacketLane.Session, CharacterHandler.HandleDeleteRequest);
            // Loads the character from the DB here, then queues the world entry for the game thread
            Register(OpCode.CharacterSelectRequest, PacketLane.Session, CharacterHandler.HandleSelectRequest);

            // Enter / leave world (the only world packets a client may send before it is ready)
            Register(OpCode.WorldReadyRequest, PacketLane.World, WorldEntryHandler.HandleWorldReady);
            Register(OpCode.LogoutRequest, PacketLane.World, LogoutHandler.HandleLogoutRequest);

            // Movement
            Register(OpCode.PlayerMoveRequest, PacketLane.World, MovementHandler.HandleMoveRequest);

            // Chat
            Register(OpCode.ChatMessageRequest, PacketLane.World, ChatHandler.HandleChatMessage);

            // Targeting & Actions
            Register(OpCode.EntityInteractRequest, PacketLane.World, InteractHandler.HandleInteractRequest);
            Register(OpCode.EntityAttackRequest, PacketLane.World, CombatHandler.HandleAttackRequest);

            // Binding
            Register(OpCode.SetBindPointRequest, PacketLane.World, BindHandler.HandleSetBindPointRequest);

            // Death & Respawn
            Register(OpCode.PlayerRespawnRequest, PacketLane.World, PlayerActionHandler.HandleRespawnRequest);

            // RPG Progression
            Register(OpCode.AllocateStatPointRequest, PacketLane.World, ProgressionHandler.HandleAllocateStatPoint);

            // Items & Inventory
            Register(OpCode.MoveInventoryItemRequest, PacketLane.World, InventoryHandler.HandleMoveItem);
            Register(OpCode.SplitItemStackRequest, PacketLane.World, InventoryHandler.HandleSplitStack);
            Register(OpCode.UseConsumableItemRequest, PacketLane.World, InventoryHandler.HandleUseConsumable);
            Register(OpCode.DropItemRequest, PacketLane.World, InventoryHandler.HandleDropItem);

            // Paperdoll Equipment
            Register(OpCode.EquipItemRequest, PacketLane.World, EquipmentHandler.HandleEquipItem);
            Register(OpCode.UnequipItemRequest, PacketLane.World, EquipmentHandler.HandleUnequipItem);

            // Refinement (+1..+N)
            Register(OpCode.UpgradeItemRequest, PacketLane.World, RefinementHandler.HandleUpgradeItem);

            // Crafting & Recipes
            Register(OpCode.LearnRecipeRequest, PacketLane.World, CraftingHandler.HandleLearnRecipe);
            Register(OpCode.CraftItemRequest, PacketLane.World, CraftingHandler.HandleCraftItem);

            // Loot Satchels & Harvesting
            Register(OpCode.OpenLootSatchelRequest, PacketLane.World, LootHandler.HandleOpenLootSatchel);
            Register(OpCode.LootItemRequest, PacketLane.World, LootHandler.HandleLootItem);
            Register(OpCode.LootAllRequest, PacketLane.World, LootHandler.HandleLootAll);

            Console.WriteLine($"Initialized PacketHandler with {_routes.Count} routes.");
        }

        /// <summary>
        /// Entry point for the network threads. Session packets are handled immediately; world packets are queued
        /// for the game thread. A client that overflows its command backlog is disconnected.
        /// </summary>
        public static void Receive(IClientConnection client, byte[] data)
        {
            // [Length: 2B][OpCode: 2B][payload]
            if (data.Length < 4)
            {
                Console.WriteLine($"Client {client.Id} sent a truncated packet ({data.Length} bytes). Disconnecting.");
                client.Disconnect();
                return;
            }

            var opCode = (OpCode)BitConverter.ToUInt16(data, 2);
            if (!_routes.TryGetValue(opCode, out var route))
            {
                Console.WriteLine($"Received unknown packet OpCode: {opCode} from Client {client.Id}");
                return;
            }

            if (route.Lane == PacketLane.Session)
            {
                HandlePacket(client, data);
            }
            else if (Server.World.GameLogic.Commands.EnqueuePacket(client, data) == Server.World.EnqueueResult.ClientBacklogFull)
            {
                Console.WriteLine($"Client {client.Id} exceeded {Server.World.GameLogic.Commands.MaxPendingPerClient} pending commands. Disconnecting.");
                client.Disconnect();
            }
        }

        private static bool AllowedBeforeWorldReady(OpCode opCode)
            => opCode == OpCode.WorldReadyRequest || opCode == OpCode.LogoutRequest;

        // Only called for world-lane packets, which run on the game thread
        private static bool IsAwaitingWorldReady(IClientConnection client)
        {
            if (!client.PlayerId.HasValue) return false;
            var player = Server.World.GameLogic.MapMgr.GetPlayer(client.PlayerId.Value);
            return player != null && ReferenceEquals(player.Connection, client) && player.AwaitingWorldReady;
        }

        /// <summary>
        /// Executes the handler for a packet on the calling thread. Network threads reach it through
        /// <see cref="Receive"/>; the game loop calls it when draining the command queue.
        /// </summary>
        public static void HandlePacket(IClientConnection client, byte[] data)
        {
            using Packet packet = new Packet(data);

            if (_routes.TryGetValue(packet.PacketId, out var route))
            {
                if (route.Lane == PacketLane.World && IsAwaitingWorldReady(client) && !AllowedBeforeWorldReady(packet.PacketId))
                {
                    // The client has not loaded its map yet: it cannot act in a world it cannot see
                    return;
                }

                route.Handler(client, packet);
            }
            else
            {
                Console.WriteLine($"Received unknown packet OpCode: {packet.PacketId} from Client {client.Id}");
            }
        }
    }
}
