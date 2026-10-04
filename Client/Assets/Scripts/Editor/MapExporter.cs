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

        [MenuItem("MMORPG/Export Spawners")]
        public static void ExportSpawners()
        {
            Client.World.EntitySpawner[] spawners = FindObjectsOfType<Client.World.EntitySpawner>();
            
            if (spawners.Length == 0)
            {
                EditorUtility.DisplayDialog("Warning", "No EntitySpawner components found in the scene!", "OK");
                return;
            }

            // Build JSON manually or use wrapper to match Server format
            List<object> spawnerList = new List<object>();
            foreach (var s in spawners)
            {
                spawnerList.Add(new {
                    Type = s.isArea ? "Area" : "Point",
                    TemplateId = s.templateId,
                    X = s.transform.position.x,
                    Y = s.transform.position.y,
                    Z = s.transform.position.z, // Mapping Z
                    Radius = s.isArea ? s.radius : 0f,
                    Amount = s.amount,
                    RotationY = s.transform.eulerAngles.y
                });
            }

            // Use Newtonsoft or standard Unity JsonUtility (Unity JsonUtility can't do top-level lists easily, so we manually format or use wrapper)
            // A simple JSON builder since it's just a flat array
            string json = "[\n";
            for(int i=0; i<spawners.Length; i++)
            {
                var s = spawners[i];
                string typeStr = s.isArea ? "Area" : "Point";
                json += "  {\n";
                json += $"    \"Type\": \"{typeStr}\",\n";
                json += $"    \"TemplateId\": {s.templateId},\n";
                json += $"    \"X\": {s.transform.position.x.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n";
                json += $"    \"Y\": {s.transform.position.y.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n";
                json += $"    \"Z\": {s.transform.position.z.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n";
                json += $"    \"Radius\": {s.radius.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n";
                json += $"    \"Amount\": {s.amount},\n";
                json += $"    \"RotationY\": {s.transform.eulerAngles.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n";
                json += "  }" + (i < spawners.Length - 1 ? "," : "") + "\n";
            }
            json += "]";

            // Automatically find the Server/Data path!
            string projectRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            string serverDataPath = Path.Combine(projectRoot, "Server", "Data", "Spawners.json");

            if (File.Exists(serverDataPath))
            {
                File.WriteAllText(serverDataPath, json);
                Debug.Log($"[MapExporter] Successfully exported {spawners.Length} spawners directly to {serverDataPath}");
                EditorUtility.DisplayDialog("Success!", $"Exported {spawners.Length} spawners directly to the Server's Data folder!\n\nRestart the Server to see them.", "Awesome");
            }
            else
            {
                string savePath = EditorUtility.SaveFilePanel("Export Spawners", "", "Spawners.json", "json");
                if (!string.IsNullOrEmpty(savePath))
                {
                    File.WriteAllText(savePath, json);
                    EditorUtility.DisplayDialog("Success", "Exported Spawners.json", "OK");
                }
            }
        }
    }
}
