using System;

namespace Shared.Models
{
    public record StatRange(int Min, int Max)
    {
        public int Roll(Random rng)
        {
            if (Max <= Min) return Min;
            return rng.Next(Min, Max + 1);
        }

        public bool HasValue => Max > 0 || Min > 0;
    }
}
