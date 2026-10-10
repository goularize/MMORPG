using Server.Data;
using Server.World.Entities;
using Shared.Enums;

namespace Server.Quests
{
    /// <summary>
    /// Which marker an NPC shows to one player: "?" when the player can turn a quest in to it, "!" when it offers a
    /// quest the player can accept now, nothing otherwise. A turn-in outranks an offer. The same rules as the
    /// dialogue's [New] / [Complete] options, so a marker always leads to something.
    /// </summary>
    public static class QuestMarkers
    {
        public static EntityMarker For(Player player, NPC npc)
        {
            if (npc.TemplateId <= 0 || npc.Type != EntityType.Npc || npc.CurrentState == AIState.Dead)
                return EntityMarker.None;

            bool offers = false;
            foreach (var quest in DataManager.Quests.Values)
            {
                if (quest.TurnInNpcTemplateId == npc.TemplateId
                    && player.Progress.GetQuestState(quest.Id) == QuestState.ReadyToTurnIn)
                    return EntityMarker.QuestReady;

                if (quest.GiverNpcTemplateId == npc.TemplateId && QuestService.IsAvailable(player, quest))
                    offers = true;
            }
            return offers ? EntityMarker.QuestAvailable : EntityMarker.None;
        }
    }
}
