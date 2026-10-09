namespace Shared.Enums
{
    /// <summary>Result of a LogoutRequest, sent back in LogoutResponse.</summary>
    public enum LogoutResult : byte
    {
        Success = 0,         // the character left the world; the client returns to character select
        ConfirmRequired = 1, // in combat: leaving keeps the character in the world for the linger time; ask the player first
        NotInWorld = 2       // the connection has no character in the world (nothing was changed)
    }
}
