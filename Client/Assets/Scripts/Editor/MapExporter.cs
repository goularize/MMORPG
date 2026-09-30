using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;

namespace Client.Editor
{
    // Wrapper classes so Unity's JsonUtility can serialize them properly
    [System.Serializable]
    public class MapExportData
    {
        public string MapName;
        public List<PolygonData> Colliders = new List<PolygonData>();
    }

    [System.Serializable]
    public class PolygonData
    {
        public List<Vector2> Points = new List<Vector2>();
    }

    public class MapExporter : EditorWindow
    {
        [MenuItem("MMORPG/Export Current Map")]
        public static void ExportMap()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            
            MapExportData exportData = new MapExportData();
            exportData.MapName = activeScene.name;

            // Find all composite colliders in the map (our merged walls/trees)
            CompositeCollider2D[] colliders = FindObjectsOfType<CompositeCollider2D>();

            int totalPoints = 0;

            foreach (var comp in colliders)
            {
                // A single CompositeCollider can contain multiple separate disconnected polygons
                for (int i = 0; i < comp.pathCount; i++)
                {
                    Vector2[] pathPoints = new Vector2[comp.GetPathPointCount(i)];
                    comp.GetPath(i, pathPoints);

                    PolygonData poly = new PolygonData();
                    foreach (Vector2 pt in pathPoints)
                    {
                        // Convert local tilemap coordinates to global world coordinates
                        Vector2 worldPt = comp.transform.TransformPoint(pt);
                        
                        // Rounding to 3 decimal places to keep the JSON file size small
                        worldPt.x = (float)System.Math.Round(worldPt.x, 3);
                        worldPt.y = (float)System.Math.Round(worldPt.y, 3);
                        
                        poly.Points.Add(worldPt);
                        totalPoints++;
                    }
                    exportData.Colliders.Add(poly);
                }
            }

            if (exportData.Colliders.Count == 0)
            {
                EditorUtility.DisplayDialog("Export Failed", "Could not find any CompositeCollider2D in the scene. Make sure your walls have physics applied!", "OK");
                return;
            }

            // Convert our data to a beautiful JSON string
            string json = JsonUtility.ToJson(exportData, true);

            // Ask the user where to save the file
            string savePath = EditorUtility.SaveFilePanel(
                "Export Map Data for Server",
                "",
                $"{activeScene.name}.json",
                "json"
            );

            if (!string.IsNullOrEmpty(savePath))
            {
                File.WriteAllText(savePath, json);
                Debug.Log($"[MapExporter] Successfully exported {exportData.Colliders.Count} polygons ({totalPoints} total points) for {activeScene.name}!");
                EditorUtility.DisplayDialog("Success!", $"Exported {exportData.Colliders.Count} polygons to {savePath}.\n\nThis file is ready for the .NET Server!", "Awesome");
            }
        }
    }
}
