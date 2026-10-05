using UnityEngine;
using Shared.Network;

namespace Client.Network.Handlers
{
    public static class ProgressionHandler
    {
        public static int Level { get; private set; } = 1;
        public static long CurrentExp { get; private set; } = 0;
        public static long ExpToNextLevel { get; private set; } = 100;
        public static int StatPoints { get; private set; } = 0;
        public static int Strength { get; private set; } = 10;
        public static int Intelligence { get; private set; } = 10;
        public static int Constitution { get; private set; } = 10;
        public static int Knowledge { get; private set; } = 10;

        public static event System.Action OnProgressionUpdated;

        public static void HandleProgressionSync(Packet packet)
        {
            Level = packet.ReadInt();
            CurrentExp = packet.ReadLong();
            ExpToNextLevel = packet.ReadLong();
            StatPoints = packet.ReadInt();
            Strength = packet.ReadInt();
            Intelligence = packet.ReadInt();
            Constitution = packet.ReadInt();
            Knowledge = packet.ReadInt();

            if (Client.UI.PlayerHUDUI.Instance != null)
            {
                Client.UI.PlayerHUDUI.Instance.UpdateLevel(Level);
            }

            if (Client.UI.ExpBarUI.Instance != null)
            {
                Client.UI.ExpBarUI.Instance.UpdateExp(CurrentExp, ExpToNextLevel);
            }

            OnProgressionUpdated?.Invoke();
            Debug.Log($"[ProgressionHandler] Synced Level {Level}, EXP: {CurrentExp}/{ExpToNextLevel}, Points: {StatPoints}, STR: {Strength}, INT: {Intelligence}, CON: {Constitution}, KNOW: {Knowledge}");
        }

        public static void HandleExpUpdate(Packet packet)
        {
            int playerId = packet.ReadInt();
            long currentExp = packet.ReadLong();
            long expToNextLevel = packet.ReadLong();

            if (playerId == CharacterHandler.LocalPlayerId)
            {
                CurrentExp = currentExp;
                ExpToNextLevel = expToNextLevel;

                // Update Exp Bar
                if (Client.UI.ExpBarUI.Instance != null)
                {
                    Client.UI.ExpBarUI.Instance.UpdateExp(currentExp, expToNextLevel);
                }

                OnProgressionUpdated?.Invoke();
            }
        }

        public static void HandleLevelUp(Packet packet)
        {
            int playerId = packet.ReadInt();
            int newLevel = packet.ReadInt();
            int newStatPoints = packet.ReadInt();

            if (playerId == CharacterHandler.LocalPlayerId)
            {
                Level = newLevel;
                StatPoints = newStatPoints;

                // Update HUD Level Badge
                if (Client.UI.PlayerHUDUI.Instance != null)
                {
                    Client.UI.PlayerHUDUI.Instance.UpdateLevel(newLevel);
                }

                OnProgressionUpdated?.Invoke();

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
                StatPoints = remainingStatPoints;
                switch ((Shared.Enums.StatType)statType)
                {
                    case Shared.Enums.StatType.Strength: Strength = newStatValue; break;
                    case Shared.Enums.StatType.Intelligence: Intelligence = newStatValue; break;
                    case Shared.Enums.StatType.Constitution: Constitution = newStatValue; break;
                    case Shared.Enums.StatType.Knowledge: Knowledge = newStatValue; break;
                }

                OnProgressionUpdated?.Invoke();
                Debug.Log($"[Progression] Allocated point to {(Shared.Enums.StatType)statType}. New value: {newStatValue}. Remaining: {remainingStatPoints}");
            }
            else
            {
                Debug.LogWarning($"[Progression] Failed to allocate stat: {errorMessage}");
            }
        }
    }
}
