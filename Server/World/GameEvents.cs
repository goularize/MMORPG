using System;
using Server.Quests;
using Server.World.Entities;

namespace Server.World
{
    /// <summary>
    /// In-process, synchronous events published by the systems that own a fact (combat for kills, inventory for items,
    /// the dialogue service for conversations, the quest engine for quest changes) and consumed by progress tracking
    /// and quests. Always raised and handled on the game thread, in subscription order, so the outcome inside a tick
    /// is deterministic. An exception in one subscriber is logged and does not stop the others.
    /// </summary>
    public sealed class GameEvents
    {
        /// <summary>The instance the game uses, wired to the default subscribers.</summary>
        public static GameEvents Instance { get; } = CreateDefault();

        /// <summary>The player landed the killing blow on an NPC of this template (credited once per kill, to the killer only).</summary>
        public event Action<Player, int>? NpcKilled;

        /// <summary>The player gained this quantity of an item template in the backpack (loot, crafting, rewards).</summary>
        public event Action<Player, int, int>? ItemGained;

        /// <summary>The player lost this quantity of an item template from the backpack (drop, use, craft, turn-in).</summary>
        public event Action<Player, int, int>? ItemLost;

        /// <summary>The player talked to an NPC of this template.</summary>
        public event Action<Player, int>? NpcTalked;

        /// <summary>A quest entered, changed in or left the player's quest log.</summary>
        public event Action<Player, int>? QuestChanged;

        private static GameEvents CreateDefault()
        {
            var events = new GameEvents();

            // Kill counts first, so quest logic and conditions already see the new total
            events.NpcKilled += (player, templateId) => player.Progress.AddKill(templateId);
            events.NpcKilled += QuestService.OnNpcKilled;
            events.NpcTalked += QuestService.OnNpcTalked;
            events.ItemGained += (player, _, _) => QuestService.OnInventoryChanged(player);
            events.ItemLost += (player, _, _) => QuestService.OnInventoryChanged(player);
            return events;
        }

        public void RaiseNpcKilled(Player player, int npcTemplateId) => Raise(NpcKilled, h => h(player, npcTemplateId));
        public void RaiseItemGained(Player player, int itemTemplateId, int quantity) => Raise(ItemGained, h => h(player, itemTemplateId, quantity));
        public void RaiseItemLost(Player player, int itemTemplateId, int quantity) => Raise(ItemLost, h => h(player, itemTemplateId, quantity));
        public void RaiseNpcTalked(Player player, int npcTemplateId) => Raise(NpcTalked, h => h(player, npcTemplateId));
        public void RaiseQuestChanged(Player player, int questId) => Raise(QuestChanged, h => h(player, questId));

        private static void Raise<T>(T? multicast, Action<T> invoke) where T : Delegate
        {
            if (multicast == null) return;

            foreach (var handler in multicast.GetInvocationList())
            {
                try
                {
                    invoke((T)handler);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GameEvents] A subscriber of {typeof(T).Name} failed: {ex}");
                }
            }
        }
    }
}
