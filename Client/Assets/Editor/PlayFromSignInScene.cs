using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MMORPG.Editor
{
    [InitializeOnLoad]
    public static class PlayFromSignInScene
    {
        private const string MenuPath = "Tools/Always Play From SignIn Scene";
        private const string ScenePath = "Assets/Scenes/SignInScene.unity";

        static PlayFromSignInScene()
        {
            // Delay call to ensure the menu system is ready before checking the menu item
            EditorApplication.delayCall += () => {
                Menu.SetChecked(MenuPath, EditorPrefs.GetBool(MenuPath, false));
            };
        }

        [MenuItem(MenuPath)]
        public static void ToggleAction()
        {
            bool isEnabled = EditorPrefs.GetBool(MenuPath, false);
            isEnabled = !isEnabled;
            
            EditorPrefs.SetBool(MenuPath, isEnabled);
            Menu.SetChecked(MenuPath, isEnabled);

            if (isEnabled)
            {
                SetPlayModeStartScene();
            }
            else
            {
                EditorSceneManager.playModeStartScene = null;
                Debug.Log("Play Mode Start Scene disabled. Unity will now play the currently active scene.");
            }
        }

        static void SetPlayModeStartScene()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset != null)
            {
                EditorSceneManager.playModeStartScene = sceneAsset;
                Debug.Log($"Play Mode Start Scene set to: {ScenePath}");
            }
            else
            {
                Debug.LogError($"Could not find SignInScene at path: {ScenePath}. Please check if the path is correct.");
                EditorPrefs.SetBool(MenuPath, false);
                Menu.SetChecked(MenuPath, false);
            }
        }

        [InitializeOnLoadMethod]
        static void AutoSetPlayModeStartScene()
        {
            if (EditorPrefs.GetBool(MenuPath, false))
            {
                SetPlayModeStartScene();
            }
            else
            {
                EditorSceneManager.playModeStartScene = null;
            }
        }
    }
}
