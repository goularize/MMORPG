namespace Shared.Constants
{
    /// <summary>What kind of world object an entity ID refers to. Stored in the top bits of the ID.</summary>
    public enum EntityIdKind
    {
        Player = 0,
        Npc = 1,
        Resource = 2,
        Satchel = 3
    }

    /// <summary>
    /// Entity IDs are positive 31-bit ints: 2 kind bits followed by a 29-bit sequence.
    /// Because the kind lives in the ID, IDs of different kinds can never collide, and the wire format
    /// (a plain int) is unchanged. Player IDs are the character's database ID (kind bits = 0), so a
    /// player ID is valid only while it fits in <see cref="MaxSequence"/>.
    /// </summary>
    public static class EntityIds
    {
        public const int KindShift = 29;
        public const int MaxSequence = (1 << KindShift) - 1;

        public static int Compose(EntityIdKind kind, int sequence)
        {
            if (sequence < 1 || sequence > MaxSequence)
            {
                throw new System.ArgumentOutOfRangeException(nameof(sequence), $"Entity sequence must be in 1..{MaxSequence}.");
            }
            return ((int)kind << KindShift) | sequence;
        }

        public static EntityIdKind GetKind(int id) => (EntityIdKind)((id >> KindShift) & 0b11);

        public static int GetSequence(int id) => id & MaxSequence;

        /// <summary>True when a database character ID can be used as a Player entity ID.</summary>
        public static bool IsValidPlayerId(int characterId) => characterId >= 1 && characterId <= MaxSequence;

        /// <summary>Readable form for logs, e.g. "Npc:12".</summary>
        public static string Describe(int id) => $"{GetKind(id)}:{GetSequence(id)}";
    }
}
