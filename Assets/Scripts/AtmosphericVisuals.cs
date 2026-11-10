using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Age of Empires IV Atmospheric Visuals System:
/// 1. Attaches AoE4CameraPostProcess to Camera.main (Hidden/AoE4PostProcess: SSAO, warm grading, bloom, vignette).
/// 2. Adds grounded PBR Cobblestone Courtyard Aprons under all buildings (HomeSite, Barracks, Farm, LumberMill, Market, Tower).
/// 3. Renders ~1,000 GPU-instanced 3D swaying meadow grass & wildflower tufts in a single Graphics.DrawMeshInstanced call per frame.
/// </summary>
public class AtmosphericVisuals : MonoBehaviour
{
    private static AtmosphericVisuals instance;

    // Courtyard apron cache & material
    private static Material courtyardMaterial;
    private readonly HashSet<Building> processedBuildings = new HashSet<Building>();
    private float nextBuildingScanTime = 0f;

    // GPU Grass Instancing
    private const int MaxTuftInstances = 1023; // Maximum allowed in a single Graphics.DrawMeshInstanced call
    private Mesh grassMesh;
    private Material grassMaterial;
    private MaterialPropertyBlock grassPropertyBlock;
    private Matrix4x4[] instancedMatrices;
    private Vector3[] basePositions;
    private Quaternion[] baseRotations;
    private Vector3[] baseScales;
    private Vector4[] colorArray;
    private Matrix4x4[] visibleMatrices;
    private Vector4[] visibleColors;
    private MaterialPropertyBlock visiblePropertyBlock;
    private int tuftCount = 0;
    private bool grassInitialized = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Initialize()
    {
        if (instance != null) return;

        GameObject host = new GameObject("AoE4_AtmosphericVisuals");
        instance = host.AddComponent<AtmosphericVisuals>();
        DontDestroyOnLoad(host);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;

        SetupPostProcessing();
        InitCourtyardMaterial();
        ScanAndApplyCourtyards();
        InitGrassTufts();
    }

    private void Start()
    {
        SetupPostProcessing();
        ScanAndApplyCourtyards();
    }

    private void Update()
    {
        // Periodic check for new camera or newly placed buildings
        if (Time.time >= nextBuildingScanTime)
        {
            nextBuildingScanTime = Time.time + 1.5f;
            SetupPostProcessing();
            ScanAndApplyCourtyards();
        }

        // Render GPU-instanced grass tufts every frame
        RenderInstancedGrass();
    }

    // =========================================================================
    // 1. CAMERA POST-PROCESSING
    // =========================================================================
    public static void SetupPostProcessing()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = FindObjectOfType<Camera>();
        if (cam == null) return;

        AoE4CameraPostProcess pp = cam.GetComponent<AoE4CameraPostProcess>();
        if (pp == null)
        {
            pp = cam.gameObject.AddComponent<AoE4CameraPostProcess>();
        }
    }

    // =========================================================================
    // 2. COBBLESTONE COURTYARD APRONS UNDER BUILDINGS
    // =========================================================================
    private void InitCourtyardMaterial()
    {
        if (courtyardMaterial != null) return;

#if UNITY_EDITOR
        courtyardMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/AoE4PBR/CourtyardCobble.mat");
#endif

        if (courtyardMaterial == null)
        {
            Shader stdShader = Shader.Find("Standard");
            if (stdShader != null)
            {
                courtyardMaterial = new Material(stdShader)
                {
                    name = "AoE4_CourtyardCobble_Runtime"
                };

#if UNITY_EDITOR
                Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/PBR/aoe4_cobble_courtyard_albedo.png");
                Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/PBR/aoe4_cobble_courtyard_normal.png");
                if (albedo != null) courtyardMaterial.mainTexture = albedo;
                if (normal != null)
                {
                    courtyardMaterial.SetTexture("_BumpMap", normal);
                    courtyardMaterial.EnableKeyword("_NORMALMAP");
                    courtyardMaterial.SetFloat("_BumpScale", 1.1f);
                }
#endif
                courtyardMaterial.SetFloat("_Glossiness", 0.20f);
                courtyardMaterial.SetFloat("_Metallic", 0.05f);
                courtyardMaterial.SetFloat("_Mode", 0f); // Opaque
            }
        }
    }

    public void ScanAndApplyCourtyards()
    {
        // KayKit buildings have authentic sculpted 3D foundations that sit naturally on terrain.
        // Clean up any lingering legacy CourtyardApron objects.
        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building b in buildings)
        {
            if (b == null) continue;
            Transform existing = b.transform.Find("CourtyardApron");
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
        }
    }

    private void AddCourtyardApron(Building building)
    {
        if (building == null) return;
        Transform existing = building.transform.Find("CourtyardApron");
        if (existing != null) return;

        string bName = building.gameObject.name;
        float width = 5.2f;
        float depth = 5.2f;

        BoxCollider bc = building.GetComponent<BoxCollider>();
        if (bc != null)
        {
            Vector3 lossy = building.transform.lossyScale;
            width = Mathf.Max(bc.size.x * (lossy.x > 0 ? lossy.x : 1f) + 1.8f, 3.8f);
            depth = Mathf.Max(bc.size.z * (lossy.z > 0 ? lossy.z : 1f) + 1.8f, 3.8f);
        }
        else
        {
            if (bName.Contains("HomeSite") || bName.Contains("TownCenter")) { width = 7.4f; depth = 5.4f; }
            else if (bName.Contains("Barracks")) { width = 6.2f; depth = 5.2f; }
            else if (bName.Contains("Farm")) { width = 5.4f; depth = 5.4f; }
            else if (bName.Contains("Tower")) { width = 4.2f; depth = 4.2f; }
            else if (bName.Contains("Market")) { width = 5.4f; depth = 4.6f; }
            else if (bName.Contains("LumberMill")) { width = 5.4f; depth = 4.8f; }
        }

        GameObject apronObj = new GameObject("CourtyardApron");
        // Place grounded quad at Y = 0.02f above building ground level
        Vector3 basePos = building.transform.position;
        basePos.y += 0.02f;
        apronObj.transform.position = basePos;
        apronObj.transform.rotation = Quaternion.identity;
        apronObj.transform.SetParent(building.transform, true);

        // Generate clean horizontal quad mesh (normal facing +Y)
        MeshFilter mf = apronObj.AddComponent<MeshFilter>();
        MeshRenderer mr = apronObj.AddComponent<MeshRenderer>();

        Mesh mesh = new Mesh { name = "CourtyardApronMesh" };
        float hw = width * 0.5f;
        float hd = depth * 0.5f;

        mesh.vertices = new Vector3[]
        {
            new Vector3(-hw, 0f, -hd),
            new Vector3( hw, 0f, -hd),
            new Vector3(-hw, 0f,  hd),
            new Vector3( hw, 0f,  hd)
        };

        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

        // UV coordinates scaled to cobblestone pattern (1 tile per 2.2m)
        float uMax = width / 2.2f;
        float vMax = depth / 2.2f;
        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(uMax, 0f),
            new Vector2(0f, vMax),
            new Vector2(uMax, vMax)
        };

        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        if (courtyardMaterial != null)
        {
            mr.sharedMaterial = courtyardMaterial;
        }

        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = true;

        // Ensure collider is strictly removed so raycasts and pathfinding are unaffected
        Collider col = apronObj.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }

    // =========================================================================
    // 3. GPU-INSTANCED 3D SWAYING MEADOW GRASS & WILDFLOWER TUFTS
    // =========================================================================
    private void InitGrassTufts()
    {
        if (grassInitialized) return;

        grassMesh = CreateCrossQuadTuftMesh();
        InitGrassMaterial();

        tuftCount = MaxTuftInstances;
        instancedMatrices = new Matrix4x4[tuftCount];
        basePositions = new Vector3[tuftCount];
        baseRotations = new Quaternion[tuftCount];
        baseScales = new Vector3[tuftCount];

        colorArray = new Vector4[tuftCount];
        grassPropertyBlock = new MaterialPropertyBlock();

        Terrain terrain = Terrain.activeTerrain;
        float terrainY = terrain != null ? terrain.transform.position.y : 1.0f;

        // Collect building and resource exclusion zones
        List<Vector3> exclusionPositions = new List<Vector3>();
        List<float> exclusionRadii = new List<float>();

        Building[] buildings = FindObjectsOfType<Building>();
        foreach (Building b in buildings)
        {
            if (b == null) continue;
            float r = 5.5f;
            string bName = b.gameObject.name;
            if (bName.Contains("HomeSite") || bName.Contains("TownCenter")) r = 9.5f;
            else if (bName.Contains("Farm")) r = 4.8f;
            exclusionPositions.Add(b.transform.position);
            exclusionRadii.Add(r);
        }

        // Also exclude gold mines and resource nodes
        ResourceNode[] resourceNodes = FindObjectsOfType<ResourceNode>();
        foreach (ResourceNode rn in resourceNodes)
        {
            if (rn != null)
            {
                exclusionPositions.Add(rn.transform.position);
                exclusionRadii.Add(3.5f);
            }
        }

        // Scatter tufts pseudo-randomly across meadow
        Random.InitState(4242);
        int spawned = 0;
        int attempts = 0;

        while (spawned < tuftCount && attempts < 8000)
        {
            attempts++;
            // Meadow range: -88m to 88m
            float x = Random.Range(-88f, 88f);
            float z = Random.Range(-88f, 88f);

            // Check exclusion zones
            bool insideExclusion = false;
            for (int k = 0; k < exclusionPositions.Count; k++)
            {
                float dx = x - exclusionPositions[k].x;
                float dz = z - exclusionPositions[k].z;
                if ((dx * dx + dz * dz) < (exclusionRadii[k] * exclusionRadii[k]))
                {
                    insideExclusion = true;
                    break;
                }
            }

            if (insideExclusion) continue;

            float y = terrainY;
            if (terrain != null)
            {
                y = terrain.SampleHeight(new Vector3(x, 0, z)) + terrainY;
            }

            Vector3 pos = new Vector3(x, y, z);
            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            float uniformScale = Random.Range(0.85f, 1.25f);
            Vector3 scale = new Vector3(uniformScale, Random.Range(0.85f, 1.15f), uniformScale);

            basePositions[spawned] = pos;
            baseRotations[spawned] = rot;
            baseScales[spawned] = scale;
            instancedMatrices[spawned] = Matrix4x4.TRS(pos, rot, scale);

            // 85% lush meadow green gradients, 15% wildflower accents
            float flowerRoll = Random.value;
            if (flowerRoll < 0.15f)
            {
                float subType = Random.value;
                if (subType < 0.35f)
                    colorArray[spawned] = new Vector4(0.96f, 0.86f, 0.22f, 1f); // Golden Buttercup
                else if (subType < 0.70f)
                    colorArray[spawned] = new Vector4(0.92f, 0.94f, 0.98f, 1f); // Chamomile White
                else
                    colorArray[spawned] = new Vector4(0.72f, 0.54f, 0.86f, 1f); // Heather Lavender
            }
            else
            {
                float tint = Random.Range(0.88f, 1.14f);
                colorArray[spawned] = new Vector4(0.42f * tint, 0.68f * tint, 0.22f * (tint * 0.92f), 1f);
            }

            spawned++;
        }

        tuftCount = spawned;
        grassPropertyBlock.SetVectorArray("_Color", colorArray);
        grassInitialized = true;
    }

    private Mesh CreateCrossQuadTuftMesh()
    {
        Mesh mesh = new Mesh { name = "AoE4GrassTuftMesh" };

        // 3 intersecting vertical quads = 6 triangles (12 vertices), 0.35m tall
        float height = 0.35f;
        float width = 0.28f;
        float hw = width * 0.5f;

        List<Vector3> verts = new List<Vector3>();
        List<Vector3> norms = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> colors = new List<Color>();
        List<int> tris = new List<int>();

        Color baseColor = new Color(0.22f, 0.36f, 0.14f, 1f); // Dark olive base
        Color tipColor = new Color(0.50f, 0.80f, 0.24f, 1f);  // Bright emerald tip

        for (int q = 0; q < 3; q++)
        {
            float angle = q * 60f * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            int startIdx = verts.Count;

            verts.Add(-dir * hw);
            verts.Add( dir * hw);
            verts.Add(-dir * (hw * 1.25f) + Vector3.up * height);
            verts.Add( dir * (hw * 1.25f) + Vector3.up * height);

            norms.Add(Vector3.up);
            norms.Add(Vector3.up);
            norms.Add(Vector3.up);
            norms.Add(Vector3.up);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0f, 1f));
            uvs.Add(new Vector2(1f, 1f));

            colors.Add(baseColor);
            colors.Add(baseColor);
            colors.Add(tipColor);
            colors.Add(tipColor);

            // Double-sided rendering: 2 triangles front, 2 back
            tris.Add(startIdx + 0);
            tris.Add(startIdx + 2);
            tris.Add(startIdx + 1);

            tris.Add(startIdx + 2);
            tris.Add(startIdx + 3);
            tris.Add(startIdx + 1);

            tris.Add(startIdx + 0);
            tris.Add(startIdx + 1);
            tris.Add(startIdx + 2);

            tris.Add(startIdx + 2);
            tris.Add(startIdx + 1);
            tris.Add(startIdx + 3);
        }

        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private void InitGrassMaterial()
    {
        if (grassMaterial != null) return;

        Shader shader = Shader.Find("Mobile/Diffuse");
        if (shader == null) shader = Shader.Find("Standard");

        grassMaterial = new Material(shader)
        {
            name = "AoE4GrassInstancedMaterial",
            enableInstancing = true
        };

#if UNITY_EDITOR
        Texture2D meadowTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/PBR/aoe4_meadow_albedo.png");
        if (meadowTex != null) grassMaterial.mainTexture = meadowTex;
#endif

        if (grassMaterial.HasProperty("_Glossiness"))
            grassMaterial.SetFloat("_Glossiness", 0.12f);
    }

    private void RenderInstancedGrass()
    {
        if (!grassInitialized || tuftCount <= 0 || grassMesh == null || grassMaterial == null)
            return;

        if (visibleMatrices == null || visibleMatrices.Length != MaxTuftInstances)
        {
            visibleMatrices = new Matrix4x4[MaxTuftInstances];
            visibleColors = new Vector4[MaxTuftInstances];
            visiblePropertyBlock = new MaterialPropertyBlock();
        }

        FogOfWar fow = FogOfWar.Instance;
        float time = Time.time * 2.2f;
        int visibleCount = 0;

        for (int i = 0; i < tuftCount; i++)
        {
            Vector3 pos = basePositions[i];

            // Filter out grass tufts in unexplored black fog shroud
            if (fow != null && !fow.IsExplored(pos))
            {
                continue;
            }

            float windAngle = Mathf.Sin(time + pos.x * 0.32f + pos.z * 0.24f) * 0.08f;
            float gustAngle = Mathf.Cos(time * 0.85f + pos.x * 0.14f) * 0.04f;

            Quaternion sway = baseRotations[i] * Quaternion.Euler(windAngle * 57.3f, 0f, gustAngle * 57.3f);
            visibleMatrices[visibleCount] = Matrix4x4.TRS(pos, sway, baseScales[i]);
            visibleColors[visibleCount] = (colorArray != null && i < colorArray.Length) ? colorArray[i] : new Vector4(0.42f, 0.68f, 0.22f, 1f);
            visibleCount++;
        }

        if (visibleCount > 0)
        {
            visiblePropertyBlock.SetVectorArray("_Color", visibleColors);
            Graphics.DrawMeshInstanced(
                grassMesh,
                0,
                grassMaterial,
                visibleMatrices,
                visibleCount,
                visiblePropertyBlock,
                ShadowCastingMode.Off,
                true
            );
        }
    }

    private void OnDestroy()
    {
        if (grassMesh != null) Destroy(grassMesh);
        if (grassMaterial != null) Destroy(grassMaterial);
    }
}

/// <summary>
/// Camera image-effect host that applies Hidden/AoE4PostProcess to Camera.main
/// </summary>
[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
public class AoE4CameraPostProcess : MonoBehaviour
{
    private Material postProcessMaterial;

    private void Awake()
    {
        InitMaterial();
    }

    private void InitMaterial()
    {
        if (postProcessMaterial == null)
        {
            Shader shader = Shader.Find("Hidden/AoE4PostProcess");
            if (shader != null && shader.isSupported)
            {
                postProcessMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.DontSave
                };
            }
        }
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (postProcessMaterial == null)
        {
            InitMaterial();
        }

        if (postProcessMaterial != null)
        {
            Graphics.Blit(src, dest, postProcessMaterial);
        }
        else
        {
            Graphics.Blit(src, dest);
        }
    }

    private void OnDestroy()
    {
        if (postProcessMaterial != null)
        {
            DestroyImmediate(postProcessMaterial);
        }
    }
}
