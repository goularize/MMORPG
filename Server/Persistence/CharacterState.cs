namespace Server.Persistence
{
    /// <summary>
    /// Immutable copy of everything about a character that is persisted on the character row. It is taken on the
    /// game thread, so the persistence workers never read live entities that the game loop is still changing.
    /// </summary>
    public sealed record CharacterState(
        int CharacterId,
        int Level,
        long Exp,
        int StatPoints,
        int Health,
        int Mana,
        int Strength,
        int Intelligence,
        int Constitution,
        int Knowledge,
        long Gold,
        int InventorySlots,
        int MapId,
        float X,
        float Y,
        float Z,
        int BindMapId,
        float BindX,
        float BindY,
        float BindZ);
}
