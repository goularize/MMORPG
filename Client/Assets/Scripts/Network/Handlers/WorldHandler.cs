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
            var pos = packet.ReadVector3();
            Vector3 unityPos = new Vector3(pos.X, pos.Y, pos.Z);

            Client.World.GameManager.Instance?.UpdateEntityPosition(entityId, unityPos);
        }

        public static void HandleVitalsUpdate(Packet packet)
        {
            int entityId = packet.ReadInt();
            int health = packet.ReadInt();
            int mana = packet.ReadInt();
        }

        public static void HandleEntitySpawn(Packet packet)
        {
            int entityId = packet.ReadInt();
            Shared.Enums.EntityType type = (Shared.Enums.EntityType)packet.ReadByte();
            string prefabName = packet.ReadString();
            string name = packet.ReadString();
            var pos = packet.ReadVector3();
            Vector3 unityPos = new Vector3(pos.X, pos.Y, pos.Z);

            Client.World.GameManager.Instance?.SpawnRemoteEntity(entityId, type, prefabName, name, unityPos);
        }

        public static void HandleEntityDespawn(Packet packet)
        {
            int entityId = packet.ReadInt();
            Client.World.GameManager.Instance?.DespawnRemoteEntity(entityId);
        }

        public static void HandleEntityCombatEvent(Packet packet)
        {
            int attackerId = packet.ReadInt();
            int targetId = packet.ReadInt();
            int damage = packet.ReadInt();
            bool isCrit = packet.ReadBool();
            bool isDodge = packet.ReadBool();

            GameObject attackerObj = Client.World.GameManager.Instance?.GetEntity(attackerId);
            GameObject targetObj = Client.World.GameManager.Instance?.GetEntity(targetId);

            if (attackerObj != null)
            {
                var entityManager = attackerObj.GetComponent<Client.World.EntityManager>();
                if (entityManager != null) entityManager.TriggerAttack();
            }

            if (targetObj != null)
            {
                var entityManager = targetObj.GetComponent<Client.World.EntityManager>();
                if (entityManager != null)
                {
                    if (!isDodge) entityManager.TriggerHit();
                    entityManager.ShowFloatingText(damage, isCrit, isDodge);
                }
            }
        }
    }
}
