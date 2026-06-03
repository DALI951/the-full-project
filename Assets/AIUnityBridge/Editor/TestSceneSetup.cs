using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TestSceneSetup
{
    [MenuItem("Tools/AI Bridge/Setup Test Scene (Floating Deer)")]
    public static void SetupTestScene()
    {
        var deerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Deer/Deer_Walk_Only.glb");
        if (deerPrefab == null)
        {
            deerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Deer/Deer_Quaternius.glb");
        }

        if (deerPrefab == null)
        {
            Debug.LogError("[AIBridge] Deer model not found in Assets/Models/Deer/");
            return;
        }

        var deer = (GameObject)PrefabUtility.InstantiatePrefab(deerPrefab);
        deer.name = "FloatingDeer_Test";

        deer.transform.position = new Vector3(0, 3f, 0);
        deer.transform.rotation = Quaternion.identity;
        deer.transform.localScale = Vector3.one;

        if (deer.GetComponent<Animator>() == null)
        {
            var animator = deer.AddComponent<Animator>();
            var deerController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/ressources/Animals/lowpolydeer/DeerAnimator.controller");
            if (deerController != null)
                animator.runtimeAnimatorController = deerController;
        }

        Selection.activeGameObject = deer;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"[AIBridge] Test deer spawned at position (0, 3, 0) — floating above ground!");
        Debug.Log($"[AIBridge] Object instance ID: {deer.GetInstanceID()}");
        Debug.Log($"[AIBridge] Use the AI Bridge to detect and fix this floating object.");
    }

    [MenuItem("Tools/AI Bridge/Setup Test Scene (Floating Deer)", true)]
    public static bool ValidateSetupTestScene()
    {
        return !EditorApplication.isPlaying;
    }
}
