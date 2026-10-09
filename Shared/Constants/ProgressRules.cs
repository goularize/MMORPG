#nullable enable
namespace Shared.Constants
{
    /// <summary>Limits for per-character progress data (flags), shared so static data can be validated against them.</summary>
    public static class ProgressRules
    {
        /// <summary>Longest flag name. Matches the CharacterFlags.Flag column size.</summary>
        public const int FlagMaxLength = 64;

        /// <summary>Most quests a character can have in its log (active or ready to turn in) at once.</summary>
        public const int MaxActiveQuests = 20;

        /// <summary>Returns an error message for an unacceptable flag name, or null when it is valid.</summary>
        public static string? ValidateFlag(string? flag)
        {
            if (string.IsNullOrEmpty(flag) || flag.Length > FlagMaxLength)
                return $"A flag must be 1-{FlagMaxLength} characters.";
            foreach (char c in flag)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
                if (!ok) return "A flag may only contain letters, digits, '_', '-' and '.'.";
            }
            return null;
        }
    }
}
