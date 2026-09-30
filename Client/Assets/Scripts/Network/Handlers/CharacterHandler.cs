using System;
using System.Collections.Generic;
using UnityEngine;
using Shared.Network;

namespace Client.Network.Handlers
{
    public class CharacterData
    {
        public int Id;
        public string Name;
        public int AppearanceId;
        public int Level;
    }

    public static class CharacterHandler
    {
        public static void HandleListResponse(Packet packet)
        {
            int count = packet.ReadInt();
            List<CharacterData> characters = new List<CharacterData>();
            for (int i = 0; i < count; i++)
            {
                characters.Add(new CharacterData
                {
                    Id = packet.ReadInt(),
                    Name = packet.ReadString(),
                    AppearanceId = packet.ReadInt(),
                    Level = packet.ReadInt()
                });
            }
            
            Debug.Log($"[CharacterHandler] Received {count} characters from server.");
            UI.CharacterSelectionUI.Instance?.OnCharacterListReceived(characters);
        }

        public static void HandleCreateResponse(Packet packet)
        {
            bool success = packet.ReadBool();
            string message = packet.ReadString();
            
            Debug.Log($"[CharacterHandler] Create Response: {success} - {message}");
            UI.CharacterCreationUI.Instance?.OnCharacterCreateResponse(success, message);
        }

        public static void HandleDeleteResponse(Packet packet)
        {
            bool success = packet.ReadBool();
            
            Debug.Log($"[CharacterHandler] Delete Response: {success}");
            UI.CharacterSelectionUI.Instance?.OnCharacterDeleteResponse(success);
        }

        public static int CurrentMapId { get; private set; }
        public static Vector3 SpawnPosition { get; private set; }

        public static void HandleSelectResponse(Packet packet)
        {
            bool success = packet.ReadBool();
            if (success)
            {
                int mapId = packet.ReadInt();
                float x = packet.ReadFloat();
                float y = packet.ReadFloat();
                float z = packet.ReadFloat();
                
                CurrentMapId = mapId;
                SpawnPosition = new Vector3(x, y, z);

                Debug.Log($"[CharacterHandler] Selection successful. Loading MapId {mapId}. Spawning at {x}, {y}, {z}");
                
                // Load the main game scene
                UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
            }
            else
            {
                Debug.LogError("[CharacterHandler] Failed to select character.");
            }
        }
    }
}
