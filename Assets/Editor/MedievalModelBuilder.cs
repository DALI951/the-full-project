using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MedievalModelBuilder — Restores clean authored 3D FBX models for all buildings
/// and resource nodes (GoldMine.fbx, Wall.fbx, Farm.fbx, LumberMill.fbx, Market.fbx,
/// Tower.fbx, HomeSite.fbx, Barracks.fbx), strips primitive props, and sets up two-sided materials.
/// </summary>
public static class MedievalModelBuilder
{
    [MenuItem("Tools/Rebuild Medieval Models")]
    public static void RebuildAllModels()
    {
        Debug.Log("[MedievalModelBuilder] Restoring authored 3D FBX models for all buildings and resources...");

        RebuildGoldMine("Assets/Prefabs/GoldMine.prefab");
        RebuildWall("Assets/Prefabs/Wall.prefab");
        RestoreBuildingFBX("Assets/Prefabs/Farm.prefab", "Assets/Models/Buildings/Farm.fbx", "Farm");
        RestoreBuildingFBX("Assets/Prefabs/LumberMill.prefab", "Assets/Models/Buildings/LumberMill.fbx", "LumberMill");
        RestoreBuildingFBX("Assets/Prefabs/Market.prefab", "Assets/Models/Buildings/Market.fbx", "Market");
        RestoreBuildingFBX("Assets/Prefabs/Tower.prefab", "Assets/Models/Buildings/Tower.fbx", "Tower");
        RestoreBuildingFBX("Assets/Prefabs/HomeSite.prefab", "Assets/Models/Buildings/HomeSite.fbx", "HomeSite");
        RestoreBuildingFBX("Assets/Prefabs/Barracks.prefab", "Assets/Models/Buildings/Barracks.fbx", "Barracks");

        AssetDatabase.SaveAssets();
        Debug.Log("[MedievalModelBuilder] All authored 3D FBX models successfully restored and cleaned!");
    }

    // =========================================================================
    // 1. GoldMine.prefab — Authored GoldMine.fbx, Rotated 180°, Two-Sided Material
    // =========================================================================
    public static void RebuildGoldMine(string prefabPath)
    {
        if (!File.Exists(prefabPath))
        {
            Debug.LogWarning($"[MedievalModelBuilder] Prefab not found: {prefabPath}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            // 1. Disable prototype renderer on root
            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null && rootRend.enabled) rootRend.enabled = false;

            // 2. Remove any previous primitive cube visual stacks or old models
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.name.Equals("GoldMineVisual", StringComparison.OrdinalIgnoreCase) ||
                    child.name.Equals("GoldMineModel", StringComparison.OrdinalIgnoreCase) ||
                    child.name.Equals("ArchitecturalProps", StringComparison.OrdinalIgnoreCase) ||
                    child.name.IndexOf("GoldMine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    child.name.Equals("Model", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            // 3. Instantiate authored GoldMine.fbx
            string fbxPath = "Assets/Models/Buildings/GoldMine.fbx";
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset == null)
            {
                Debug.LogError($"[MedievalModelBuilder] Missing model asset at {fbxPath}");
                return;
            }

            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, root.transform);
            if (modelInstance == null)
                modelInstance = UnityEngine.Object.Instantiate(fbxAsset, root.transform);

            modelInstance.name = "GoldMineModel";
            modelInstance.transform.localPosition = Vector3.zero;
            // Entrance faces toward the camera (camera is at negative Z looking positive Z)
            modelInstance.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
            modelInstance.transform.localScale = Vector3.one;

            // 4. Configure two-sided material with _Cull = 0 so backfaces are never invisible
            Material goldMineMat = GetOrCreateTwoSidedBuildingMaterial("Mat_GoldMine_TwoSided", "Assets/Models/Buildings/GoldMine.fbm/BuildingAtlas.png");
            foreach (Renderer r in modelInstance.GetComponentsInChildren<Renderer>(true))
            {
                if (goldMineMat != null)
                {
                    r.sharedMaterial = goldMineMat;
                }
                else if (r.sharedMaterial != null)
                {
                    if (r.sharedMaterial.HasProperty("_Cull"))
                    {
                        r.sharedMaterial.SetFloat("_Cull", 0f);
                        r.sharedMaterial.SetInt("_Cull", 0);
                    }
                }
            }

            // 5. Remove any colliders from the visual child
            StripColliders(modelInstance);

            // 6. Hook up ResourceNode.visualRoot
            ResourceNode rn = root.GetComponent<ResourceNode>();
            if (rn != null)
            {
                rn.visualRoot = modelInstance;
                EditorUtility.SetDirty(rn);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[MedievalModelBuilder] Restored authored 3D GoldMine.fbx (facing camera, two-sided PBR): {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =========================================================================
    // 2. Wall.prefab — Authored Wall.fbx
    // =========================================================================
    public static void RebuildWall(string prefabPath)
    {
        if (!File.Exists(prefabPath))
        {
            Debug.LogWarning($"[MedievalModelBuilder] Prefab not found: {prefabPath}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null && rootRend.enabled) rootRend.enabled = false;

            // Remove WallVisual or any previous primitive cube stack
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.name.Equals("WallVisual", StringComparison.OrdinalIgnoreCase) ||
                    child.name.Equals("ArchitecturalProps", StringComparison.OrdinalIgnoreCase) ||
                    child.name.Equals("Wall", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            // Instantiate authored Wall.fbx
            string fbxPath = "Assets/Models/Buildings/Wall.fbx";
            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset == null)
            {
                Debug.LogError($"[MedievalModelBuilder] Missing model asset at {fbxPath}");
                return;
            }

            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, root.transform);
            if (modelInstance == null)
                modelInstance = UnityEngine.Object.Instantiate(fbxAsset, root.transform);

            modelInstance.name = "Wall";
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;

            StripColliders(modelInstance);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[MedievalModelBuilder] Restored authored 3D Wall.fbx: {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =========================================================================
    // 3. Buildings — Restore Authored FBX and Remove ArchitecturalProps
    // =========================================================================
    public static void RestoreBuildingFBX(string prefabPath, string fbxPath, string childName)
    {
        if (!File.Exists(prefabPath)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null && rootRend.enabled) rootRend.enabled = false;

            // Remove ArchitecturalProps and any loose primitive children
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name.Equals("ArchitecturalProps", StringComparison.OrdinalIgnoreCase) ||
                    child.name.Equals("GoldMineVisual", StringComparison.OrdinalIgnoreCase) ||
                    child.name.Equals("WallVisual", StringComparison.OrdinalIgnoreCase))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            // Check if authored model child already exists
            Transform existingModel = null;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.name.Equals("SpawnPoint", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.GetComponent<Canvas>() != null) continue;
                if (child.GetComponentsInChildren<Renderer>(true).Length > 0)
                {
                    existingModel = child;
                    break;
                }
            }

            if (existingModel == null)
            {
                GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                if (fbxAsset != null)
                {
                    GameObject childInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, root.transform);
                    if (childInstance == null)
                        childInstance = UnityEngine.Object.Instantiate(fbxAsset, root.transform);

                    childInstance.name = childName;
                    childInstance.transform.localPosition = Vector3.zero;
                    childInstance.transform.localRotation = Quaternion.identity;
                    childInstance.transform.localScale = Vector3.one;
                    existingModel = childInstance.transform;
                    Debug.Log($"[MedievalModelBuilder] Restored missing {childName} model from {fbxPath}");
                }
            }

            if (existingModel != null)
            {
                StripColliders(existingModel.gameObject);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[MedievalModelBuilder] Cleaned building prefab (no primitive props): {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // =========================================================================
    // Material & Collider Utilities
    // =========================================================================
    private static void StripColliders(GameObject go)
    {
        var colliders = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            UnityEngine.Object.DestroyImmediate(colliders[i]);
        }
    }

    private static Material GetOrCreateTwoSidedBuildingMaterial(string matName, string texturePath)
    {
        string folder = "Assets/Materials";
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        string path = $"{folder}/{matName}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find("Custom/TwoSidedBuilding");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Diffuse");

            mat = new Material(shader);
            mat.name = matName;

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex != null)
            {
                mat.mainTexture = tex;
            }

            mat.color = Color.white;
            if (mat.HasProperty("_Cull"))
            {
                mat.SetFloat("_Cull", 0f);
                mat.SetInt("_Cull", 0);
            }

            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            if (mat.HasProperty("_Cull"))
            {
                mat.SetFloat("_Cull", 0f);
                mat.SetInt("_Cull", 0);
            }
            EditorUtility.SetDirty(mat);
        }

        return mat;
    }
}
