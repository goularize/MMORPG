using UnityEngine;
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

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            SpawnLocalPlayer();
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
            
            // Set the camera to follow the player
            Camera.main.transform.SetParent(_localPlayer.transform);
            Camera.main.transform.localPosition = new Vector3(0, 0, -10);

            Debug.Log($"[GameManager] Spawned local player at {spawnPos} for Map {CharacterHandler.CurrentMapId}");
        }
    }
}
