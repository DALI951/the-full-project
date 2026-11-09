using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class PBRAndEnvironmentUpgrader
{
    private const string PbrTexturePath = "Assets/Textures/PBR";
    private const string PbrMaterialPath = "Assets/Materials/AoE4PBR";
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("Tools/Upgrade AoE4 PBR Materials And Terrain")]
    public static void UpgradeMaterialsAndTerrain()
    {
        Debug.Log("[PBRAndEnvironmentUpgrader] Starting AoE4 PBR Material & Terrain Upgrade...");

        ConfigurePbrTextureImporters();
        CreateOrUpdatePbrMaterials();
        ApplyMeadowToTerrainAndGround();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[PBRAndEnvironmentUpgrader] AoE4 PBR Upgrade complete! All materials & terrain updated with normal maps.");
    }

    /// <summary>
    /// Configures TextureImporter for all normal maps and tileable albedos in Assets/Textures/PBR/
    /// </summary>
    public static void ConfigurePbrTextureImporters()
    {
        if (!AssetDatabase.IsValidFolder(PbrTexturePath)) return;

        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new string[] { PbrTexturePath });
        int normalCount = 0;
        int albedoCount = 0;

        foreach (string guid in texGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool isNormal = path.EndsWith("_normal.png", System.StringComparison.OrdinalIgnoreCase);
            bool isAlbedo = path.EndsWith("_albedo.png", System.StringComparison.OrdinalIgnoreCase);

            bool dirty = false;

            if (isNormal)
            {
                if (importer.textureType != TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    dirty = true;
                }
                normalCount++;
            }
            else if (isAlbedo)
            {
                if (importer.textureType != TextureImporterType.Default)
                {
                    importer.textureType = TextureImporterType.Default;
                    dirty = true;
                }
                albedoCount++;
            }

            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                dirty = true;
            }

            if (importer.filterMode != FilterMode.Bilinear && importer.filterMode != FilterMode.Trilinear)
            {
                importer.filterMode = FilterMode.Bilinear;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        Debug.Log($"[PBRAndEnvironmentUpgrader] Configured {normalCount} Normal Maps and {albedoCount} Albedo Textures.");
    }

    /// <summary>
    /// Creates and configures the 17 AoE4 PBR Standard materials in Assets/Materials/AoE4PBR/
    /// </summary>
    public static void CreateOrUpdatePbrMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        if (!AssetDatabase.IsValidFolder(PbrMaterialPath))
        {
            AssetDatabase.CreateFolder("Assets/Materials", "AoE4PBR");
        }

        Shader standardShader = Shader.Find("Standard");
        if (standardShader == null)
        {
            Debug.LogError("[PBRAndEnvironmentUpgrader] Standard shader not found!");
            return;
        }

        // Material specifications: (Name, AlbedoFile, NormalFile, Tint, Metallic, Glossiness)
        var matSpecs = new (string name, string albedo, string normal, Color tint, float metallic, float gloss)[]
        {
            ("StoneWall",        "aoe4_stone_albedo.png",            "aoe4_stone_normal.png",            Color.white,                       0.02f, 0.20f),
            ("TimberFrame",      "aoe4_timber_oak_albedo.png",       "aoe4_timber_oak_normal.png",       Color.white,                       0.00f, 0.20f),
            ("TudorPlaster",     "aoe4_tudor_plaster_albedo.png",    "aoe4_tudor_plaster_normal.png",    Color.white,                       0.00f, 0.15f),
            ("TerracottaRoof",   "aoe4_roof_terracotta_albedo.png",  "aoe4_roof_terracotta_normal.png",  Color.white,                       0.00f, 0.25f),
            ("CrimsonRoof",      "aoe4_roof_crimson_albedo.png",     "aoe4_roof_crimson_normal.png",     Color.white,                       0.05f, 0.22f),
            ("SlateRoof",        "aoe4_roof_crimson_albedo.png",     "aoe4_roof_crimson_normal.png",     new Color(0.42f, 0.44f, 0.48f),   0.05f, 0.25f),
            ("GoldOre",          "aoe4_gold_ore_albedo.png",         "aoe4_gold_ore_normal.png",         Color.white,                       0.65f, 0.45f),
            ("CliffRock",        "aoe4_stone_albedo.png",            "aoe4_stone_normal.png",            new Color(0.85f, 0.82f, 0.80f),   0.00f, 0.18f),
            ("GoldenWheat",      "aoe4_wheat_albedo.png",            "aoe4_wheat_normal.png",            Color.white,                       0.00f, 0.15f),
            ("DarkSoil",         "aoe4_dark_soil_albedo.png",        "aoe4_dark_soil_normal.png",        Color.white,                       0.00f, 0.12f),
            ("BarkWood",         "aoe4_timber_oak_albedo.png",       "aoe4_timber_oak_normal.png",       new Color(0.70f, 0.62f, 0.55f),   0.00f, 0.15f),
            ("CutWood",          "aoe4_timber_oak_albedo.png",       "aoe4_timber_oak_normal.png",       new Color(1.05f, 0.95f, 0.85f),   0.00f, 0.20f),
            ("CrimsonFabric",    "aoe4_fabric_crimson_albedo.png",   "aoe4_fabric_crimson_normal.png",   Color.white,                       0.00f, 0.15f),
            ("RoyalBlueFabric",  "aoe4_fabric_blue_albedo.png",      "aoe4_fabric_blue_normal.png",      Color.white,                       0.00f, 0.15f),
            ("GoldTrim",         "aoe4_gold_ore_albedo.png",         "aoe4_gold_ore_normal.png",         new Color(1.0f, 0.85f, 0.45f),    0.85f, 0.60f),
            ("DarkArch",         "aoe4_stone_albedo.png",            "aoe4_stone_normal.png",            new Color(0.65f, 0.62f, 0.60f),   0.00f, 0.20f),
            ("CourtyardCobble",  "aoe4_cobble_courtyard_albedo.png", "aoe4_cobble_courtyard_normal.png", Color.white,                       0.02f, 0.20f),
        };

        foreach (var spec in matSpecs)
        {
            string matPath = $"{PbrMaterialPath}/{spec.name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(standardShader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = standardShader;
            }

            // Solid Opaque Mode (Mode = 0)
            mat.SetFloat("_Mode", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.One);
            mat.SetInt("_DstBlend", (int)BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = -1;

            // Albedo Texture & Color
            string albedoPath = $"{PbrTexturePath}/{spec.albedo}";
            Texture2D albedoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            if (albedoTex != null)
            {
                mat.mainTexture = albedoTex;
            }
            mat.color = spec.tint;

            // Tangent-Space Normal Map & Depth
            string normalPath = $"{PbrTexturePath}/{spec.normal}";
            Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
                mat.SetFloat("_BumpScale", 1.1f);
            }

            // PBR Smoothness & Metallic
            mat.SetFloat("_Glossiness", spec.gloss);
            mat.SetFloat("_Metallic", spec.metallic);

            EditorUtility.SetDirty(mat);
        }

        Debug.Log($"[PBRAndEnvironmentUpgrader] Created/Updated {matSpecs.Length} PBR Standard materials in {PbrMaterialPath}.");
    }

    /// <summary>
    /// Applies aoe4_meadow_albedo.png + aoe4_meadow_normal.png to the Terrain and Ground planes in GameScene.unity
    /// with crisp (45, 45) tiling and bump depth.
    /// </summary>
    public static void ApplyMeadowToTerrainAndGround()
    {
        // Ensure GameScene is loaded
        var activeScene = EditorSceneManager.GetActiveScene();
        if (activeScene.path != GameScenePath && !Application.isPlaying)
        {
            if (File.Exists(GameScenePath))
            {
                activeScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            }
        }

        Texture2D meadowAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PbrTexturePath}/aoe4_meadow_albedo.png");
        Texture2D meadowNormal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PbrTexturePath}/aoe4_meadow_normal.png");

        if (meadowAlbedo == null || meadowNormal == null)
        {
            Debug.LogError("[PBRAndEnvironmentUpgrader] Meadow Albedo or Normal texture missing!");
            return;
        }

        // 1. Terrain Application
        Terrain terrain = Object.FindObjectOfType<Terrain>();
        if (terrain != null && terrain.terrainData != null)
        {
            TerrainData tData = terrain.terrainData;
            float terrainW = tData.size.x;
            float terrainL = tData.size.z;

            // (45, 45) tiling across the terrain area
            float tileX = (terrainW > 0f) ? (terrainW / 45.0f) : 4.444f;
            float tileZ = (terrainL > 0f) ? (terrainL / 45.0f) : 4.444f;

            string layerPath = $"{PbrMaterialPath}/AoE4MeadowTerrainLayer.terrainlayer";
            TerrainLayer meadowLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (meadowLayer == null)
            {
                meadowLayer = new TerrainLayer();
                AssetDatabase.CreateAsset(meadowLayer, layerPath);
            }

            meadowLayer.diffuseTexture = meadowAlbedo;
            meadowLayer.normalMapTexture = meadowNormal;
            meadowLayer.tileSize = new Vector2(tileX, tileZ);
            meadowLayer.normalScale = 1.1f;
            meadowLayer.smoothness = 0.20f;
            meadowLayer.metallic = 0.00f;

            EditorUtility.SetDirty(meadowLayer);

            tData.terrainLayers = new TerrainLayer[] { meadowLayer };
            EditorUtility.SetDirty(tData);
            EditorUtility.SetDirty(terrain);

            Debug.Log($"[PBRAndEnvironmentUpgrader] Applied AoE4 Meadow to Terrain with (45, 45) tiling (tileSize: {tileX:F2}m x {tileZ:F2}m) & normal scale 1.1.");
        }

        // 2. Mesh-based Ground / Plane objects
        GameObject[] rootObjs = activeScene.GetRootGameObjects();
        foreach (GameObject root in rootObjs)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r == null || r.gameObject == null) continue;
                string lowerName = r.gameObject.name.ToLower();

                // Check for Ground / Floor / Meadow objects, excluding Fog of War planes
                if ((lowerName.Contains("ground") || lowerName.Contains("floor") || lowerName.Contains("meadow")) &&
                    !lowerName.Contains("fog"))
                {
                    Material groundMat = r.sharedMaterial;
                    if (groundMat != null)
                    {
                        groundMat.shader = Shader.Find("Standard");
                        groundMat.SetFloat("_Mode", 0f);
                        groundMat.mainTexture = meadowAlbedo;
                        groundMat.mainTextureScale = new Vector2(45f, 45f);
                        groundMat.SetTexture("_BumpMap", meadowNormal);
                        groundMat.SetTextureScale("_BumpMap", new Vector2(45f, 45f));
                        groundMat.EnableKeyword("_NORMALMAP");
                        groundMat.SetFloat("_BumpScale", 1.1f);
                        groundMat.SetFloat("_Glossiness", 0.20f);
                        EditorUtility.SetDirty(groundMat);
                        EditorUtility.SetDirty(r);
                    }
                }
            }
        }

        if (activeScene.isLoaded && !Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
        }
    }
}
