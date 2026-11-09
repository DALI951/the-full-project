using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class AutoAssignClips
{
    private static readonly Dictionary<string, string[]> ControllerFolderMap = new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase)
    {
        { "CavalryAnimator", new[] { "Assets/Models/Horse" } },
        { "DeerAnimator",    new[] { "Assets/Models/Deer", "Assets/ressources/Animals/lowpolydeer" } },
        { "SoldierAnimator", new[] { "Assets/ressources/infantry" } },
        { "VillagerAnimator", new[] { "Assets/ressources/villager" } }
    };

    static AutoAssignClips()
    {
        EditorApplication.delayCall += () =>
        {
            int fixedCount = EnsureAllInstantLocomotionTransitions();
            if (fixedCount > 0)
            {
                Debug.Log($"[AutoAssignClips] Automatically enforced hasExitTime=false on {fixedCount} locomotion transitions.");
            }
        };
    }

    [MenuItem("Tools/Assign Animator Clips")]
    public static void AssignClips()
    {
        string[] controllerGuids = AssetDatabase.FindAssets("t:AnimatorController");
        int assigned = 0;
        int transitionsFixed = 0;

        foreach (string guid in controllerGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) continue;

            string ctrlName = controller.name;
            if (!ControllerFolderMap.TryGetValue(ctrlName, out string[] searchFolders))
            {
                // If not in specific map, search only within controller's own directory to avoid cross-pollution
                string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                searchFolders = new[] { folder };
            }

            Debug.Log($"[AutoAssignClips] Processing '{ctrlName}' at {path} with folders: {string.Join(", ", searchFolders)}");

            // Collect clips ONLY from the allowed folders for this controller
            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", searchFolders);
            List<AnimationClip> clips = new List<AnimationClip>();

            foreach (string fbxGuid in fbxGuids)
            {
                string fbxPath = AssetDatabase.GUIDToAssetPath(fbxGuid);
                if (!fbxPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                foreach (Object asset in assets)
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    {
                        clips.Add(clip);
                    }
                }
            }

            // Also search for standalone .anim clips in these folders
            string[] animGuids = AssetDatabase.FindAssets("t:AnimationClip", searchFolders);
            foreach (string aGuid in animGuids)
            {
                string aPath = AssetDatabase.GUIDToAssetPath(aGuid);
                if (aPath.EndsWith(".anim", System.StringComparison.OrdinalIgnoreCase))
                {
                    AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(aPath);
                    if (clip != null && !clips.Contains(clip))
                    {
                        clips.Add(clip);
                    }
                }
            }

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;
                foreach (var childState in layer.stateMachine.states)
                {
                    AnimatorState state = childState.state;
                    string stateName = state.name;

                    AnimationClip chosen = FindBestMatchingClip(stateName, clips);
                    if (chosen != null)
                    {
                        state.motion = chosen;
                        assigned++;
                        Debug.Log($"  {ctrlName}: {stateName} -> {chosen.name} ({AssetDatabase.GetAssetPath(chosen)})");
                    }
                    else
                    {
                        Debug.LogWarning($"  {ctrlName}: No matching clip found for '{stateName}'");
                    }
                }
            }

            // Ensure all locomotion transitions (Speed > 0.1, Speed < 0.1, IsMoving, IsWalking) have hasExitTime = false
            transitionsFixed += EnsureInstantLocomotionTransitions(controller);

            EditorUtility.SetDirty(controller);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[AutoAssignClips] Assigned {assigned} clips total. Fixed {transitionsFixed} locomotion transitions (hasExitTime = false).");
    }

    [MenuItem("Tools/Ensure Instant Locomotion Transitions")]
    public static void EnsureInstantLocomotionTransitionsMenu()
    {
        int totalFixed = EnsureAllInstantLocomotionTransitions();
        Debug.Log($"[AutoAssignClips] Finished transition check. Fixed {totalFixed} locomotion transitions with hasExitTime = false.");
    }

    /// <summary>
    /// Checks all AnimatorControllers in the project and sets hasExitTime = false for locomotion transitions.
    /// </summary>
    public static int EnsureAllInstantLocomotionTransitions()
    {
        string[] controllerGuids = AssetDatabase.FindAssets("t:AnimatorController");
        int totalFixed = 0;

        foreach (string guid in controllerGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) continue;

            int fixedCount = EnsureInstantLocomotionTransitions(controller);
            if (fixedCount > 0)
            {
                totalFixed += fixedCount;
                EditorUtility.SetDirty(controller);
            }
        }

        if (totalFixed > 0)
        {
            AssetDatabase.SaveAssets();
        }

        return totalFixed;
    }

    /// <summary>
    /// Recursively checks all state machine transitions and enforces hasExitTime = false
    /// for locomotion transitions (Speed > 0.1, Speed < 0.1, IsMoving, IsWalking, and Idle<->Walk).
    /// </summary>
    public static int EnsureInstantLocomotionTransitions(AnimatorController controller)
    {
        if (controller == null) return 0;
        int count = 0;

        foreach (var layer in controller.layers)
        {
            if (layer.stateMachine != null)
                count += ProcessStateMachineTransitions(controller.name, layer.stateMachine);
        }

        return count;
    }

    private static int ProcessStateMachineTransitions(string ctrlName, AnimatorStateMachine sm)
    {
        if (sm == null) return 0;
        int count = 0;

        // 1. Process transitions from child states
        foreach (var childState in sm.states)
        {
            var state = childState.state;
            if (state == null) continue;

            foreach (var trans in state.transitions)
            {
                string dstName = trans.destinationState != null ? trans.destinationState.name : "";
                if (IsLocomotionTransition(trans, state.name, dstName))
                {
                    if (trans.hasExitTime)
                    {
                        trans.hasExitTime = false;
                        count++;
                        Debug.Log($"  [AutoAssignClips] {ctrlName}: Set hasExitTime = false on '{state.name}' -> '{dstName}'");
                    }
                }
            }
        }

        // 2. Process AnyState transitions
        foreach (var trans in sm.anyStateTransitions)
        {
            string dstName = trans.destinationState != null ? trans.destinationState.name : "";
            if (IsLocomotionTransition(trans, "AnyState", dstName))
            {
                if (trans.hasExitTime)
                {
                    trans.hasExitTime = false;
                    count++;
                    Debug.Log($"  [AutoAssignClips] {ctrlName}: Set hasExitTime = false on AnyState -> '{dstName}'");
                }
            }
        }

        // 3. Recurse into sub-state machines
        foreach (var subSm in sm.stateMachines)
        {
            count += ProcessStateMachineTransitions(ctrlName, subSm.stateMachine);
        }

        return count;
    }

    private static bool IsLocomotionTransition(AnimatorStateTransition trans, string srcStateName, string dstStateName)
    {
        if (trans == null) return false;

        // Check conditions (Speed > 0.1, Speed < 0.1, IsMoving, IsWalking, etc.)
        foreach (var cond in trans.conditions)
        {
            string p = cond.parameter;
            if (p.Equals("Speed", System.StringComparison.OrdinalIgnoreCase) ||
                p.Equals("IsMoving", System.StringComparison.OrdinalIgnoreCase) ||
                p.Equals("IsWalking", System.StringComparison.OrdinalIgnoreCase) ||
                p.IndexOf("Move", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.IndexOf("Walk", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        // Check state names (Idle <-> Walk/Run/Gallop/Canter/Locomotion)
        bool srcIsMove = IsMovementState(srcStateName);
        bool dstIsMove = IsMovementState(dstStateName);
        bool srcIsIdle = IsIdleState(srcStateName);
        bool dstIsIdle = IsIdleState(dstStateName);

        if ((srcIsIdle && dstIsMove) || (srcIsMove && dstIsIdle) || (srcIsMove && dstIsMove))
            return true;

        return false;
    }

    private static bool IsMovementState(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = name.ToLowerInvariant();
        return lower.Contains("walk") || lower.Contains("run") || lower.Contains("moving") ||
               lower.Contains("gallop") || lower.Contains("canter") || lower.Contains("locomotion") ||
               lower.Contains("sprint");
    }

    private static bool IsIdleState(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string lower = name.ToLowerInvariant();
        return lower.Contains("idle") || lower.Contains("stop") || lower.Contains("stand");
    }

    private static AnimationClip FindBestMatchingClip(string stateName, List<AnimationClip> clips)
    {
        if (clips == null || clips.Count == 0) return null;

        // 1. Exact match on clip.name
        var exact = clips.FirstOrDefault(c => c.name.Equals(stateName, System.StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // 2. Exact match on FBX asset file name (e.g. state "Idle" matches "Idle.fbx")
        var fileExact = clips.FirstOrDefault(c =>
        {
            string p = AssetDatabase.GetAssetPath(c);
            string fName = System.IO.Path.GetFileNameWithoutExtension(p);
            return fName.Equals(stateName, System.StringComparison.OrdinalIgnoreCase);
        });
        if (fileExact != null) return fileExact;

        // 3. Contains state name in clip.name or file name
        var contains = clips.Where(c =>
        {
            string p = AssetDatabase.GetAssetPath(c);
            string fName = System.IO.Path.GetFileNameWithoutExtension(p);
            return c.name.IndexOf(stateName, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fName.IndexOf(stateName, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }).OrderBy(c => c.name.Length).FirstOrDefault();
        if (contains != null) return contains;

        // 4. Special cases
        if (stateName.Equals("walk", System.StringComparison.OrdinalIgnoreCase) ||
            stateName.Equals("walking", System.StringComparison.OrdinalIgnoreCase) ||
            stateName.Equals("run", System.StringComparison.OrdinalIgnoreCase))
        {
            var moveClip = clips.FirstOrDefault(c =>
                c.name.IndexOf("walk", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.name.IndexOf("run", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.name.IndexOf("gallop", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.name.IndexOf("animation'", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (moveClip != null) return moveClip;
        }

        if (stateName.Equals("Die", System.StringComparison.OrdinalIgnoreCase) ||
            stateName.Equals("Death", System.StringComparison.OrdinalIgnoreCase))
        {
            var dieClip = clips.FirstOrDefault(c =>
                c.name.IndexOf("die", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.name.IndexOf("death", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.name.IndexOf("dying", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (dieClip != null) return dieClip;
        }

        if (stateName.Equals("Attack", System.StringComparison.OrdinalIgnoreCase))
        {
            var atkClip = clips.FirstOrDefault(c =>
                c.name.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.name.IndexOf("slash", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (atkClip != null) return atkClip;
        }

        return null;
    }
}
