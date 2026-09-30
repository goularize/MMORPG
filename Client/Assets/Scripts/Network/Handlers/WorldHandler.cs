using UnityEngine;
using Shared.Network;

namespace Client.Network.Handlers
{
    public static class WorldHandler
    {
        public static void HandleStatsUpdate(Packet packet)
        {
            int maxHealth = packet.ReadInt();
            int maxMana = packet.ReadInt();
            int attack = packet.ReadInt();
            int magicAttack = packet.ReadInt();
            int defense = packet.ReadInt();
            int magicDefense = packet.ReadInt();

            Debug.Log($"[WorldHandler] Stats Update -> MaxHP: {maxHealth}, MaxMP: {maxMana}, Atk: {attack}, Def: {defense}");
        }

        public static void HandleEntityPositionUpdate(Packet packet)
        {
            int entityId = packet.ReadInt();
            float x = packet.ReadFloat();
            float y = packet.ReadFloat();
            float z = packet.ReadFloat();

            // When the server rubberbands us or when other players move
            Debug.Log($"[WorldHandler] EntityPositionUpdate: Entity {entityId} moved to {x}, {y}, {z}");
        }

        public static void HandleVitalsUpdate(Packet packet)
        {
            int entityId = packet.ReadInt();
            int health = packet.ReadInt();
            int mana = packet.ReadInt();

            // We don't need to log this every time, it happens often (regen)
            // Debug.Log($"[WorldHandler] VitalsUpdate: Entity {entityId} HP:{health} MP:{mana}");
        }

        public static void HandleEntitySpawn(Packet packet)
        {
            int entityId = packet.ReadInt();
            // Typically includes type, appearance, position, etc.
            Debug.Log($"[WorldHandler] EntitySpawn: Entity {entityId} spawned.");
        }
    }
}
