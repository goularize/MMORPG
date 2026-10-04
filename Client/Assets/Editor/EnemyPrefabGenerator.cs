using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using TMPro;
using Client.World;

public class EnemyPrefabGenerator
{
    [MenuItem("Tools/MMORPG/Generate Enemy Prefab")]
    [MenuItem("Assets/MMORPG/Generate Enemy Prefab", false, 10)]
    public static void GenerateEnemyPrefab()
    {
        // 1. Get the selected texture
        Texture2D selectedTexture = Selection.activeObject as Texture2D;
        if (selectedTexture == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a Texture/Sprite in the Project window first!", "OK");
            return;
        }

        string spritePath = AssetDatabase.GetAssetPath(selectedTexture);
        string enemyName = selectedTexture.name;
        string prefabFolder = "Assets/Resources/Prefabs/Entities";
        string prefabPath = $"{prefabFolder}/Enemy_{enemyName}.prefab";
        
        string animationsFolder = "Assets/Animations/Enemies";
        if (!AssetDatabase.IsValidFolder(animationsFolder))
        {
            System.IO.Directory.CreateDirectory(Application.dataPath + "/Animations/Enemies");
            AssetDatabase.Refresh();
        }
        string controllerPath = $"{animationsFolder}/{enemyName}_Controller.controller";

        // 2. Create the Root GameObject
        GameObject rootGo = new GameObject($"Enemy_{enemyName}");

        // Add Root Components (Rigidbody2D, BoxCollider2D, NetworkEntity, EntityManager)
        Rigidbody2D rb = rootGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic; // Enemies are moved by server!
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        BoxCollider2D col = rootGo.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.8f, 0.8f);

        rootGo.AddComponent<NetworkEntity>();
        EntityManager entityManager = rootGo.AddComponent<EntityManager>();

        // 3. Create the Sprite Child
        GameObject spriteGo = new GameObject("Sprite");
        spriteGo.transform.SetParent(rootGo.transform);
        spriteGo.transform.localPosition = Vector3.zero;

        SpriteRenderer sr = spriteGo.AddComponent<SpriteRenderer>();
        Object[] sprites = AssetDatabase.LoadAllAssetsAtPath(spritePath);
        if (sprites != null && sprites.Length > 1)
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] is Sprite spr) { sr.sprite = spr; break; }
            }
        }
        else if (sprites != null && sprites.Length == 1 && sprites[0] is Sprite singleSpr)
        {
            sr.sprite = singleSpr;
        }
        sr.sortingOrder = 5;

        // Create Animator Controller
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

        Animator animator = spriteGo.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        entityManager.animator = animator;

        // 4. Create Name Text Child
        GameObject textGo = new GameObject("EntityName");
        textGo.transform.SetParent(rootGo.transform);
        textGo.transform.localPosition = new Vector3(0, 1.2f, 0); // Above the head
        
        TextMeshPro tmp = textGo.AddComponent<TextMeshPro>();
        tmp.text = $"{enemyName} Lv. 1";
        tmp.fontSize = 2.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.sortingOrder = 10;
        RectTransform rt = textGo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(5, 1);

        entityManager.nameText = tmp;

        // 5. Save as Prefab
        if (!AssetDatabase.IsValidFolder(prefabFolder))
        {
            System.IO.Directory.CreateDirectory(Application.dataPath + "/Resources/Prefabs/Entities");
            AssetDatabase.Refresh();
        }

        PrefabUtility.SaveAsPrefabAsset(rootGo, prefabPath);
        Object.DestroyImmediate(rootGo);

        Debug.Log($"Successfully generated {enemyName} prefab at {prefabPath}!");
        GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        EditorGUIUtility.PingObject(savedPrefab);
    }
}
