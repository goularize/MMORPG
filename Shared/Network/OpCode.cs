namespace Shared.Network
{
    public enum OpCode : ushort
    {
        // Internal/System
        Unknown = 0,
        
        // Client -> Server
        SignInRequest = 1,
        SignUpRequest = 3,
        
        // Server -> Client
        SignInResponse = 2,
        SignUpResponse = 4,

        // World / AoI
        EntitySpawn = 5,
        EntityDespawn = 6,
        EntityPositionUpdate = 7,

        // Lobby / Character Management
        CharacterListRequest = 8,
        CharacterListResponse = 9,
        CharacterCreateRequest = 10,
        CharacterCreateResponse = 11,
        CharacterDeleteRequest = 12,
        CharacterDeleteResponse = 13,
        CharacterSelectRequest = 14,
        CharacterSelectResponse = 15,

        // Movement
        PlayerMoveRequest = 16,

        // Chat
        ChatMessageRequest = 17,
        ChatMessageBroadcast = 18,

        // Vitals & Stats
        StatsUpdate = 19,
        VitalsUpdate = 20,

        // Targeting & Actions
        EntityInteractRequest = 21,
        EntityAttackRequest = 22,

        // Binding
        SetBindPointRequest = 23,
        SetBindPointResponse = 24,

        // Death & Respawn
        PlayerRespawnRequest = 25,
        PlayerRespawnResponse = 26,

        // Combat Broadcasts
        EntityCombatEvent = 27,

        // RPG Progression & Stats Allocation
        PlayerExpUpdate = 28,
        PlayerLevelUp = 29,
        AllocateStatPointRequest = 30,
        AllocateStatPointResponse = 31,
        PlayerProgressionSync = 32,

        // Entity state replication, AoI-scoped (Server -> Client)
        EntityVitals = 33,   // entityId, health, maxHealth
        EntityDeath = 34,    // entityId

        // Items & Inventory (Client -> Server)
        MoveInventoryItemRequest = 70,
        SplitItemStackRequest = 71,
        UseConsumableItemRequest = 72,
        EquipItemRequest = 73,
        UnequipItemRequest = 74,
        UpgradeItemRequest = 75,
        LearnRecipeRequest = 76,
        CraftItemRequest = 77,
        DropItemRequest = 78,

        // Items & Inventory (Server -> Client)
        InventorySync = 80,
        InventorySlotUpdate = 81,
        EquippedItemsSync = 82,
        UpgradeItemResponse = 83,
        CraftItemResponse = 84,

        // Loot Satchels & Harvesting (Client -> Server)
        OpenLootSatchelRequest = 85,
        LootItemRequest = 86,
        LootAllRequest = 87,

        // Loot Satchels & Harvesting (Server -> Client)
        LootSatchelSync = 88,
        LootSatchelClose = 89
    }
}
