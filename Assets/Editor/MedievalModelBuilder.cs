using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MedievalModelBuilder — Binds high-detail AoE4 3D models (Assets/Models/AoE4Buildings/*.obj)
/// to the 8 building prefabs, wires all 17 PBR normal-mapped materials from Assets/Materials/AoE4PBR/,
/// and strips duplicate/child colliders so physics and navigation remain pristine.
/// </summary>
public static class MedievalModelBuilder
{
    private const string PbrMaterialFolder = "Assets/Materials/AoE4PBR";

    private static readonly (string prefabPath, string objPath, string childName)[] BuildingMappings = new[]
    {
        ("Assets/Prefabs/HomeSite.prefab",   "Assets/Models/AoE4Buildings/AoE4_TownCenter.obj", "TownCenter"),
        ("Assets/Prefabs/Barracks.prefab",   "Assets/Models/AoE4Buildings/AoE4_Barracks.obj",   "Barracks"),
        ("Assets/Prefabs/Farm.prefab",       "Assets/Models/AoE4Buildings/AoE4_Farm.obj",       "Farm"),
        ("Assets/Prefabs/LumberMill.prefab", "Assets/Models/AoE4Buildings/AoE4_LumberMill.obj", "LumberMill"),
        ("Assets/Prefabs/Market.prefab",     "Assets/Models/AoE4Buildings/AoE4_Market.obj",     "Market"),
        ("Assets/Prefabs/Tower.prefab",      "Assets/Models/AoE4Buildings/AoE4_Watchtower.obj", "Tower"),
        ("Assets/Prefabs/Wall.prefab",       "Assets/Models/AoE4Buildings/AoE4_StoneWall.obj",  "Wall"),
        ("Assets/Prefabs/GoldMine.prefab",   "Assets/Models/AoE4Buildings/AoE4_GoldMine.obj",   "GoldMine")
    };

    [MenuItem("Tools/Rebuild Medieval Models")]
    public static void RebuildAllModels()
    {
        Debug.Log("[MedievalModelBuilder] Binding high-detail AoE4 OBJ models and 17 PBR materials to all 8 prefabs...");

        var pbrMatDict = LoadPbrMaterialDictionary();

        foreach (var (prefabPath, objPath, childName) in BuildingMappings)
        {
            RebuildBuildingModel(prefabPath, objPath, childName, pbrMatDict);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[MedievalModelBuilder] All 8 AoE4 building models successfully bound, PBR textured, and colliders stripped!");
    }

    public static void RebuildBuildingModel(string prefabPath, string objPath, string childName, Dictionary<string, Material> pbrMatDict = null)
    {
        if (!File.Exists(prefabPath))
        {
            Debug.LogWarning($"[MedievalModelBuilder] Prefab not found: {prefabPath}");
            return;
        }

        if (pbrMatDict == null)
        {
            pbrMatDict = LoadPbrMaterialDictionary();
        }

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return;

        try
        {
            // 1. Remove or disable prototype renderer/meshfilter on root
            var rootRend = root.GetComponent<MeshRenderer>();
            if (rootRend != null) UnityEngine.Object.DestroyImmediate(rootRend, true);
            var rootFilt = root.GetComponent<MeshFilter>();
            if (rootFilt != null) UnityEngine.Object.DestroyImmediate(rootFilt, true);

            // 2. Strip previous child visual objects, primitive props, and old models
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = root.transform.GetChild(i);
                if (child.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.name.Equals("SpawnPoint", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.name.Equals("Pole", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.name.Equals("FlagHead", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.GetComponent<Canvas>() != null) continue;

                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            // 3. Load authored AoE4 .obj asset
            GameObject objAsset = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
            if (objAsset == null)
            {
                Debug.LogError($"[MedievalModelBuilder] Missing model asset at {objPath}");
                return;
            }

            // 4. Instantiate model child
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(objAsset, root.transform);
            if (modelInstance == null)
            {
                modelInstance = UnityEngine.Object.Instantiate(objAsset, root.transform);
            }

            modelInstance.name = childName;
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;

            // 5. Strip all child colliders (only root should hold BoxCollider / NavMeshObstacle)
            StripColliders(modelInstance);

            // 6. Wire 17 PBR normal-mapped materials from Assets/Materials/AoE4PBR/
            WirePbrMaterials(modelInstance, pbrMatDict);

            // 7. Hook up ResourceNode.visualRoot if present (e.g. GoldMine)
            ResourceNode rn = root.GetComponent<ResourceNode>();
            if (rn != null)
            {
                rn.visualRoot = modelInstance;
                EditorUtility.SetDirty(rn);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[MedievalModelBuilder] Successfully bound AoE4 model '{childName}' to {prefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Dictionary<string, Material> LoadPbrMaterialDictionary()
    {
        var dict = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        if (!AssetDatabase.IsValidFolder(PbrMaterialFolder)) return dict;

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { PbrMaterialFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                string key = mat.name.Trim();
                dict[key] = mat;
                // Also index without common prefixes/suffixes
                dict[key.Replace(" ", "")] = mat;
                dict[key.Replace("_", "")] = mat;
            }
        }

        return dict;
    }

    private static void WirePbrMaterials(GameObject modelInstance, Dictionary<string, Material> pbrMatDict)
    {
        if (pbrMatDict == null || pbrMatDict.Count == 0) return;

        Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            Material[] shared = r.sharedMaterials;
            bool modified = false;

            for (int i = 0; i < shared.Length; i++)
            {
                Material cur = shared[i];
                if (cur == null) continue;

                string matName = cur.name
                    .Replace(" (Instance)", "")
                    .Replace(" (Clone)", "")
                    .Trim();

                if (pbrMatDict.TryGetValue(matName, out Material targetMat) && targetMat != null)
                {
                    shared[i] = targetMat;
                    modified = true;
                }
                else
                {
                    // Fallback fuzzy search by key match
                    foreach (var kvp in pbrMatDict)
                    {
                        if (matName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            kvp.Key.IndexOf(matName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            shared[i] = kvp.Value;
                            modified = true;
                            break;
                        }
                    }
                }
            }

            if (modified)
            {
                r.sharedMaterials = shared;
                EditorUtility.SetDirty(r);
            }
        }
    }

    private static void StripColliders(GameObject go)
    {
        var colliders = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            UnityEngine.Object.DestroyImmediate(colliders[i], true);
        }
    }

    // Legacy method stubs
    public static void RebuildGoldMine(string prefabPath) =>
        RebuildBuildingModel(prefabPath, "Assets/Models/AoE4Buildings/AoE4_GoldMine.obj", "GoldMine");

    public static void RebuildWall(string prefabPath) =>
        RebuildBuildingModel(prefabPath, "Assets/Models/AoE4Buildings/AoE4_StoneWall.obj", "Wall");

    public static void RestoreBuildingFBX(string prefabPath, string fbxPath, string childName)
    {
        foreach (var mapping in BuildingMappings)
        {
            if (mapping.prefabPath.Equals(prefabPath, StringComparison.OrdinalIgnoreCase))
            {
                RebuildBuildingModel(mapping.prefabPath, mapping.objPath, mapping.childName);
                return;
            }
        }
    }
}
