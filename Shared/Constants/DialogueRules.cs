#nullable enable
using System;
using System.Collections.Generic;

namespace Shared.Constants
{
    /// <summary>Limits and text rules for dialogue and quest text, shared so authoring tools can pre-validate.</summary>
    public static class DialogueRules
    {
        /// <summary>Keeps a DialogueOpen packet far below the 65535-byte frame limit.</summary>
        public const int MaxTextLength = 600;
        public const int MaxLabelLength = 80;

        /// <summary>Most options an authored greeting or node may have.</summary>
        public const int MaxAuthoredOptions = 8;

        /// <summary>Most options the server ever sends on one screen (authored + generated quest options).</summary>
        public const int MaxOptionsPerScreen = 12;

        /// <summary>Longest dialogue id.</summary>
        public const int MaxIdLength = 64;

        /// <summary>Prefix of nodes the server builds itself (quest offer/progress/complete); authored nodes cannot use it.</summary>
        public const string GeneratedNodePrefix = "quest:";

        /// <summary>Target of a GotoNode that returns to the NPC's greeting.</summary>
        public const string GreetingNode = "$greeting";

        public const string TokenPlayerName = "playerName";
        public const string TokenPlayerLevel = "playerLevel";
        public const string TokenNpcName = "npcName";

        public static readonly IReadOnlyList<string> KnownTokens = new[] { TokenPlayerName, TokenPlayerLevel, TokenNpcName };

        private static bool IsKnownToken(string token)
        {
            foreach (string known in KnownTokens)
            {
                if (known == token) return true;
            }
            return false;
        }

        /// <summary>Returns an error message for text that is empty, too long or uses an unknown or unbalanced {{token}}, or null.</summary>
        public static string? ValidateText(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return "text is empty.";
            if (text.Length > maxLength) return "text is longer than " + maxLength + " characters.";

            int index = 0;
            while (index < text.Length)
            {
                int open = text.IndexOf("{{", index, StringComparison.Ordinal);
                int close = text.IndexOf("}}", index, StringComparison.Ordinal);

                if (open < 0 && close < 0) break;
                if (close >= 0 && (open < 0 || close < open)) return "text has a stray closing braces pair.";

                int end = text.IndexOf("}}", open + 2, StringComparison.Ordinal);
                if (end < 0) return "text has an unclosed token.";

                string token = text.Substring(open + 2, end - open - 2);
                if (!IsKnownToken(token))
                    return "text uses unknown token '" + token + "' (known: " + string.Join(", ", KnownTokens) + ").";
                index = end + 2;
            }

            return null;
        }
    }
}
