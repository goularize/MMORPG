using System;
using Shared.Constants;

namespace Server.World
{
    /// <summary>
    /// Hands out runtime entity IDs for NPCs, resources and loot satchels. Counters are monotonic, so an ID
    /// is never reused during a server run. Players keep their database ID (see <see cref="EntityIds"/>).
    /// </summary>
    public static class EntityIdAllocator
    {
        private static readonly long[] _counters = new long[4];

        public static int Next(EntityIdKind kind)
        {
            if (kind == EntityIdKind.Player)
            {
                throw new ArgumentException("Player entity IDs are character database IDs and are not allocated.", nameof(kind));
            }

            long sequence = System.Threading.Interlocked.Increment(ref _counters[(int)kind]);
            if (sequence > EntityIds.MaxSequence)
            {
                throw new InvalidOperationException($"{kind} entity ID space is exhausted.");
            }

            return EntityIds.Compose(kind, (int)sequence);
        }
    }
}
