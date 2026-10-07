namespace Shared.Enums
{
    /// <summary>Result of an EntityInteractRequest, sent back in EntityInteractResponse.</summary>
    public enum InteractOutcome : byte
    {
        Success = 0,
        NotFound = 1,      // no such entity on the player's map or outside their area of interest
        TooFar = 2,
        TargetDead = 3,
        NothingToDo = 4,   // the target offers no interaction
        NotAvailable = 5   // the target offers an interaction the server does not implement yet (e.g. shops)
    }
}
