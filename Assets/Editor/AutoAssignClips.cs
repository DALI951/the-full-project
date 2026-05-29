using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public static class AutoAssignClips
{
    [MenuItem("Tools/Assign Animator Clips")]
    public static void AssignClips()
    {
        // Get all FBX files in the project that might have animations
        string[] fbxGuids = AssetDatabase.FindAssets("t:Model");

        // Build a map: clip name -> clip asset
        Dictionary<string, List<AnimationClip>> clipMap = new Dictionary<string, List<AnimationClip>>();

        foreach (string guid in fbxGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                continue;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object asset in assets)
            {
                if (asset is AnimationClip clip)
                {
                    if (!clipMap.ContainsKey(clip.name))
                        clipMap[clip.name] = new List<AnimationClip>();
                    clipMap[clip.name].Add(clip);
                }
            }
        }

        // Now process all Animator Controllers
        string[] controllerGuids = AssetDatabase.FindAssets("t:AnimatorController");
        int assigned = 0;

        foreach (string guid in controllerGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) continue;

            UnityEngine.Debug.Log($"Processing: {path}");

            foreach (var layer in controller.layers)
            {
                foreach (var childState in layer.stateMachine.states)
                {
                    AnimatorState state = childState.state;
                    string stateName = state.name;

                    if (clipMap.TryGetValue(stateName, out List<AnimationClip> candidates))
                    {
                        AnimationClip chosen = candidates[0];
                        if (candidates.Count > 1)
                            chosen = candidates.OrderBy(c => c.name.Length).First();
                        state.motion = chosen;
                        assigned++;
                        UnityEngine.Debug.Log($"  {stateName} -> {AssetDatabase.GetAssetPath(chosen)}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"  No clip found for '{stateName}'");
                    }
                }
            }

            EditorUtility.SetDirty(controller);
        }

        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log($"Assigned {assigned} clips total");
    }
}
