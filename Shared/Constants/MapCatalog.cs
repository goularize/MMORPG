using System.Collections.Generic;

namespace Shared.Constants
{
    /// <summary>
    /// Which Unity scene shows which server map. The server's map files are named "&lt;id&gt;_&lt;Name&gt;.json" and the
    /// scene carries the same name, so adding a map means adding one entry here (a test checks every server map is listed).
    /// </summary>
    public static class MapCatalog
    {
        private static readonly Dictionary<int, string> SceneNames = new()
        {
            [1] = "01_StartingVillage",
        };

        public static IReadOnlyDictionary<int, string> All => SceneNames;

        public static bool TryGetSceneName(int mapId, out string sceneName)
        {
            if (SceneNames.TryGetValue(mapId, out var name))
            {
                sceneName = name;
                return true;
            }

            sceneName = string.Empty;
            return false;
        }
    }
}
