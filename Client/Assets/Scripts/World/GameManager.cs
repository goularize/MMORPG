using UnityEngine;
using System.Collections.Generic;
using Client.Network.Handlers;

namespace Client.World
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("State")]
        private GameObject _localPlayer;
        public Dictionary<int, GameObject> SpawnedEntities { get; private set; } = new Dictionary<int, GameObject>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            LoadMap(CharacterHandler.CurrentMapId);
        }

        private void LoadMap(int mapId)
        {
            // Map the Server's MapId to the Unity Scene Name
            string sceneName = "01_StartingVillage"; 
            
            // In the future, you can add more maps like: if (mapId == 2) sceneName = "02_Forest";

            Debug.Log($"[GameManager] Loading Map Scene: {sceneName} for Map ID: {mapId}");
            
            // Load the map dynamically in the background, merging it with the GameScene
            var loadOp = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            
            StartCoroutine(TrackMapLoad(loadOp));
        }

        private System.Collections.IEnumerator TrackMapLoad(AsyncOperation loadOp)
        {
            while (!loadOp.isDone)
            {
                if (Client.UI.LoadingScreenUI.Instance != null)
                {
                    Client.UI.LoadingScreenUI.Instance.UpdateProgress(loadOp.progress, "Loading Map Assets...");
                }
                yield return null;
            }

            if (Client.UI.LoadingScreenUI.Instance != null)
            {
                Client.UI.LoadingScreenUI.Instance.UpdateProgress(1f, "Synchronizing World State...");
            }

            SpawnLocalPlayer();

            // The server's initial state (entity spawns, vitals, positions) was held while the scene loaded; handle it
            // now that the local player exists. A WorldReady handshake (#177) would make the server wait instead.
            Network.NetworkManager.Instance?.ReleaseWorldPackets();

            if (Client.UI.LoadingScreenUI.Instance != null)
            {
                Client.UI.LoadingScreenUI.Instance.Hide();
            }
        }

        private void SpawnLocalPlayer()
        {
            GameObject dynamicPrefab = Resources.Load<GameObject>("Prefabs/Entities/Character");
            if (dynamicPrefab == null)
            {
                Debug.LogError("[GameManager] Character prefab could not be loaded from Resources!");
                return;
            }

            Vector3 spawnPos = CharacterHandler.SpawnPosition;
            _localPlayer = Instantiate(dynamicPrefab, spawnPos, Quaternion.identity);
            _localPlayer.name = "LocalPlayer";

            // Initialize visual state via the EntityManager
            var entityManager = _localPlayer.GetComponent<EntityManager>();
            if (entityManager != null) entityManager.SetName(Client.UI.CharacterSelectionUI.SelectedCharacterName);
            
            // Set the camera to follow the player
            Camera.main.transform.SetParent(_localPlayer.transform);
            Camera.main.transform.localPosition = new Vector3(0, 0, -10);

            Debug.Log($"[GameManager] Spawned local player at {spawnPos} for Map {CharacterHandler.CurrentMapId}");
        }

        public void SpawnRemoteEntity(int entityId, Shared.Enums.EntityType type, string prefabName, string entityName, Vector3 pos)
        {
            if (SpawnedEntities.ContainsKey(entityId)) return;

            GameObject prefabToSpawn = Resources.Load<GameObject>($"Prefabs/Entities/{prefabName}");
            if (prefabToSpawn == null)
            {
                Debug.LogError($"[GameManager] Missing Prefab in Resources: {prefabName}. Falling back to Character prefab!");
                prefabToSpawn = Resources.Load<GameObject>("Prefabs/Entities/Character");
                if (prefabToSpawn == null) return;
            }

            GameObject newEntity = Instantiate(prefabToSpawn, pos, Quaternion.identity);
            newEntity.name = $"{type}_{entityId}_{entityName}";

            // Initialize visual state via the EntityManager (if it has one)
            var entityManager = newEntity.GetComponent<EntityManager>();
            if (entityManager != null) entityManager.SetName(entityName);

            // Disable local input control
            var controller = newEntity.GetComponent<PlayerController>();
            if (controller != null) controller.enabled = false;

            // Add and initialize network synchronization
            var netEntity = newEntity.GetComponent<NetworkEntity>();
            if (netEntity == null) netEntity = newEntity.AddComponent<NetworkEntity>();
            netEntity.Initialize(entityId, entityName, pos);
            netEntity.ConfigureCollision(type, MapRules.IsPvp(CharacterHandler.CurrentMapId));

            SpawnedEntities.Add(entityId, newEntity);
        }

        public void DespawnRemoteEntity(int entityId)
        {
            if (SpawnedEntities.TryGetValue(entityId, out GameObject obj))
            {
                Destroy(obj);
                SpawnedEntities.Remove(entityId);
            }
        }

        public void UpdateEntityPosition(int entityId, Vector3 newPos)
        {
            if (SpawnedEntities.TryGetValue(entityId, out GameObject obj))
            {
                var netEntity = obj.GetComponent<NetworkEntity>();
                if (netEntity != null) netEntity.UpdateTargetPosition(newPos);
            }
            else if (entityId == CharacterHandler.LocalPlayerId && _localPlayer != null)
            {
                // Server correction for our own character (anti-cheat rubberband)
                _localPlayer.transform.position = newPos;
                Debug.Log($"[GameManager] Rubberbanded local player to {newPos}");
            }
            // Any other unknown id belongs to an entity we have not spawned (yet): ignore it, never move the player
        }

        /// <summary>
        /// Returns the EntityManager of a known entity (remote or the local player), or null.
        /// Unlike GetEntity it never falls back to the local player for unknown ids.
        /// </summary>
        public EntityManager GetEntityManager(int entityId)
        {
            if (SpawnedEntities.TryGetValue(entityId, out GameObject obj))
            {
                return obj != null ? obj.GetComponent<EntityManager>() : null;
            }

            if (_localPlayer != null && entityId == CharacterHandler.LocalPlayerId)
            {
                return _localPlayer.GetComponent<EntityManager>();
            }

            return null;
        }

        public GameObject GetEntity(int entityId)
        {
            if (SpawnedEntities.TryGetValue(entityId, out GameObject obj))
            {
                return obj;
            }
            // The server doesn't send EntitySpawn for ourselves, so our own id is not in SpawnedEntities.
            // Any other unknown id is an entity we have not spawned: null, never the local player.
            return entityId == CharacterHandler.LocalPlayerId ? _localPlayer : null;
        }
    }
}
