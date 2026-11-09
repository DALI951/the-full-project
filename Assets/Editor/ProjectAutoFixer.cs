using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class ProjectAutoFixer
{
    private const string SessionKey = "ProjectAutoFixer_Ran_v2";

    static ProjectAutoFixer()
    {
        // Batch mode workers must never hold port 9876
        if (Application.isBatchMode)
        {
            try { AIBridgeServer.StopServer(); } catch { }
            return;
        }

        // Ensure AIBridgeServer is running on interactive Editor
        try { AIBridgeServer.StartServer(); } catch { }

        EditorApplication.delayCall += () =>
        {
            FixAll();
        };
    }

    [MenuItem("Tools/Fix All Prefabs And Animators")]
    public static void FixAll()
    {
        try
        {
            Debug.Log("[ProjectAutoFixer] Starting automated repair and scale normalization...");

            PBRAndEnvironmentUpgrader.UpgradeMaterialsAndTerrain();
            FixCavalryAnimator();
            FixDeerAnimator();
            FixVillagerAnimator();
            FixBuildingPrefabs();
            FixUnitPrefabs();
            FixResourcePrefabs();
            FixFlagPrefab();
            MedievalModelBuilder.RebuildAllModels();
            NormalizePrefabScaleAndColliders();

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectAutoFixer] All prefabs and animators verified, normalized, and saved successfully!");

            ReportAllPrefabWorldSizes();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ProjectAutoFixer] Error during auto-fix: {ex}");
        }
    }

    // =========================================================================
    // A. CavalryAnimator.controller
    // =========================================================================
    public static void FixCavalryAnimator()
    {
        string path = "Assets/Animations/CavalryAnimator.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null)
        {
            string[] guids = AssetDatabase.FindAssets("CavalryAnimator t:AnimatorController");
            if (guids.Length > 0)
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        if (controller == null)
        {
            Debug.LogWarning($"[ProjectAutoFixer] Could not find CavalryAnimator.controller");
            return;
        }

        // 1. Ensure parameters exist
        EnsureParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "Attack", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Die", AnimatorControllerParameterType.Bool);

        // 2. Load animation clips from Assets/Models/Horse/Horse_Quaternius.fbx
        string horseFbxPath = "Assets/Models/Horse/Horse_Quaternius.fbx";
        UnityEngine.Object[] horseAssets = AssetDatabase.LoadAllAssetsAtPath(horseFbxPath);
        List<AnimationClip> horseClips = horseAssets.OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToList();

        AnimationClip idleClip = FindClip(horseClips, "Idle");
        AnimationClip runClip = FindClip(horseClips, "Run", "Gallop");
        AnimationClip attackClip = FindClip(horseClips, "Attack");
        AnimationClip dieClip = FindClip(horseClips, "Die", "Death");

        if (controller.layers.Length == 0) return;
        var stateMachine = controller.layers[0].stateMachine;

        AnimatorState idleState = null;
        AnimatorState runState = null;
        AnimatorState attackState = null;
        AnimatorState dieState = null;

        foreach (var childState in stateMachine.states)
        {
            var s = childState.state;
            if (s.name.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            {
                idleState = s;
                if (idleClip != null) s.motion = idleClip;
            }
            else if (s.name.Equals("Run", StringComparison.OrdinalIgnoreCase) ||
                     s.name.Equals("Gallop", StringComparison.OrdinalIgnoreCase))
            {
                runState = s;
                if (runClip != null) s.motion = runClip;
            }
            else if (s.name.Equals("Attack", StringComparison.OrdinalIgnoreCase))
            {
                attackState = s;
                if (attackClip != null) s.motion = attackClip;
            }
            else if (s.name.Equals("Die", StringComparison.OrdinalIgnoreCase) ||
                     s.name.Equals("Death", StringComparison.OrdinalIgnoreCase))
            {
                dieState = s;
                if (dieClip != null) s.motion = dieClip;
            }
        }

        // 3. Programmatically add transitions if none exist
        if (idleState != null && runState != null && idleState.transitions.Length == 0)
        {
            var tIdleToRun = idleState.AddTransition(runState);
            tIdleToRun.hasExitTime = false;
            tIdleToRun.duration = 0.15f;
            tIdleToRun.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
        }

        if (runState != null && idleState != null && runState.transitions.Length == 0)
        {
            var tRunToIdle = runState.AddTransition(idleState);
            tRunToIdle.hasExitTime = false;
            tRunToIdle.duration = 0.15f;
            tRunToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
        }

        if (idleState != null && attackState != null && !idleState.transitions.Any(t => t.destinationState == attackState))
        {
            var tIdleToAtk = idleState.AddTransition(attackState);
            tIdleToAtk.hasExitTime = false;
            tIdleToAtk.duration = 0.1f;
            tIdleToAtk.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        }

        if (runState != null && attackState != null && !runState.transitions.Any(t => t.destinationState == attackState))
        {
            var tRunToAtk = runState.AddTransition(attackState);
            tRunToAtk.hasExitTime = false;
            tRunToAtk.duration = 0.1f;
            tRunToAtk.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        }

        if (attackState != null && idleState != null && attackState.transitions.Length == 0)
        {
            var tAtkToIdle = attackState.AddTransition(idleState);
            tAtkToIdle.hasExitTime = true;
            tAtkToIdle.exitTime = 0.9f;
            tAtkToIdle.duration = 0.15f;
        }

        if (dieState != null)
        {
            bool anyToDie = stateMachine.anyStateTransitions.Any(t => t.destinationState == dieState);
            if (!anyToDie)
            {
                var anyTrans = stateMachine.AddAnyStateTransition(dieState);
                anyTrans.hasExitTime = false;
                anyTrans.duration = 0.1f;
                anyTrans.canTransitionToSelf = false;
                anyTrans.AddCondition(AnimatorConditionMode.If, 0, "Die");
            }
        }

        EditorUtility.SetDirty(controller);
        Debug.Log("[ProjectAutoFixer] CavalryAnimator repaired.");
    }

    // =========================================================================
    // B. DeerAnimator.controller
    // =========================================================================
    public static void FixDeerAnimator()
    {
        string[] candidatePaths = {
            "Assets/ressources/Animals/lowpolydeer/DeerAnimator.controller",
            "Assets/Animations/DeerAnimator.controller"
        };

        foreach (string path in candidatePaths)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) continue;

            EnsureParameter(controller, "IsWalking", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, "Die", AnimatorControllerParameterType.Bool);

            // Collect clips exclusively from Assets/Models/Deer/
            List<AnimationClip> deerClips = new List<AnimationClip>();
            string[] deerGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models/Deer" });
            foreach (string g in deerGuids)
            {
                string fbxP = AssetDatabase.GUIDToAssetPath(g);
                if (fbxP.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    deerClips.AddRange(AssetDatabase.LoadAllAssetsAtPath(fbxP).OfType<AnimationClip>()
                        .Where(c => !c.name.StartsWith("__preview__")));
                }
            }

            // Also check lowpolydeer folder for die.anim
            string[] animGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/ressources/Animals/lowpolydeer" });
            foreach (string g in animGuids)
            {
                string aPath = AssetDatabase.GUIDToAssetPath(g);
                AnimationClip c = AssetDatabase.LoadAssetAtPath<AnimationClip>(aPath);
                if (c != null && !deerClips.Contains(c))
                    deerClips.Add(c);
            }

            AnimationClip walkClip = FindClip(deerClips, "deer_walk", "walk");
            AnimationClip idleClip = FindClip(deerClips, "ArmatureAction", "Idle");
            AnimationClip dieClip = FindClip(deerClips, "die", "death") ?? idleClip;

            if (controller.layers.Length > 0)
            {
                var stateMachine = controller.layers[0].stateMachine;
                AnimatorState idleState = null;
                AnimatorState walkState = null;
                AnimatorState dieState = null;

                foreach (var childState in stateMachine.states)
                {
                    var s = childState.state;
                    if (s.name.Equals("walk", StringComparison.OrdinalIgnoreCase) ||
                        s.name.Equals("Walking", StringComparison.OrdinalIgnoreCase))
                    {
                        walkState = s;
                        if (walkClip != null) s.motion = walkClip;
                    }
                    else if (s.name.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                    {
                        idleState = s;
                        if (idleClip != null) s.motion = idleClip;
                    }
                    else if (s.name.Equals("Die", StringComparison.OrdinalIgnoreCase) ||
                             s.name.Equals("Death", StringComparison.OrdinalIgnoreCase))
                    {
                        dieState = s;
                        if (dieClip != null) s.motion = dieClip;
                    }
                }

                if (idleState != null)
                {
                    foreach (var t in idleState.transitions)
                    {
                        if (t.destinationState == walkState)
                            t.hasExitTime = false;
                    }
                }

                if (walkState != null)
                {
                    foreach (var t in walkState.transitions)
                    {
                        if (t.destinationState == idleState)
                            t.hasExitTime = false;
                    }
                }

                if (dieState != null)
                {
                    bool anyToDie = stateMachine.anyStateTransitions.Any(t => t.destinationState == dieState);
                    if (!anyToDie)
                    {
                        var anyTrans = stateMachine.AddAnyStateTransition(dieState);
                        anyTrans.hasExitTime = false;
                        anyTrans.duration = 0.1f;
                        anyTrans.canTransitionToSelf = false;
                        anyTrans.AddCondition(AnimatorConditionMode.If, 0, "Die");
                    }
                }
            }

            EditorUtility.SetDirty(controller);
            Debug.Log($"[ProjectAutoFixer] DeerAnimator at {path} repaired.");
        }
    }

    // =========================================================================
    // C. VillagerAnimator.controller
    // =========================================================================
    public static void FixVillagerAnimator()
    {
        string[] candidatePaths = {
            "Assets/ressources/villager/VillagerAnimator.controller",
            "Assets/Animations/VillagerAnimator.controller"
        };

        foreach (string path in candidatePaths)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) continue;

            bool modified = false;

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;

                foreach (var childState in layer.stateMachine.states)
                {
                    var s = childState.state;
                    if (s.name.Equals("Punshing", StringComparison.OrdinalIgnoreCase))
                    {
                        s.name = "Punching";
                        modified = true;
                    }

                    foreach (var trans in s.transitions)
                    {
                        string srcName = s.name;
                        string dstName = trans.destinationState != null ? trans.destinationState.name : "";

                        if (srcName.Equals("Walking", StringComparison.OrdinalIgnoreCase) &&
                            dstName.Equals("Idle", StringComparison.OrdinalIgnoreCase))
                        {
                            trans.hasExitTime = false;
                            modified = true;
                        }

                        if (srcName.Equals("Idle", StringComparison.OrdinalIgnoreCase) &&
                            dstName.Equals("Gathering", StringComparison.OrdinalIgnoreCase))
                        {
                            trans.hasExitTime = false;
                            modified = true;
                        }
                    }
                }
            }

            if (modified)
            {
                EditorUtility.SetDirty(controller);
                Debug.Log($"[ProjectAutoFixer] VillagerAnimator at {path} repaired.");
            }
        }
    }

    // =========================================================================
    // D. Building Prefabs
    // =========================================================================
    public static void FixBuildingPrefabs()
    {
        var buildingConfigs = new (string path, string name, int maxHealth)[]
        {
            ("Assets/Prefabs/Tower.prefab",      "Tower",      400),
            ("Assets/Prefabs/Wall.prefab",       "Wall",       500),
            ("Assets/Prefabs/Farm.prefab",       "Farm",       250),
            ("Assets/Prefabs/LumberMill.prefab", "LumberMill", 300),
            ("Assets/Prefabs/Market.prefab",     "Market",     300)
        };

        foreach (var (prefabPath, buildingName, maxHealth) in buildingConfigs)
        {
            if (!File.Exists(prefabPath))
            {
                Debug.LogWarning($"[ProjectAutoFixer] Prefab not found: {prefabPath}");
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) continue;

            try
            {
                bool modified = false;

                Building b = root.GetComponent<Building>();
                if (b == null)
                {
                    b = root.AddComponent<Building>();
                    modified = true;
                }

                SerializedObject so = new SerializedObject(b);
                SerializedProperty nameProp = so.FindProperty("buildingName");
                SerializedProperty healthProp = so.FindProperty("maxBuildingHealth");

                if (nameProp != null && nameProp.stringValue != buildingName)
                {
                    nameProp.stringValue = buildingName;
                    modified = true;
                }
                if (healthProp != null && healthProp.intValue != maxHealth)
                {
                    healthProp.intValue = maxHealth;
                    modified = true;
                }
                so.ApplyModifiedProperties();

                if (buildingName == "Tower")
                {
                    Type towerAttackType = Type.GetType("TowerAttack, Assembly-CSharp") ?? Type.GetType("TowerAttack");
                    if (towerAttackType != null && root.GetComponent(towerAttackType) == null)
                    {
                        root.AddComponent(towerAttackType);
                        modified = true;
                        Debug.Log("[ProjectAutoFixer] Attached TowerAttack to Tower.prefab.");
                    }
                }

                if (buildingName == "Wall")
                {
                    Transform wv = root.transform.Find("WallVisual");
                    if (wv != null) { UnityEngine.Object.DestroyImmediate(wv.gameObject, true); modified = true; }
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log($"[ProjectAutoFixer] Fixed building prefab: {prefabPath}");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static bool EnsureChild3DModel(GameObject root, string fbxPath, string childName)
    {
        bool modified = false;

        bool hasModelChild = false;
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            if (child.name != "SelectionCircle" && child.GetComponentInChildren<Renderer>() != null)
            {
                hasModelChild = true;
                break;
            }
        }

        if (!hasModelChild)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (modelAsset != null)
            {
                GameObject childInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
                if (childInstance == null)
                    childInstance = UnityEngine.Object.Instantiate(modelAsset, root.transform);

                childInstance.name = childName;
                childInstance.transform.localPosition = Vector3.zero;
                childInstance.transform.localRotation = Quaternion.identity;
                childInstance.transform.localScale = Vector3.one;
                modified = true;
                Debug.Log($"[ProjectAutoFixer] Instantiated {fbxPath} as child under {root.name}");
            }
        }

        MeshRenderer rootRend = root.GetComponent<MeshRenderer>();
        if (rootRend != null && rootRend.enabled)
        {
            rootRend.enabled = false;
            modified = true;
        }

        return modified;
    }

    // =========================================================================
    // E. Unit Prefabs
    // =========================================================================
    public static void FixUnitPrefabs()
    {
        FixCavalryPrefab();
        FixUnitPrefabGeneral("Assets/Prefabs/Infantry.prefab", "Infantry", "Infantry");
        FixUnitPrefabGeneral("Assets/Prefabs/Villager.prefab", "Villager", "Villager");
        FixUnitPrefabGeneral("Assets/Prefabs/Builder.prefab", "Builder", "Builder");
    }

    private static void FixCavalryPrefab()
    {
        string prefabPath = "Assets/Prefabs/Cavarly.prefab";
        if (!File.Exists(prefabPath)) return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            bool modified = false;
            Cavalry cav = root.GetComponent<Cavalry>();

            if (cav != null)
            {
                SerializedObject so = new SerializedObject(cav);
                SerializedProperty uName = so.FindProperty("unitName");
                SerializedProperty uType = so.FindProperty("unitType");
                if (uName != null && uName.stringValue != "Cavalry")
                {
                    uName.stringValue = "Cavalry";
                    modified = true;
                }
                if (uType != null && uType.stringValue != "Cavalry")
                {
                    uType.stringValue = "Cavalry";
                    modified = true;
                }
                so.ApplyModifiedProperties();
            }

            string horseFbxPath = "Assets/Models/Horse/Horse_Quaternius.fbx";
            Transform horseChild = null;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name != "SelectionCircle" && child.GetComponentInChildren<Renderer>() != null)
                {
                    horseChild = child;
                    break;
                }
            }

            if (horseChild == null)
            {
                GameObject horseModel = AssetDatabase.LoadAssetAtPath<GameObject>(horseFbxPath);
                if (horseModel != null)
                {
                    GameObject childInstance = (GameObject)PrefabUtility.InstantiatePrefab(horseModel, root.transform);
                    if (childInstance == null)
                        childInstance = UnityEngine.Object.Instantiate(horseModel, root.transform);

                    childInstance.name = "Horse";
                    childInstance.transform.localPosition = Vector3.zero;
                    childInstance.transform.localRotation = Quaternion.identity;
                    childInstance.transform.localScale = Vector3.one;
                    horseChild = childInstance.transform;
                    modified = true;
                    Debug.Log("[ProjectAutoFixer] Instantiated Horse model on Cavarly.prefab");
                }
            }

            MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
            if (rootRenderer != null) UnityEngine.Object.DestroyImmediate(rootRenderer, true);
            MeshFilter rootFilter = root.GetComponent<MeshFilter>();
            if (rootFilter != null) UnityEngine.Object.DestroyImmediate(rootFilter, true);

            if (horseChild != null)
            {
                Animator anim = horseChild.GetComponentInChildren<Animator>();
                if (anim == null)
                    anim = horseChild.gameObject.AddComponent<Animator>();
                RuntimeAnimatorController cavCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/CavalryAnimator.controller");
                if (anim != null && cavCtrl != null && anim.runtimeAnimatorController != cavCtrl)
                {
                    anim.runtimeAnimatorController = cavCtrl;
                    modified = true;
                }

                Renderer horseRenderer = horseChild.GetComponentInChildren<Renderer>();
                if (cav != null)
                {
                    if (cav.animator != anim)
                    {
                        cav.animator = anim;
                        modified = true;
                    }

                    SerializedObject so = new SerializedObject(cav);
                    SerializedProperty uRend = so.FindProperty("unitRenderer");
                    if (uRend != null && horseRenderer != null && uRend.objectReferenceValue != horseRenderer)
                    {
                        uRend.objectReferenceValue = horseRenderer;
                        so.ApplyModifiedProperties();
                        modified = true;
                    }
                }
            }

            Transform selCircle = root.transform.Find("SelectionCircle");
            if (selCircle != null)
            {
                var colliders = selCircle.GetComponents<Collider>();
                foreach (var c in colliders)
                {
                    UnityEngine.Object.DestroyImmediate(c);
                    modified = true;
                }
            }

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log("[ProjectAutoFixer] Cavarly.prefab repaired.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static void FixResourcePrefabs()
    {
        string[] resourcePrefabs = {
            "Assets/Prefabs/GoldMine.prefab",
            "Assets/Prefabs/Tree.prefab",
            "Assets/Prefabs/Animal.prefab"
        };

        foreach (string prefabPath in resourcePrefabs)
        {
            if (!File.Exists(prefabPath)) continue;
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) continue;

            try
            {
                bool modified = false;
                if (prefabPath.EndsWith("GoldMine.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    Transform gmv = root.transform.Find("GoldMineVisual");
                    if (gmv != null) { UnityEngine.Object.DestroyImmediate(gmv.gameObject, true); modified = true; }
                }

                ResourceNode rn = root.GetComponent<ResourceNode>();
                if (rn != null && root.transform.childCount > 0)
                {
                    Transform visualChild = FindVisualChild(root);
                    if (visualChild != null && rn.visualRoot != visualChild.gameObject)
                    {
                        rn.visualRoot = visualChild.gameObject;
                        EditorUtility.SetDirty(rn);
                        modified = true;
                    }
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log($"[ProjectAutoFixer] {Path.GetFileName(prefabPath)} resource prefab repaired.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private static void FixUnitPrefabGeneral(string prefabPath, string unitName, string unitType)
    {
        if (!File.Exists(prefabPath)) return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            bool modified = false;
            Unit unit = root.GetComponent<Unit>();
            if (unit != null)
            {
                SerializedObject so = new SerializedObject(unit);
                SerializedProperty uName = so.FindProperty("unitName");
                SerializedProperty uType = so.FindProperty("unitType");
                if (uName != null && uName.stringValue != unitName)
                {
                    uName.stringValue = unitName;
                    modified = true;
                }
                if (uType != null && uType.stringValue != unitType)
                {
                    uType.stringValue = unitType;
                    modified = true;
                }
                so.ApplyModifiedProperties();
            }

            MeshRenderer rootRenderer = root.GetComponent<MeshRenderer>();
            if (rootRenderer != null)
            {
                UnityEngine.Object.DestroyImmediate(rootRenderer, true);
                modified = true;
            }
            MeshFilter rootFilter = root.GetComponent<MeshFilter>();
            if (rootFilter != null)
            {
                UnityEngine.Object.DestroyImmediate(rootFilter, true);
                modified = true;
            }

            Transform selCircle = root.transform.Find("SelectionCircle");
            if (selCircle != null)
            {
                var colliders = selCircle.GetComponents<Collider>();
                foreach (var c in colliders)
                {
                    UnityEngine.Object.DestroyImmediate(c);
                    modified = true;
                }
            }

            if (modified)
            {
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[ProjectAutoFixer] {Path.GetFileName(prefabPath)} repaired.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =========================================================================
    // F. Flag Prefab Setup
    // =========================================================================
    public static void FixFlagPrefab()
    {
        string prefabPath = "Assets/Prefabs/Flag.prefab";
        if (!File.Exists(prefabPath)) return;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var setup = root.GetComponent<FlagPrefabSetup>();
            if (setup == null)
            {
                root.AddComponent<FlagPrefabSetup>();
            }

            foreach (var c in root.GetComponents<Collider>())
            {
                UnityEngine.Object.DestroyImmediate(c);
            }

            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null && rootRend.enabled)
            {
                rootRend.enabled = false;
            }

            // Ensure Pole child
            Transform pole = root.transform.Find("Pole");
            if (pole == null)
            {
                GameObject poleObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                poleObj.name = "Pole";
                poleObj.transform.SetParent(root.transform, false);
                var coll = poleObj.GetComponent<Collider>();
                if (coll != null) UnityEngine.Object.DestroyImmediate(coll);
                pole = poleObj.transform;
            }
            else
            {
                foreach (var c in pole.GetComponents<Collider>())
                {
                    UnityEngine.Object.DestroyImmediate(c);
                }
            }

            // Standard cylinder: height 2.0. Scale y=0.4 => height 0.8f. Center at y=0.4 => bottom at 0.
            pole.localPosition = new Vector3(0, 0.4f, 0);
            pole.localRotation = Quaternion.identity;
            pole.localScale = new Vector3(0.04f, 0.4f, 0.04f);

            // Ensure FlagHead child
            Transform flagHead = root.transform.Find("FlagHead");
            if (flagHead == null)
            {
                GameObject headObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                headObj.name = "FlagHead";
                headObj.transform.SetParent(root.transform, false);
                var coll = headObj.GetComponent<Collider>();
                if (coll != null) UnityEngine.Object.DestroyImmediate(coll);
                flagHead = headObj.transform;
            }
            else
            {
                foreach (var c in flagHead.GetComponents<Collider>())
                {
                    UnityEngine.Object.DestroyImmediate(c);
                }
            }

            flagHead.localPosition = new Vector3(0.18f, 0.65f, 0);
            flagHead.localRotation = Quaternion.identity;
            flagHead.localScale = new Vector3(0.32f, 0.22f, 0.02f);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log("[ProjectAutoFixer] Flag.prefab normalized to 0.8f tall RTS flag without colliders.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =========================================================================
    // G. Automatic Bounds-Based Scale & Collider Normalizer
    // =========================================================================
    [MenuItem("Tools/Normalize Prefab Scale And Colliders")]
    public static void NormalizePrefabScaleAndColliders()
    {
        Debug.Log("[ProjectAutoFixer] Starting comprehensive bounds-based scale normalization...");

        // 1. Units
        NormalizeUnitPrefab("Assets/Prefabs/Villager.prefab", rootScale: new Vector3(0.75f, 0.75f, 0.75f), targetHeight: 1.75f, colliderRadius: 0.40f, navMeshRadius: 0.38f, selCircleScale: 1.35f);
        NormalizeUnitPrefab("Assets/Prefabs/Builder.prefab",  rootScale: new Vector3(0.75f, 0.75f, 0.75f), targetHeight: 1.75f, colliderRadius: 0.40f, navMeshRadius: 0.38f, selCircleScale: 1.35f);
        NormalizeUnitPrefab("Assets/Prefabs/Infantry.prefab", rootScale: new Vector3(0.78f, 0.78f, 0.78f), targetHeight: 1.85f, colliderRadius: 0.42f, navMeshRadius: 0.40f, selCircleScale: 1.40f);
        NormalizeCavalryScale("Assets/Prefabs/Cavarly.prefab", rootScale: new Vector3(0.75f, 0.75f, 0.75f), targetHeight: 2.20f, targetMaxHoriz: 2.40f, colliderRadius: 0.55f, navMeshRadius: 0.50f, selCircleScale: 1.85f);

        // 2. Resources
        NormalizeAnimalPrefab("Assets/Prefabs/Animal.prefab", targetHeight: 0.85f, targetMaxHoriz: 0.95f, colliderRadius: 0.35f, navMeshRadius: 0.30f, selCircleScale: 1.0f);
        NormalizeTreePrefab("Assets/Prefabs/Tree.prefab", targetHeight: 3.2f, colliderRadius: 0.35f);
        NormalizeGoldMinePrefab("Assets/Prefabs/GoldMine.prefab", targetMaxHoriz: 2.2f);

        // 3. Buildings
        NormalizeBuildingPrefab("Assets/Prefabs/HomeSite.prefab",   targetMaxHoriz: 4.80f, targetHeight: 3.50f, selCircleScale: 5.2f);
        NormalizeBuildingPrefab("Assets/Prefabs/Barracks.prefab",   targetMaxHoriz: 3.8f, targetHeight: -1f);
        NormalizeBuildingPrefab("Assets/Prefabs/Farm.prefab",       targetMaxHoriz: 3.2f, targetHeight: -1f);
        NormalizeBuildingPrefab("Assets/Prefabs/LumberMill.prefab", targetMaxHoriz: 3.2f, targetHeight: -1f);
        NormalizeBuildingPrefab("Assets/Prefabs/Market.prefab",     targetMaxHoriz: 3.2f, targetHeight: -1f);
        NormalizeBuildingPrefab("Assets/Prefabs/Tower.prefab",      targetMaxHoriz: 2.0f, targetHeight: 4.0f);
        NormalizeBuildingPrefab("Assets/Prefabs/Wall.prefab",       targetMaxHoriz: 2.2f, targetHeight: 2.2f);
        NormalizeConstructionSitePrefab("Assets/Prefabs/ConstructionSite.prefab", new Vector3(2.5f, 0.15f, 2.5f));

        Debug.Log("[ProjectAutoFixer] Comprehensive scale normalization complete!");
    }

    private static void NormalizeUnitPrefab(string prefabPath, Vector3 rootScale, float targetHeight, float colliderRadius, float navMeshRadius, float selCircleScale)
    {
        if (!File.Exists(prefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = rootScale;

            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null) UnityEngine.Object.DestroyImmediate(rootRend, true);
            var rootFilt = root.GetComponent<MeshFilter>();
            if (rootFilt != null) UnityEngine.Object.DestroyImmediate(rootFilt, true);

            Transform visualChild = FindVisualChild(root);
            if (visualChild != null)
            {
                visualChild.localRotation = Quaternion.identity;
                Bounds currentB = GetChildRenderersBounds(visualChild);
                if (currentB.size.y > 0.001f)
                {
                    float factor = targetHeight / currentB.size.y;
                    visualChild.localScale *= factor;
                }

                Bounds newB = GetChildRenderersBounds(visualChild);
                visualChild.position += new Vector3(-newB.center.x, -newB.min.y, -newB.center.z);
            }

            // Collider on root (scaled by rootScale):
            CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y-axis
            capsule.height = targetHeight / rootScale.y;
            capsule.radius = colliderRadius / rootScale.x;
            capsule.center = new Vector3(0, (targetHeight * 0.5f) / rootScale.y, 0);

            // NavMeshAgent on root
            NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = targetHeight;
                agent.radius = navMeshRadius;
                agent.baseOffset = 0f;
            }

            // SelectionCircle
            Transform selCircle = root.transform.Find("SelectionCircle");
            if (selCircle != null)
            {
                selCircle.localPosition = new Vector3(0, 0.02f, 0);
                selCircle.localScale = new Vector3(selCircleScale, 0.01f, selCircleScale);
                foreach (var c in selCircle.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(c);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ProjectAutoFixer] Normalized unit: {Path.GetFileName(prefabPath)} (H={targetHeight:F2}, RootScale={rootScale})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void NormalizeUnitPrefab(string prefabPath, float targetHeight, float colliderRadius, float selCircleScale)
    {
        NormalizeUnitPrefab(prefabPath, new Vector3(0.5f, 0.5f, 0.5f), targetHeight, colliderRadius, colliderRadius, selCircleScale);
    }

    private static void NormalizeCavalryScale(string prefabPath, Vector3 rootScale, float targetHeight, float targetMaxHoriz, float colliderRadius, float navMeshRadius, float selCircleScale)
    {
        if (!File.Exists(prefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = rootScale;

            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null) UnityEngine.Object.DestroyImmediate(rootRend, true);
            var rootFilt = root.GetComponent<MeshFilter>();
            if (rootFilt != null) UnityEngine.Object.DestroyImmediate(rootFilt, true);

            Transform horseChild = FindVisualChild(root);
            if (horseChild != null)
            {
                Bounds currentB = GetChildRenderersBounds(horseChild);
                float curHoriz = Mathf.Max(currentB.size.x, currentB.size.z);
                float factorY = currentB.size.y > 0.001f ? (targetHeight / currentB.size.y) : 1f;
                float factorHoriz = (targetMaxHoriz > 0f && curHoriz > 0.001f) ? (targetMaxHoriz / curHoriz) : factorY;

                horseChild.localScale = new Vector3(horseChild.localScale.x * factorHoriz, horseChild.localScale.y * factorY, horseChild.localScale.z * factorHoriz);

                Bounds newB = GetChildRenderersBounds(horseChild);
                horseChild.position += new Vector3(-newB.center.x, -newB.min.y, -newB.center.z);
            }

            CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y-axis
            capsule.height = targetHeight / rootScale.y;
            capsule.radius = colliderRadius / rootScale.x;
            capsule.center = new Vector3(0, (targetHeight * 0.5f) / rootScale.y, 0);

            NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = targetHeight;
                agent.radius = navMeshRadius;
                agent.baseOffset = 0f;
            }

            Transform selCircle = root.transform.Find("SelectionCircle");
            if (selCircle != null)
            {
                selCircle.localPosition = new Vector3(0, 0.02f, 0);
                selCircle.localScale = new Vector3(selCircleScale, 0.01f, selCircleScale);
                foreach (var c in selCircle.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(c);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ProjectAutoFixer] Normalized Cavalry: {Path.GetFileName(prefabPath)} (H={targetHeight:F2}, Horiz={targetMaxHoriz:F2}, RootScale={rootScale})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void NormalizeCavalryScale(string prefabPath, float targetHeight, float colliderRadius, float selCircleScale)
    {
        NormalizeCavalryScale(prefabPath, new Vector3(0.5f, 0.5f, 0.5f), targetHeight, -1f, colliderRadius, colliderRadius, selCircleScale);
    }

    private static void NormalizeAnimalPrefab(string prefabPath, float targetHeight, float targetMaxHoriz, float colliderRadius, float navMeshRadius, float selCircleScale = 1.0f)
    {
        if (!File.Exists(prefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null) UnityEngine.Object.DestroyImmediate(rootRend, true);
            var rootFilt = root.GetComponent<MeshFilter>();
            if (rootFilt != null) UnityEngine.Object.DestroyImmediate(rootFilt, true);

            Transform visualChild = FindVisualChild(root);
            if (visualChild != null)
            {
                visualChild.localRotation = Quaternion.identity;
                Bounds currentB = GetChildRenderersBounds(visualChild);
                float curHoriz = Mathf.Max(currentB.size.x, currentB.size.z);
                float factorY = currentB.size.y > 0.001f ? (targetHeight / currentB.size.y) : 1f;
                float factorHoriz = (targetMaxHoriz > 0f && curHoriz > 0.001f) ? (targetMaxHoriz / curHoriz) : factorY;

                visualChild.localScale = new Vector3(visualChild.localScale.x * factorHoriz, visualChild.localScale.y * factorY, visualChild.localScale.z * factorHoriz);

                Bounds newB = GetChildRenderersBounds(visualChild);
                visualChild.position += new Vector3(-newB.center.x, -newB.min.y, -newB.center.z);
            }

            CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.height = targetHeight;
            capsule.radius = colliderRadius;
            capsule.center = new Vector3(0, targetHeight * 0.5f, 0);

            NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.height = targetHeight;
                agent.radius = navMeshRadius;
                agent.baseOffset = 0f;
            }

            Transform selCircle = root.transform.Find("SelectionCircle");
            if (selCircle != null)
            {
                selCircle.localPosition = new Vector3(0, 0.02f, 0);
                selCircle.localScale = new Vector3(selCircleScale, 0.01f, selCircleScale);
                foreach (var c in selCircle.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(c);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ProjectAutoFixer] Normalized Animal: {Path.GetFileName(prefabPath)} (H={targetHeight:F2}, Horiz={targetMaxHoriz:F2})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void NormalizeAnimalPrefab(string prefabPath, float targetHeight, float colliderRadius)
    {
        NormalizeAnimalPrefab(prefabPath, targetHeight, -1f, colliderRadius, colliderRadius, 1.0f);
    }

    private static void NormalizeTreePrefab(string prefabPath, float targetHeight, float colliderRadius)
    {
        if (!File.Exists(prefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null) UnityEngine.Object.DestroyImmediate(rootRend, true);
            var rootFilt = root.GetComponent<MeshFilter>();
            if (rootFilt != null) UnityEngine.Object.DestroyImmediate(rootFilt, true);

            Transform visualChild = FindVisualChild(root);
            if (visualChild != null)
            {
                visualChild.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                Bounds currentB = GetChildRenderersBounds(visualChild);
                if (currentB.size.y > 0.001f)
                {
                    float factor = targetHeight / currentB.size.y;
                    visualChild.localScale *= factor;
                }

                Bounds newB = GetChildRenderersBounds(visualChild);
                visualChild.position += new Vector3(-newB.center.x, -newB.min.y, -newB.center.z);
            }

            // Remove extra/duplicate colliders
            var colliders = root.GetComponents<Collider>();
            for (int i = 1; i < colliders.Length; i++)
            {
                UnityEngine.Object.DestroyImmediate(colliders[i]);
            }

            CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
            if (capsule == null) capsule = root.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.height = targetHeight;
            capsule.radius = colliderRadius;
            capsule.center = new Vector3(0, targetHeight * 0.5f, 0);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ProjectAutoFixer] Normalized Tree: {Path.GetFileName(prefabPath)} (H={targetHeight:F2}, R={colliderRadius:F2})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void NormalizeGoldMinePrefab(string prefabPath, float targetMaxHoriz)
    {
        if (!File.Exists(prefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null) UnityEngine.Object.DestroyImmediate(rootRend, true);
            var rootFilt = root.GetComponent<MeshFilter>();
            if (rootFilt != null) UnityEngine.Object.DestroyImmediate(rootFilt, true);

            Transform visualChild = FindVisualChild(root);
            if (visualChild != null)
            {
                visualChild.localRotation = Quaternion.identity;
                Bounds currentB = GetChildRenderersBounds(visualChild);
                float curHoriz = Mathf.Max(currentB.size.x, currentB.size.z);
                float factorHoriz = curHoriz > 0.001f ? (targetMaxHoriz / curHoriz) : 1f;
                float factorY = currentB.size.y > 0.001f ? (1.4f / currentB.size.y) : 1f;
                visualChild.localScale = new Vector3(visualChild.localScale.x * factorHoriz, visualChild.localScale.y * factorY, visualChild.localScale.z * factorHoriz);

                Bounds newB = GetChildRenderersBounds(visualChild);
                visualChild.position += new Vector3(-newB.center.x, -newB.min.y, -newB.center.z);

                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box == null) box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(newB.size.x, newB.size.y, newB.size.z);
                box.center = new Vector3(0, newB.size.y * 0.5f, 0);

                NavMeshObstacle obs = root.GetComponent<NavMeshObstacle>();
                if (obs == null) obs = root.AddComponent<NavMeshObstacle>();
                obs.shape = NavMeshObstacleShape.Box;
                obs.size = new Vector3(newB.size.x, newB.size.y, newB.size.z);
                obs.center = new Vector3(0, newB.size.y * 0.5f, 0);
                obs.carving = true;
                obs.carveOnlyStationary = true;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ProjectAutoFixer] Normalized GoldMine: {Path.GetFileName(prefabPath)} (MaxHoriz={targetMaxHoriz:F2}, H=1.40)");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void NormalizeBuildingPrefab(string prefabPath, float targetMaxHoriz, float targetHeight, float selCircleScale = -1f)
    {
        if (!File.Exists(prefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null) UnityEngine.Object.DestroyImmediate(rootRend, true);
            var rootFilt = root.GetComponent<MeshFilter>();
            if (rootFilt != null) UnityEngine.Object.DestroyImmediate(rootFilt, true);

            Transform visualChild = FindVisualChild(root);
            if (visualChild != null)
            {
                visualChild.localRotation = Quaternion.identity;

                Bounds currentB = GetChildRenderersBounds(visualChild);
                float curHoriz = Mathf.Max(currentB.size.x, currentB.size.z);

                if (targetMaxHoriz > 0f && targetHeight > 0f)
                {
                    float factorHoriz = curHoriz > 0.001f ? (targetMaxHoriz / curHoriz) : 1f;
                    float factorY = currentB.size.y > 0.001f ? (targetHeight / currentB.size.y) : 1f;
                    visualChild.localScale = new Vector3(visualChild.localScale.x * factorHoriz, visualChild.localScale.y * factorY, visualChild.localScale.z * factorHoriz);
                }
                else if (targetHeight > 0f && currentB.size.y > 0.001f)
                {
                    float factor = targetHeight / currentB.size.y;
                    visualChild.localScale *= factor;
                }
                else if (targetMaxHoriz > 0f && curHoriz > 0.001f)
                {
                    float factor = targetMaxHoriz / curHoriz;
                    visualChild.localScale *= factor;
                }

                Bounds newB = GetChildRenderersBounds(visualChild);
                visualChild.position += new Vector3(-newB.center.x, -newB.min.y, -newB.center.z);

                // Update BoxCollider to match visual model bounds
                BoxCollider box = root.GetComponent<BoxCollider>();
                if (box == null) box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(newB.size.x, newB.size.y, newB.size.z);
                box.center = new Vector3(0, newB.size.y * 0.5f, 0);

                // Update NavMeshObstacle to match visual model bounds
                NavMeshObstacle obs = root.GetComponent<NavMeshObstacle>();
                if (obs == null) obs = root.AddComponent<NavMeshObstacle>();
                obs.shape = NavMeshObstacleShape.Box;
                obs.size = new Vector3(newB.size.x, newB.size.y, newB.size.z);
                obs.center = new Vector3(0, newB.size.y * 0.5f, 0);
                obs.carving = true;
                obs.carveOnlyStationary = true;

                // Adjust SpawnPoint if present
                Transform spawnPt = root.transform.Find("SpawnPoint");
                if (spawnPt != null)
                {
                    float offsetZ = (newB.size.z * 0.5f) + 0.8f;
                    spawnPt.localPosition = new Vector3(0, 0, offsetZ);
                }
            }

            // SelectionCircle if present
            if (selCircleScale > 0f)
            {
                Transform selCircle = root.transform.Find("SelectionCircle");
                if (selCircle != null)
                {
                    selCircle.localPosition = new Vector3(0, 0.02f, 0);
                    selCircle.localScale = new Vector3(selCircleScale, 0.01f, selCircleScale);
                    foreach (var c in selCircle.GetComponents<Collider>())
                        UnityEngine.Object.DestroyImmediate(c);
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ProjectAutoFixer] Normalized building: {Path.GetFileName(prefabPath)} (TargetMaxHoriz={targetMaxHoriz:F2}, TargetHeight={targetHeight:F2})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void NormalizeConstructionSitePrefab(string prefabPath, Vector3 targetDimensions)
    {
        if (!File.Exists(prefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            Transform cubeChild = root.transform.Find("Cube");
            if (cubeChild != null)
            {
                cubeChild.localScale = targetDimensions;
                cubeChild.localPosition = new Vector3(0, targetDimensions.y * 0.5f, 0);
                foreach (var c in cubeChild.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(c);
            }

            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null && cubeChild != null)
                rootRend.enabled = false;

            BoxCollider box = root.GetComponent<BoxCollider>();
            if (box == null) box = root.AddComponent<BoxCollider>();
            box.size = targetDimensions;
            box.center = new Vector3(0, targetDimensions.y * 0.5f, 0);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[ProjectAutoFixer] Normalized ConstructionSite: {targetDimensions}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =========================================================================
    // H. Report All Prefab World Sizes
    // =========================================================================
    [MenuItem("Tools/Report All Prefab World Sizes")]
    public static void ReportAllPrefabWorldSizes()
    {
        string[] prefabs = {
            "Assets/Prefabs/Villager.prefab",
            "Assets/Prefabs/Builder.prefab",
            "Assets/Prefabs/Infantry.prefab",
            "Assets/Prefabs/Cavarly.prefab",
            "Assets/Prefabs/Animal.prefab",
            "Assets/Prefabs/Tree.prefab",
            "Assets/Prefabs/GoldMine.prefab",
            "Assets/Prefabs/HomeSite.prefab",
            "Assets/Prefabs/Barracks.prefab",
            "Assets/Prefabs/Farm.prefab",
            "Assets/Prefabs/LumberMill.prefab",
            "Assets/Prefabs/Market.prefab",
            "Assets/Prefabs/Tower.prefab",
            "Assets/Prefabs/Wall.prefab",
            "Assets/Prefabs/ConstructionSite.prefab",
            "Assets/Prefabs/Flag.prefab"
        };

        Debug.Log("================ PREFAB WORLD SIZE REPORT ================");
        foreach (var path in prefabs)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[PrefabReport] Not found: {path}");
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;
            try
            {
                Bounds b = GetVisualRenderersBounds(root);
                Collider col = root.GetComponent<Collider>();
                string colInfo = "none";
                if (col is BoxCollider bc)
                    colInfo = $"Box({bc.size.x * root.transform.lossyScale.x:F2}, {bc.size.y * root.transform.lossyScale.y:F2}, {bc.size.z * root.transform.lossyScale.z:F2})";
                else if (col is CapsuleCollider cc)
                    colInfo = $"Capsule(r={cc.radius * root.transform.lossyScale.x:F2}, h={cc.height * root.transform.lossyScale.y:F2})";

                Debug.Log($"[PrefabReport] {Path.GetFileNameWithoutExtension(path),-16} | Bounds: {b.size.x:F2}W x {b.size.y:F2}H x {b.size.z:F2}D | Bottom Y: {b.min.y:F2} | RootScale: {root.transform.localScale} | Collider: {colInfo}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        Debug.Log("==========================================================");
    }

    // =========================================================================
    // Bounds & Geometry Utilities
    // =========================================================================
    private static Transform FindVisualChild(GameObject root)
    {
        for (int i = 0; i < root.transform.childCount; i++)
        {
            Transform child = root.transform.GetChild(i);
            if (child.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase)) continue;
            if (child.name.Equals("SpawnPoint", StringComparison.OrdinalIgnoreCase)) continue;
            if (child.name.Equals("Pole", StringComparison.OrdinalIgnoreCase)) continue;
            if (child.name.Equals("FlagHead", StringComparison.OrdinalIgnoreCase)) continue;
            if (child.name.Equals("ArchitecturalProps", StringComparison.OrdinalIgnoreCase)) continue;
            if (child.GetComponent<Canvas>() != null) continue;

            if (child.GetComponentsInChildren<Renderer>(true).Length > 0)
            {
                return child;
            }
        }
        return null;
    }

    private static Bounds GetChildRenderersBounds(Transform visualChild)
    {
        Bounds b = new Bounds();
        bool hasBounds = false;
        Renderer[] renderers = visualChild.GetComponentsInChildren<Renderer>(false);
        foreach (var r in renderers)
        {
            if (r.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase)) continue;

            if (r.GetComponentInParent<Canvas>() != null) continue;

            if (!hasBounds)
            {
                b = r.bounds;
                hasBounds = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }
        return b;
    }

    private static Bounds GetVisualRenderersBounds(GameObject root)
    {
        Bounds b = new Bounds();
        bool hasBounds = false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        foreach (var r in renderers)
        {
            if (r.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase)) continue;

            if (r.GetComponentInParent<Canvas>() != null) continue;

            // Ignore disabled root prototype cube/capsule mesh renderers if children have renderers
            if (r.gameObject == root && root.transform.childCount > 0)
            {
                bool hasChildRend = false;
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var ch = root.transform.GetChild(i);
                    if (!ch.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase) &&
                        ch.GetComponentsInChildren<Renderer>(false).Length > 0)
                    {
                        hasChildRend = true;
                        break;
                    }
                }
                if (hasChildRend) continue;
            }

            if (!hasBounds)
            {
                b = r.bounds;
                hasBounds = true;
            }
            else
            {
                b.Encapsulate(r.bounds);
            }
        }
        return b;
    }

    // =========================================================================
    // General Utilities
    // =========================================================================
    private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        if (controller.parameters.All(p => !p.name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            controller.AddParameter(name, type);
        }
    }

    private static AnimationClip FindClip(List<AnimationClip> clips, params string[] searchNames)
    {
        if (clips == null || clips.Count == 0) return null;

        foreach (string term in searchNames)
        {
            var match = clips.FirstOrDefault(c => c.name.Equals(term, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }

        foreach (string term in searchNames)
        {
            var match = clips.FirstOrDefault(c => c.name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            if (match != null) return match;
        }

        return null;
    }
}
