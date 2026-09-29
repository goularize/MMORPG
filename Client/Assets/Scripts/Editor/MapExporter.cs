using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Client.Editor
{
    [Serializable]
    public class Vector2Data
    {
        public float x;
        public float y;

        public Vector2Data(Vector2 v)
        {
            x = v.x;
            y = v.y;
        }
    }

    [Serializable]
    public class ColliderExportData
    {
        public string Type;
        public Vector2Data Position;
        
        // Box
        public Vector2Data Size;
        
        // Circle
        public float Radius;
        
        // Polygon
        public List<Vector2Data> Vertices;
    }

    [Serializable]
    public class ResourceSpawnerExportData
    {
        public string Type;
        public Vector2Data Position;
        public float RespawnTimeSeconds;
    }

    [Serializable]
    public class MapExportData
    {
        public string MapName;
        public int MaxPlayers;
        public List<ColliderExportData> Colliders = new List<ColliderExportData>();
        public List<ResourceSpawnerExportData> ResourceSpawners = new List<ResourceSpawnerExportData>();
    }

    public class MapExporter
    {
        [MenuItem("MMORPG/Export Map Data")]
        public static void ExportMapData()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            string sceneName = activeScene.name;

            // Basic parsing of MapId from Scene name if it follows "Map_X" convention, otherwise default to 1.
            int mapId = 1;
            if (sceneName.StartsWith("Map_"))
            {
                int.TryParse(sceneName.Substring(4), out mapId);
            }

            MapExportData mapData = new MapExportData
            {
                MapName = sceneName,
                MaxPlayers = 100 // Default max players
            };

            // Find all 2D Colliders in the scene
            Collider2D[] colliders = UnityEngine.Object.FindObjectsOfType<Collider2D>();
            
            foreach (Collider2D col in colliders)
            {
                // Skip triggers if you only want solid walls, or keep them if you need portal zones.
                // For Phase 3, we export everything.
                ColliderExportData colData = new ColliderExportData
                {
                    Position = new Vector2Data(col.transform.position)
                };

                if (col is BoxCollider2D box)
                {
                    colData.Type = "Box";
                    colData.Size = new Vector2Data(Vector2.Scale(box.size, box.transform.lossyScale));
                    colData.Position = new Vector2Data((Vector2)box.transform.position + box.offset);
                }
                else if (col is CircleCollider2D circle)
                {
                    colData.Type = "Circle";
                    float scaleX = Mathf.Abs(circle.transform.lossyScale.x);
                    float scaleY = Mathf.Abs(circle.transform.lossyScale.y);
                    float maxScale = Mathf.Max(scaleX, scaleY);
                    colData.Radius = circle.radius * maxScale;
                    colData.Position = new Vector2Data((Vector2)circle.transform.position + circle.offset);
                }
                else if (col is PolygonCollider2D poly)
                {
                    colData.Type = "Polygon";
                    colData.Vertices = new List<Vector2Data>();
                    colData.Position = new Vector2Data((Vector2)poly.transform.position + poly.offset);

                    foreach (Vector2 point in poly.points)
                    {
                        // Apply scale to points
                        Vector2 scaledPoint = Vector2.Scale(point, poly.transform.lossyScale);
                        colData.Vertices.Add(new Vector2Data(scaledPoint));
                    }
                }
                else
                {
                    // Skip unsupported colliders (like EdgeCollider2D)
                    continue;
                }

                mapData.Colliders.Add(colData);
            }

            // Find all ResourceSpawners
            Client.Map.ResourceSpawner[] spawners = UnityEngine.Object.FindObjectsOfType<Client.Map.ResourceSpawner>();
            foreach (var spawner in spawners)
            {
                mapData.ResourceSpawners.Add(new ResourceSpawnerExportData
                {
                    Type = spawner.ResourceType,
                    Position = new Vector2Data(spawner.transform.position),
                    RespawnTimeSeconds = spawner.RespawnTimeSeconds
                });
            }

            // Convert to JSON
            string json = JsonUtility.ToJson(mapData, true);

            // Save to file
            // Let's save it directly to the Server's Data folder so it can be loaded later.
            string projectRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            string exportDir = Path.Combine(projectRoot, "Server", "Data", "Maps");
            
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            string filePath = Path.Combine(exportDir, $"Map_{mapId}_Config.json");
            File.WriteAllText(filePath, json);

            Debug.Log($"[MapExporter] Successfully exported map data to: {filePath}");
            EditorUtility.DisplayDialog("Export Complete", $"Exported {mapData.Colliders.Count} colliders to Map_{mapId}_Config.json", "OK");
        }
    }
}
