using UnityEngine;
using Shared.Network;

namespace Client.Network.Handlers
{
    public static class ProgressionHandler
    {
        public static void HandleExpUpdate(Packet packet)
        {
            int playerId = packet.ReadInt();
            long currentExp = packet.ReadLong();
            long expToNextLevel = packet.ReadLong();

            if (playerId == CharacterHandler.LocalPlayerId)
            {
                // Update Exp Bar
                if (Client.UI.ExpBarUI.Instance != null)
                {
                    Client.UI.ExpBarUI.Instance.UpdateExp(currentExp, expToNextLevel);
                }
            }
        }

        public static void HandleLevelUp(Packet packet)
        {
            int playerId = packet.ReadInt();
            int newLevel = packet.ReadInt();
            int newStatPoints = packet.ReadInt();

            if (playerId == CharacterHandler.LocalPlayerId)
            {
                // Update HUD Level Badge
                if (Client.UI.PlayerHUDUI.Instance != null)
                {
                    Client.UI.PlayerHUDUI.Instance.UpdateLevel(newLevel);
                }

                // Show Level Up VFX
                // TODO: Spawn particle effect
                Debug.Log($"[Progression] LEVEL UP! You are now level {newLevel}. Stat points available: {newStatPoints}");
            }
            else
            {
                // Remote player leveled up
                // TODO: Spawn particle effect on them
            }
        }

        public static void HandleStatPointResponse(Packet packet)
        {
            bool isSuccess = packet.ReadBool();
            byte statType = packet.ReadByte();
            int newStatValue = packet.ReadInt();
            int remainingStatPoints = packet.ReadInt();
            string errorMessage = packet.ReadString();

            if (isSuccess)
            {
                Debug.Log($"[Progression] Allocated point to {(Shared.Enums.StatType)statType}. New value: {newStatValue}. Remaining: {remainingStatPoints}");
                // Update Character Sheet UI (if it's open)
            }
            else
            {
                Debug.LogWarning($"[Progression] Failed to allocate stat: {errorMessage}");
            }
        }
    }
}
