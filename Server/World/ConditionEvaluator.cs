using System.Collections.Generic;
using Server.World.Entities;
using Shared.Data;
using Shared.Enums;

namespace Server.World
{
    /// <summary>
    /// Decides whether a character meets a <see cref="Condition"/>. Pure: it only reads the character's progress,
    /// level and items, so it runs on the game thread without side effects.
    /// </summary>
    public static class ConditionEvaluator
    {
        /// <summary>True when every condition holds. A null or empty list always holds (AND of nothing).</summary>
        public static bool Evaluate(Player player, IReadOnlyList<Condition>? conditions)
        {
            if (conditions == null) return true;

            for (int i = 0; i < conditions.Count; i++)
            {
                if (!Evaluate(player, conditions[i])) return false;
            }
            return true;
        }

        public static bool Evaluate(Player player, Condition condition)
        {
            bool met = Check(player, condition);
            return condition.Negate ? !met : met;
        }

        private static bool Check(Player player, Condition c)
        {
            switch (c.Type)
            {
                case ConditionType.KillCount:
                    return player.Progress.GetKillCount(c.Id) >= c.Min;

                case ConditionType.QuestState:
                    return player.Progress.GetQuestState(c.Id) == c.State;

                case ConditionType.Flag:
                    return !string.IsNullOrEmpty(c.Name) && player.Progress.HasFlag(c.Name);

                case ConditionType.Level:
                    return player.Level >= c.Min && (c.Max <= 0 || player.Level <= c.Max);

                case ConditionType.HasItem:
                    return InventoryOps.CountOwned(player, c.Id) >= c.Min;

                default:
                    // An unknown type never validates at startup, so it cannot get here; failing closed is the safe answer
                    return false;
            }
        }
    }
}
