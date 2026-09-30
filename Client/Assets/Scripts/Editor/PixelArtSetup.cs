using UnityEditor;
using UnityEngine;

namespace Client.Editor
{
    public class PixelArtSetup
    {
        [MenuItem("Tools/Fix Pixel Art Settings")]
        public static void FixPixelArt()
        {
            // Find all textures in the Assets folder (you can restrict this to "Assets/Art" if you want)
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
            int fixedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                
                // Skip UI fonts and default packages
                if (path.Contains("TextMesh Pro") || path.Contains("Packages")) continue;

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

                if (importer != null)
                {
                    bool changed = false;

                    // 1. Ensure it's a Sprite
                    if (importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        changed = true;
                    }

                    // 2. Point (No Filter) fixes the blur
                    if (importer.filterMode != FilterMode.Point)
                    {
                        importer.filterMode = FilterMode.Point;
                        changed = true;
                    }

                    // 3. Uncompressed fixes weird color artifacts
                    if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                    {
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        changed = true;
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        fixedCount++;
                    }
                }
            }

            Debug.Log($"[PixelArtSetup] Fixed {fixedCount} textures! They should look perfectly crisp now.");
        }
    }
}
