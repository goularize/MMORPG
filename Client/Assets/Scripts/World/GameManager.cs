using UnityEngine;
using System.Collections.Generic;
using Client.Network.Handlers;

namespace Client.World
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Prefabs")]
        public GameObject playerPrefab;
        
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
            
            // Once the map is fully loaded, spawn the player on it!
            loadOp.completed += (asyncOp) => 
            {
                SpawnLocalPlayer();
            };
        }

        private void SpawnLocalPlayer()
        {
            if (playerPrefab == null)
            {
                Debug.LogError("[GameManager] Player prefab is not assigned!");
                return;
            }

            Vector3 spawnPos = CharacterHandler.SpawnPosition;
            _localPlayer = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
            _localPlayer.name = "LocalPlayer";

            // Initialize visual state via the CharacterManager
            var charManager = _localPlayer.GetComponent<CharacterManager>();
            if (charManager != null) charManager.SetName(Client.UI.CharacterSelectionUI.SelectedCharacterName);
            
            // Set the camera to follow the player
            Camera.main.transform.SetParent(_localPlayer.transform);
            Camera.main.transform.localPosition = new Vector3(0, 0, -10);

            Debug.Log($"[GameManager] Spawned local player at {spawnPos} for Map {CharacterHandler.CurrentMapId}");
        }

        public void SpawnRemoteEntity(int entityId, string entityName, Vector3 pos)
        {
            if (SpawnedEntities.ContainsKey(entityId)) return;

            GameObject newEntity = Instantiate(playerPrefab, pos, Quaternion.identity);
            newEntity.name = $"Remote_{entityId}_{entityName}";

            // Initialize visual state via the CharacterManager
            var charManager = newEntity.GetComponent<CharacterManager>();
            if (charManager != null) charManager.SetName(entityName);

            // Disable local input control
            var controller = newEntity.GetComponent<PlayerController>();
            if (controller != null) controller.enabled = false;

            // Add and initialize network synchronization
            var netEntity = newEntity.AddComponent<NetworkEntity>();
            netEntity.Initialize(entityId, entityName, pos);

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
            else
            {
                // If it's not in SpawnedEntities, it's likely a rubberband packet for our Local Player
                if (_localPlayer != null)
                {
                    _localPlayer.transform.position = newPos;
                    Debug.Log($"[GameManager] Rubberbanded local player to {newPos}");
                }
            }
        }

        public GameObject GetEntity(int entityId)
        {
            if (SpawnedEntities.TryGetValue(entityId, out GameObject obj))
            {
                return obj;
            }
            // Fallback: If it's not a remote entity, assume it's the local player.
            // (Since the server doesn't send EntitySpawn for ourselves, our ID is not in SpawnedEntities)
            return _localPlayer;
        }
    }
}
