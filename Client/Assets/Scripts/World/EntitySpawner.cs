using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Client.World
{
    public class EntitySpawner : MonoBehaviour
    {
        [Header("Spawner Configuration")]
        [Tooltip("The ID of the NPC/Enemy from Npcs.json")]
        public int templateId = 100;
        
        [Tooltip("Number of entities to spawn")]
        public int amount = 1;
        
        [Tooltip("Is this an Area spawn or a specific Point?")]
        public bool isArea = true;
        
        [Tooltip("Wander/Spawn radius (only used if isArea is true)")]
        public float radius = 5f;

        [Header("Editor Visuals")]
        public Color gizmoColor = new Color(1f, 0f, 0f, 0.3f);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // Draw a translucent colored sphere for the area
            Gizmos.color = gizmoColor;
            
            if (isArea && radius > 0)
            {
                Gizmos.DrawSphere(transform.position, radius);
                
                // Draw a solid wireframe border to make it pop
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
                Gizmos.DrawWireSphere(transform.position, radius);
            }
            else
            {
                // If it's a point spawn, just draw a little box
                Gizmos.DrawCube(transform.position, new Vector3(1, 1, 1));
            }

            // Draw the Template ID above it so you know what it is!
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 14;
            style.fontStyle = FontStyle.Bold;
            Handles.Label(transform.position + Vector3.up * 1.5f, $"Spawner\nID: {templateId} (x{amount})", style);
        }
#endif
    }
}
