using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class MaterialAndLightingFixer
{
    private const string SessionKey = "AoE4MaterialsFixed";

    static MaterialAndLightingFixer()
    {
        if (Application.isBatchMode) return;
        if (SessionState.GetBool(SessionKey, false)) return;

        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            FixAllMaterialsAndLighting();
            SessionState.SetBool(SessionKey, true);
        };
    }

    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        if (Application.isBatchMode) return;
        if (SessionState.GetBool(SessionKey, false)) return;

        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            FixAllMaterialsAndLighting();
            SessionState.SetBool(SessionKey, true);
        };
    }

    [MenuItem("Tools/Fix Materials And AoE4 Lighting")]
    public static void FixAllMaterialsAndLighting()
    {
        Debug.Log("[MaterialAndLightingFixer] Starting Opaque Material & AoE4 Lighting pass...");

        int fixedCount = FixAllMaterials();
        ApplyAoE4Lighting();

        SessionState.SetBool(SessionKey, true);
        Debug.Log($"[MaterialAndLightingFixer] Complete! Normalized {fixedCount} materials to solid Opaque & applied AoE4 lighting.");
    }

    public static int FixAllMaterials()
    {
        HashSet<Material> candidateMaterials = new HashSet<Material>();

        // 1. Scan project folders for materials
        string[] searchFolders = new string[] { "Assets/Prefabs", "Assets/Materials", "Assets/ressources", "Assets/Models" };
        List<string> existingFolders = new List<string>();
        foreach (var folder in searchFolders)
        {
            if (AssetDatabase.IsValidFolder(folder))
                existingFolders.Add(folder);
        }

        string[] matGuids = existingFolders.Count > 0
            ? AssetDatabase.FindAssets("t:Material", existingFolders.ToArray())
            : AssetDatabase.FindAssets("t:Material");

        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (ShouldSkipPath(path)) continue;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) candidateMaterials.Add(mat);
        }

        // 2. Scan all prefabs in Assets/Prefabs to catch any referenced materials
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r == null || r.sharedMaterials == null) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m != null && !ShouldSkipPath(AssetDatabase.GetAssetPath(m)))
                    {
                        candidateMaterials.Add(m);
                    }
                }
            }
        }

        int modifiedCount = 0;
        foreach (Material mat in candidateMaterials)
        {
            if (mat == null) continue;
            string matName = mat.name.ToLower();

            // Skip fog, ghost, selection, particle, line, ui
            if (matName.Contains("fog") ||
                matName.Contains("ghost") ||
                matName.Contains("selection") ||
                matName.Contains("particle") ||
                matName.Contains("line") ||
                matName.Contains("ui"))
            {
                continue;
            }

            bool dirty = false;

            // Force Standard shader (and similar PBR shaders) to Opaque mode
            if (mat.HasProperty("_Mode"))
            {
                float mode = mat.GetFloat("_Mode");
                if (mode != 0f)
                {
                    mat.SetFloat("_Mode", 0f);
                    dirty = true;
                }
            }

            if (mat.HasProperty("_SrcBlend"))
            {
                int src = mat.GetInt("_SrcBlend");
                if (src != (int)UnityEngine.Rendering.BlendMode.One)
                {
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    dirty = true;
                }
            }

            if (mat.HasProperty("_DstBlend"))
            {
                int dst = mat.GetInt("_DstBlend");
                if (dst != (int)UnityEngine.Rendering.BlendMode.Zero)
                {
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    dirty = true;
                }
            }

            if (mat.HasProperty("_ZWrite"))
            {
                int zw = mat.GetInt("_ZWrite");
                if (zw != 1)
                {
                    mat.SetInt("_ZWrite", 1);
                    dirty = true;
                }
            }

            if (mat.IsKeywordEnabled("_ALPHATEST_ON"))
            {
                mat.DisableKeyword("_ALPHATEST_ON");
                dirty = true;
            }
            if (mat.IsKeywordEnabled("_ALPHABLEND_ON"))
            {
                mat.DisableKeyword("_ALPHABLEND_ON");
                dirty = true;
            }
            if (mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
            {
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                dirty = true;
            }

            if (mat.renderQueue != -1)
            {
                mat.renderQueue = -1;
                dirty = true;
            }

            // Ensure color alpha is 1.0f
            if (mat.HasProperty("_Color"))
            {
                Color c = mat.color;
                if (c.a < 0.999f)
                {
                    c.a = 1.0f;
                    mat.color = c;
                    dirty = true;
                }
            }

            // Clamp smoothness / glossiness to natural PBR (0.15f - 0.25f)
            if (mat.HasProperty("_Glossiness"))
            {
                float gloss = mat.GetFloat("_Glossiness");
                if (gloss > 0.25f || gloss < 0.15f)
                {
                    mat.SetFloat("_Glossiness", Mathf.Clamp(gloss, 0.15f, 0.25f));
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(mat);
                modifiedCount++;
            }
        }

        if (modifiedCount > 0)
        {
            AssetDatabase.SaveAssets();
        }

        return modifiedCount;
    }

    private static bool ShouldSkipPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string lower = path.ToLower();
        if (lower.Contains("mirror/examples") || lower.Contains("textmesh pro/examples"))
            return true;
        return false;
    }

    public static void ApplyAoE4Lighting()
    {
        // 1. Ambient atmosphere
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.68f, 0.74f, 0.85f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.52f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.30f, 0.25f);
        RenderSettings.ambientIntensity = 1.0f;

        // 2. Main Directional Light (Sun)
        Light sun = RenderSettings.sun;
        if (sun == null)
        {
            Light[] lights = UnityEngine.Object.FindObjectsOfType<Light>();
            foreach (Light l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    sun = l;
                    break;
                }
            }
        }

        if (sun != null)
        {
            sun.color = new Color(1.0f, 0.88f, 0.72f, 1.0f); // Warm golden AoE4 sunlight
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(sun);
        }

        // Save scene if open and dirty
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (scene.isLoaded && !Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
    }
}
