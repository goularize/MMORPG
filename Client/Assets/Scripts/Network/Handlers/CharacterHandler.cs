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

        public static void HandleSelectResponse(Packet packet)
        {
            bool success = packet.ReadBool();
            if (success)
            {
                float x = packet.ReadFloat();
                float y = packet.ReadFloat();
                float z = packet.ReadFloat();
                
                Debug.Log($"[CharacterHandler] Selection successful. Spawning at {x}, {y}, {z}");
                
                // TODO: Load Game Scene and prepare to receive EntitySpawn and initial stats
                // UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
            }
            else
            {
                Debug.LogError("[CharacterHandler] Failed to select character.");
            }
        }
    }
}
