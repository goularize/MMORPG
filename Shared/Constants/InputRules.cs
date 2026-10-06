#nullable enable
using System.Text;

namespace Shared.Constants
{
    /// <summary>
    /// Limits and format rules for player-typed input. Shared so the client can pre-validate with the exact rules the
    /// authoritative server enforces. Limits stay within the database column sizes (50 / 30).
    /// </summary>
    public static class InputRules
    {
        public const int UsernameMinLength = 3;
        public const int UsernameMaxLength = 32;

        public const int PasswordMinLength = 8;
        /// <summary>BCrypt silently ignores everything past 72 bytes, so longer passwords are rejected instead.</summary>
        public const int PasswordMaxBytes = 72;

        public const int CharacterNameMinLength = 3;
        public const int CharacterNameMaxLength = 20;

        public const int ChatMaxMessageLength = 200;

        public const int MinAppearanceId = 1;
        public const int MaxAppearanceId = 10;

        /// <summary>Returns an error message for an unacceptable username, or null when it is valid.</summary>
        public static string? ValidateUsername(string? username)
        {
            if (string.IsNullOrEmpty(username) || username.Length < UsernameMinLength || username.Length > UsernameMaxLength)
                return $"Username must be {UsernameMinLength}-{UsernameMaxLength} characters.";
            foreach (char c in username)
            {
                if (!IsAsciiLetterOrDigit(c) && c != '_' && c != '-')
                    return "Username may only contain letters, digits, '_' and '-'.";
            }
            return null;
        }

        public static string? ValidatePassword(string? password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < PasswordMinLength)
                return $"Password must be at least {PasswordMinLength} characters.";
            if (Encoding.UTF8.GetByteCount(password) > PasswordMaxBytes)
                return $"Password is too long (max {PasswordMaxBytes} bytes).";
            return null;
        }

        /// <summary>Names are letters and digits, with single inner spaces; no leading/trailing space (trim first).</summary>
        public static string? ValidateCharacterName(string? name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < CharacterNameMinLength || name.Length > CharacterNameMaxLength)
                return $"Name must be {CharacterNameMinLength}-{CharacterNameMaxLength} characters.";
            if (name[0] == ' ' || name[^1] == ' ')
                return "Name cannot start or end with a space.";
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ' && name[i - 1] == ' ')
                    return "Name cannot contain consecutive spaces.";
                if (!IsAsciiLetterOrDigit(c) && c != ' ')
                    return "Name may only contain letters, digits and spaces.";
            }
            return null;
        }

        public static bool IsValidAppearanceId(int id) => id >= MinAppearanceId && id <= MaxAppearanceId;

        private static bool IsAsciiLetterOrDigit(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
    }
}
