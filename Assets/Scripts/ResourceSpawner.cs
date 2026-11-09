using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// ResourceSpawner — AoE4 competitive map generation & resource distribution.
/// 
/// Solves map clutter by emulating Age of Empires IV map balance:
/// 1. Base Clearing: Guaranteed wide open meadow (22m+ radius) around every Town Center.
/// 2. Guaranteed Balanced Starting Resources per Base:
///    - Starting Woodline Grove: 12–14 trees clustered at 16m–20m (rear flank).
///    - Starting Gold Vein: 2 gold mines placed 3.5m apart at 15m–18m (side flank).
///    - Starting Deer Herd: 5 deer grazing together at 14m–17m (opposite flank).
/// 3. Wide Open Tactical Meadows:
///    - 14–16 natural Forest Groves (10–14 trees each) with 24m+ tactical corridors between forests.
///    - 10–14 strategic neutral Gold Deposits across the map (25m+ apart).
///    - 7–8 wild Deer Herds grazing across the plains (4–5 deer each).
/// Total counts: ~180–220 trees, ~18–24 gold mines, ~40–50 deer (down from 729 random objects).
/// </summary>
public class ResourceSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject treePrefab;
    [SerializeField] private GameObject minePrefab;
    [SerializeField] private GameObject animalPrefab;

    [Header("=== AOE4 BASE CLEARING & CORRIDORS ===")]
    [Tooltip("Keep at least this distance clear around every Town Center and building.")]
    [SerializeField] private float baseClearRadius = 22f;
    [Tooltip("Inner sanctuary radius around Town Center (0m-14.5m) 100% free of trees and mines.")]
    [SerializeField] private float startInnerClearRadius = 14.5f;
    [SerializeField] private float buildingClearance = 5.0f;

    [Header("=== AOE4 RESOURCE TARGETS ===")]
    [Tooltip("Target natural forest groves across the map.")]
    [SerializeField] private int targetForestGroves = 15;
    [SerializeField] private int treesPerGroveMin = 10;
    [SerializeField] private int treesPerGroveMax = 14;
    [Tooltip("Target neutral strategic gold deposits across the map.")]
    [SerializeField] private int targetStrategicGold = 12;
    [Tooltip("Target wild deer herds across open grasslands.")]
    [SerializeField] private int targetWildHerds = 7;
    [SerializeField] private int deerPerHerd = 5;

    [Header("=== STARTING RESOURCES PER BASE ===")]
    [SerializeField] private int startingTrees = 13;
    [SerializeField] private int startingMines = 2;
    [SerializeField] private int startingDeer = 5;
    [SerializeField] private float startWoodlineDistance = 17.5f;
    [SerializeField] private float startGoldDistance = 16.5f;
    [SerializeField] private float startDeerDistance = 15.5f;

    [Header("=== SPACING ===")]
    [SerializeField] private float minTreeDistance = 2.4f;
    [SerializeField] private float minMineDistance = 3.2f;
    [SerializeField] private float minAnimalDistance = 2.1f;
    [SerializeField] private bool ignoreNavMesh = false;

    [Header("=== VARIATION ===")]
    [SerializeField] private bool randomYaw = true;
    [SerializeField] private float minScale = 0.85f;
    [SerializeField] private float maxScale = 1.18f;

    [Header("=== MAP / MARGINS ===")]
    [SerializeField] private float edgeMarginFraction = 0.05f;

    [Header("=== DIAGNOSTICS ===")]
    [SerializeField] private int randomSeed = 12345;
    [SerializeField] private bool logCoverageReport = true;

#pragma warning disable 0414
    // Legacy fields preserved to safely absorb existing serialized scene properties
    [SerializeField, HideInInspector] private float areaPerTree = 90f;
    [SerializeField, HideInInspector] private float areaPerMine = 420f;
    [SerializeField, HideInInspector] private float areaPerAnimal = 210f;
    [SerializeField, HideInInspector] private int maxNodesPerType = 900;
    [SerializeField, HideInInspector] private int treesPerStart = 26;
    [SerializeField, HideInInspector] private int minesPerStart = 7;
    [SerializeField, HideInInspector] private int animalsPerStart = 12;
    [SerializeField, HideInInspector] private float startMinRadius = 11f;
    [SerializeField, HideInInspector] private float startMaxRadius = 34f;
    [SerializeField, HideInInspector] private float extraGap = 1.6f;
    [SerializeField, HideInInspector] private float groveFraction = 0.45f;
    [SerializeField, HideInInspector] private int groveSizeMin = 4;
    [SerializeField, HideInInspector] private int groveSizeMax = 9;
    [SerializeField, HideInInspector] private float groveRadius = 11f;
#pragma warning restore 0414

    // ── runtime state ──────────────────────────────────────────────────────
    private float mapHalfSize = 100f;
    private float limit;
    private float minNodeSeparationSqr;

    private readonly List<Vector3> placed = new List<Vector3>();
    private readonly SpatialHash hash = new SpatialHash(12f);
    private readonly List<Vector3> buildingPositions = new List<Vector3>();
    private readonly List<Vector3> goldDepositPositions = new List<Vector3>();
    private readonly Dictionary<GameObject, int> counts = new Dictionary<GameObject, int>();
    private readonly Dictionary<GameObject, int> misses = new Dictionary<GameObject, int>();
    private readonly Dictionary<GameObject, float> footprints = new Dictionary<GameObject, float>();

    private void Awake()
    {
        // Enforce clean AoE4 balance and override bloated serialized values
        baseClearRadius = 22f;
        startInnerClearRadius = 14.5f;
        buildingClearance = 5.0f;

        targetForestGroves = 15;
        treesPerGroveMin = 10;
        treesPerGroveMax = 14;
        targetStrategicGold = 12;
        targetWildHerds = 7;
        deerPerHerd = 5;

        startingTrees = 13;
        startingMines = 2;
        startingDeer = 5;
        startWoodlineDistance = 17.5f;
        startGoldDistance = 16.5f;
        startDeerDistance = 15.5f;

        minTreeDistance = 2.4f;
        minMineDistance = 3.2f;
        minAnimalDistance = 2.1f;
    }

    private void Start()
    {
        if (MapBoundary.Instance != null) mapHalfSize = MapBoundary.Instance.HalfSize;
        limit = mapHalfSize * (1f - edgeMarginFraction);

        Random.InitState(randomSeed);

        minNodeSeparationSqr = minTreeDistance * minTreeDistance;

        StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        // Wait 1 frame so BuildingSpawner initializes Town Centers and Barracks
        yield return null;

        CollectBuildings();
        List<Vector3> basePositions = CollectBasePositions();

        float mapArea = (mapHalfSize * 2f) * (mapHalfSize * 2f);
        Debug.Log($"[ResourceSpawner] Map {mapHalfSize * 2f:F0}x{mapHalfSize * 2f:F0} ({mapArea:F0} sq u) | " +
                  $"Detected {basePositions.Count} base(s) | Enforcing AoE4 layout (baseClearRadius={baseClearRadius:F1}m)");

        // 1. Guaranteed Balanced Starting Resources per Base (clean 14.5m inner meadow)
        SpawnBalancedStartingResources(basePositions);

        // 2. Natural Forest Groves across the map (wide tactical corridors between groves)
        SpawnForestGroves(basePositions);

        // 3. Strategic Neutral Gold Deposits (25m+ separation)
        SpawnStrategicGoldDeposits(basePositions);

        // 4. Wild Deer Herds in open grasslands
        SpawnWildDeerHerds(basePositions);

        if (logCoverageReport)
            Report(mapArea);
    }

    private void CollectBuildings()
    {
        buildingPositions.Clear();
        Building[] bs = FindObjectsOfType<Building>();
        for (int i = 0; i < bs.Length; i++)
        {
            if (bs[i] != null)
                buildingPositions.Add(bs[i].transform.position);
        }
    }

    private List<Vector3> CollectBasePositions()
    {
        var bases = new List<Vector3>();

        // 1. Use BuildingSpawner recorded base positions if available
        if (BuildingSpawner.BasePositions != null && BuildingSpawner.BasePositions.Count > 0)
        {
            for (int i = 0; i < BuildingSpawner.BasePositions.Count; i++)
            {
                Vector3 p = BuildingSpawner.BasePositions[i];
                if (!bases.Exists(b => (b - p).sqrMagnitude < 4f))
                    bases.Add(p);
            }
        }

        // 2. Check for HomeSite / TownCenter instances in the scene
        Building[] allBuildings = FindObjectsOfType<Building>();
        for (int i = 0; i < allBuildings.Length; i++)
        {
            if (allBuildings[i] == null) continue;
            string bName = allBuildings[i].gameObject.name;
            if (bName.IndexOf("HomeSite", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                bName.IndexOf("TownCenter", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Vector3 p = allBuildings[i].transform.position;
                if (!bases.Exists(b => (b - p).sqrMagnitude < 4f))
                    bases.Add(p);
            }
        }

        // 3. Fallback to SpawnAreaManager spawn points
        if (bases.Count == 0)
        {
            SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
            if (area != null)
            {
                int count = area.GetSpawnCount();
                for (int i = 0; i < count; i++)
                {
                    Vector3 sp = area.GetSpawnPosition(i);
                    Vector3 toCenter = (Vector3.zero - sp).normalized;
                    if (toCenter.sqrMagnitude > 0.001f)
                        sp += toCenter * 4f;
                    if (!bases.Exists(b => (b - sp).sqrMagnitude < 4f))
                        bases.Add(sp);
                }
            }
        }

        // 4. Default to center if no bases exist
        if (bases.Count == 0)
        {
            bases.Add(Vector3.zero);
            Debug.LogWarning("[ResourceSpawner] No bases found; anchoring starting resources to map center.");
        }

        return bases;
    }

    // ── STAGE 1: BALANCED STARTING RESOURCES ──────────────────────────────

    /// <summary>
    /// Spawns guaranteed, fair starting resources for each player base (AoE4 style):
    /// - 0m to 14.5m: 100% clear inner town meadow.
    /// - 16m to 20m rear flank: 1 Starting Woodline Grove (12-14 trees).
    /// - 15m to 18m side flank: 1 Starting Gold Vein (2 gold mines, 3.5m apart).
    /// - 14m to 17m opposite flank: 1 Starting Deer Herd (5 deer, 2.2m apart).
    /// </summary>
    public void SpawnBalancedStartingResources(List<Vector3> basePositions)
    {
        if (basePositions == null || basePositions.Count == 0) return;

        for (int b = 0; b < basePositions.Count; b++)
        {
            Vector3 basePos = basePositions[b];
            Vector3 toCenter = (Vector3.zero - basePos).normalized;
            if (toCenter.sqrMagnitude < 0.001f)
                toCenter = Vector3.forward;
            Vector3 side = Vector3.Cross(toCenter, Vector3.up).normalized;

            // 1. Starting Woodline Grove on rear flank (~135 deg from center facing)
            Vector3 woodDir = (-toCenter * 0.75f + side * 0.65f).normalized;
            Vector3 woodCenter = basePos + woodDir * startWoodlineDistance;
            woodCenter = ClampToMapWithMargin(woodCenter, 8f);
            SpawnWoodlineCluster(woodCenter, basePos, startingTrees, 6.5f);

            // 2. Starting Gold Vein on side flank (~70 deg from center facing)
            Vector3 goldDir = (toCenter * 0.35f + side * 0.94f).normalized;
            Vector3 goldCenter = basePos + goldDir * startGoldDistance;
            goldCenter = ClampToMapWithMargin(goldCenter, 6f);
            SpawnStartingGoldVein(goldCenter, basePos, goldDir, startingMines);

            // 3. Starting Deer Herd on opposite flank (~-70 deg from center facing)
            Vector3 deerDir = (toCenter * 0.35f - side * 0.94f).normalized;
            Vector3 deerCenter = basePos + deerDir * startDeerDistance;
            deerCenter = ClampToMapWithMargin(deerCenter, 6f);
            SpawnStartingDeerHerd(deerCenter, basePos, startingDeer);
        }
    }

    private void SpawnWoodlineCluster(Vector3 woodCenter, Vector3 basePos, int count, float radius)
    {
        if (treePrefab == null || count <= 0) return;
        int spawned = 0;

        // Place center anchor tree
        if (Vector3.Distance(woodCenter, basePos) >= startInnerClearRadius)
        {
            if (TryPlace(treePrefab, woodCenter, minTreeDistance * minTreeDistance, out _))
                spawned++;
        }

        int attempts = 0;
        while (spawned < count && attempts < 90)
        {
            attempts++;
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 p = woodCenter + new Vector3(r.x, 0f, r.y);

            // Strictly preserve inner town clearing (0m to 14.5m)
            if (Vector3.Distance(p, basePos) < startInnerClearRadius)
                continue;

            if (TryPlace(treePrefab, p, minTreeDistance * minTreeDistance, out _))
                spawned++;
        }
    }

    private void SpawnStartingGoldVein(Vector3 goldCenter, Vector3 basePos, Vector3 goldDir, int count)
    {
        if (minePrefab == null || count <= 0) return;
        Vector3 tangent = Vector3.Cross(goldDir, Vector3.up).normalized;
        float spacing = 3.5f;

        for (int i = 0; i < count; i++)
        {
            float offset = (i - (count - 1) * 0.5f) * spacing;
            Vector3 p = goldCenter + tangent * offset;

            if (Vector3.Distance(p, basePos) < startInnerClearRadius)
                continue;

            p = AlignToGround(p);
            if (!ClearOfBuildings(p, buildingClearance)) continue;
            if (!ClearOfNodes(p, minMineDistance * minMineDistance)) continue;

            Quaternion rot = Quaternion.LookRotation(-goldDir) * Quaternion.Euler(0f, Random.Range(-15f, 15f), 0f);
            GameObject mine = Instantiate(minePrefab, p, rot);
            mine.transform.localScale = ScaleFor(minePrefab);

            hash.Add(p);
            placed.Add(p);
            goldDepositPositions.Add(p);
            Bump(counts, minePrefab, 1);
        }
    }

    private void SpawnStartingDeerHerd(Vector3 deerCenter, Vector3 basePos, int count)
    {
        if (animalPrefab == null || count <= 0) return;
        int herdId = AnimalNode.AllocateHerdId();
        int spawned = 0;
        int attempts = 0;

        while (spawned < count && attempts < 60)
        {
            attempts++;
            Vector2 r = Random.insideUnitCircle * 3.2f;
            Vector3 p = deerCenter + new Vector3(r.x, 0f, r.y);

            if (Vector3.Distance(p, basePos) < 14.0f)
                continue;

            if (TryPlace(animalPrefab, p, minAnimalDistance * minAnimalDistance, out GameObject animal))
            {
                if (animal.TryGetComponent(out AnimalNode node))
                    node.herdId = herdId;
                spawned++;
            }
        }
    }

    // ── STAGE 2: NATURAL FOREST GROVES ────────────────────────────────────

    /// <summary>
    /// Spawns 14–16 Forest Groves across the map with 24m+ tactical corridors between groves.
    /// Trees never spawn as single isolated noise, creating authentic AoE4 tactical gameplay.
    /// </summary>
    public void SpawnForestGroves(List<Vector3> basePositions)
    {
        if (treePrefab == null) return;

        List<Vector3> groveCenters = new List<Vector3>();
        float minGroveSpacing = 24f; // Wide tactical corridors between woodlines

        // Stratified cell sampling for uniform regional spread across the map
        int gridSide = 5;
        float cellSize = (limit * 2f) / gridSide;
        List<Vector2Int> cells = new List<Vector2Int>();
        for (int cx = 0; cx < gridSide; cx++)
            for (int cz = 0; cz < gridSide; cz++)
                cells.Add(new Vector2Int(cx, cz));

        // Shuffle cells
        for (int i = 0; i < cells.Count; i++)
        {
            int j = Random.Range(i, cells.Count);
            var tmp = cells[i]; cells[i] = cells[j]; cells[j] = tmp;
        }

        for (int i = 0; i < cells.Count && groveCenters.Count < targetForestGroves; i++)
        {
            var cell = cells[i];
            for (int attempt = 0; attempt < 12; attempt++)
            {
                float x = -limit + (cell.x + Random.Range(0.2f, 0.8f)) * cellSize;
                float z = -limit + (cell.y + Random.Range(0.2f, 0.8f)) * cellSize;
                Vector3 candidate = new Vector3(x, 0f, z);

                if (IsTooCloseToBase(candidate, baseClearRadius + 4f, basePositions)) continue;
                if (IsTooCloseToPoints(candidate, minGroveSpacing, groveCenters)) continue;

                groveCenters.Add(candidate);
                break;
            }
        }

        // Secondary pass if more grove centers needed
        int extraAttempts = 0;
        while (groveCenters.Count < targetForestGroves && extraAttempts < 250)
        {
            extraAttempts++;
            float x = Random.Range(-limit + 10f, limit - 10f);
            float z = Random.Range(-limit + 10f, limit - 10f);
            Vector3 candidate = new Vector3(x, 0f, z);

            if (IsTooCloseToBase(candidate, baseClearRadius + 4f, basePositions)) continue;
            if (IsTooCloseToPoints(candidate, minGroveSpacing, groveCenters)) continue;

            groveCenters.Add(candidate);
        }

        // Place clustered trees inside each forest grove
        for (int g = 0; g < groveCenters.Count; g++)
        {
            Vector3 center = groveCenters[g];
            int groveTreeTarget = Random.Range(treesPerGroveMin, treesPerGroveMax + 1);
            float groveRadius = Random.Range(7.0f, 9.0f);

            int spawned = 0;
            int attempts = 0;

            // Anchor tree
            if (!IsTooCloseToBase(center, baseClearRadius, basePositions))
            {
                if (TryPlace(treePrefab, center, minTreeDistance * minTreeDistance, out _))
                    spawned++;
            }

            while (spawned < groveTreeTarget && attempts < 70)
            {
                attempts++;
                Vector2 r = Random.insideUnitCircle * groveRadius;
                Vector3 p = center + new Vector3(r.x, 0f, r.y);

                if (IsTooCloseToBase(p, baseClearRadius, basePositions))
                    continue;

                if (TryPlace(treePrefab, p, minTreeDistance * minTreeDistance, out _))
                    spawned++;
            }
        }
    }

    // ── STAGE 3: STRATEGIC GOLD DEPOSITS ──────────────────────────────────

    /// <summary>
    /// Spawns 10–14 Strategic Gold Deposits across the map with 25m+ separation.
    /// Neutral mines are high-value map objectives placed in clear view.
    /// </summary>
    public void SpawnStrategicGoldDeposits(List<Vector3> basePositions)
    {
        if (minePrefab == null) return;

        List<Vector3> depositCenters = new List<Vector3>();
        float minDepositDist = 25f; // Generous strategic distance between gold veins

        int attempts = 0;
        while (depositCenters.Count < targetStrategicGold && attempts < 350)
        {
            attempts++;
            float x = Random.Range(-limit + 10f, limit - 10f);
            float z = Random.Range(-limit + 10f, limit - 10f);
            Vector3 candidate = new Vector3(x, 0f, z);

            if (IsTooCloseToBase(candidate, baseClearRadius + 5f, basePositions)) continue;
            if (IsTooCloseToPoints(candidate, minDepositDist, depositCenters)) continue;
            if (IsTooCloseToPoints(candidate, minDepositDist, goldDepositPositions)) continue;
            // Clear of dense forest trees
            if (hash.AnyWithin(candidate, 5.5f * 5.5f)) continue;

            depositCenters.Add(candidate);
        }

        for (int i = 0; i < depositCenters.Count; i++)
        {
            Vector3 center = depositCenters[i];
            int minesInDeposit = (Random.value < 0.35f) ? 2 : 1;
            Vector3 tan = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            float spacing = 3.5f;

            for (int m = 0; m < minesInDeposit; m++)
            {
                float offset = (m - (minesInDeposit - 1) * 0.5f) * spacing;
                Vector3 p = center + tan * offset;

                if (IsTooCloseToBase(p, baseClearRadius, basePositions)) continue;

                p = AlignToGround(p);
                if (!ClearOfBuildings(p, buildingClearance)) continue;
                if (!ClearOfNodes(p, minMineDistance * minMineDistance)) continue;

                Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                GameObject mine = Instantiate(minePrefab, p, rot);
                mine.transform.localScale = ScaleFor(minePrefab);

                hash.Add(p);
                placed.Add(p);
                goldDepositPositions.Add(p);
                Bump(counts, minePrefab, 1);
            }
        }
    }

    // ── STAGE 4: WILD DEER HERDS ──────────────────────────────────────────

    /// <summary>
    /// Spawns 7–8 Wild Deer Herds grazing peacefully in open meadows across the map.
    /// Each herd contains 4–5 deer with 20m+ separation between herds.
    /// </summary>
    public void SpawnWildDeerHerds(List<Vector3> basePositions)
    {
        if (animalPrefab == null) return;

        List<Vector3> herdCenters = new List<Vector3>();
        float minHerdDist = 20f;

        int attempts = 0;
        while (herdCenters.Count < targetWildHerds && attempts < 300)
        {
            attempts++;
            float x = Random.Range(-limit + 12f, limit - 12f);
            float z = Random.Range(-limit + 12f, limit - 12f);
            Vector3 candidate = new Vector3(x, 0f, z);

            if (IsTooCloseToBase(candidate, baseClearRadius + 4f, basePositions)) continue;
            if (IsTooCloseToPoints(candidate, minHerdDist, herdCenters)) continue;
            if (IsTooCloseToPoints(candidate, 8f, goldDepositPositions)) continue;
            // Grazing out in open clearings, not inside thick trees
            if (hash.AnyWithin(candidate, 5.0f * 5.0f)) continue;

            herdCenters.Add(candidate);
        }

        for (int h = 0; h < herdCenters.Count; h++)
        {
            Vector3 center = herdCenters[h];
            int herdId = AnimalNode.AllocateHerdId();
            int herdCount = deerPerHerd;
            int spawned = 0;
            int deerAttempts = 0;

            while (spawned < herdCount && deerAttempts < 50)
            {
                deerAttempts++;
                Vector2 r = Random.insideUnitCircle * 3.8f;
                Vector3 p = center + new Vector3(r.x, 0f, r.y);

                if (IsTooCloseToBase(p, baseClearRadius, basePositions)) continue;

                if (TryPlace(animalPrefab, p, minAnimalDistance * minAnimalDistance, out GameObject animal))
                {
                    if (animal.TryGetComponent(out AnimalNode node))
                        node.herdId = herdId;
                    spawned++;
                }
            }
        }
    }

    // ── PLACEMENT & SPATIAL HELPERS ───────────────────────────────────────

    private bool TryPlace(GameObject prefab, Vector3 want, float sepSqr, out GameObject spawned)
    {
        spawned = null;
        if (prefab == null) return false;

        Vector3 p = AlignToGround(want);

        if (!ClearOfBuildings(p, buildingClearance)) return false;
        if (!ClearOfNodes(p, sepSqr)) return false;

        Quaternion rot = Rotation(prefab);
        spawned = Instantiate(prefab, p, rot);
        spawned.transform.localScale = ScaleFor(prefab);

        hash.Add(p);
        placed.Add(p);
        Bump(counts, prefab, 1);
        return true;
    }

    private Vector3 AlignToGround(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, -limit, limit);
        p.z = Mathf.Clamp(p.z, -limit, limit);

        if (!ignoreNavMesh && NavMesh.SamplePosition(p, out NavMeshHit hit, 8f, NavMesh.AllAreas))
        {
            p = hit.position;
        }
        else if (Physics.Raycast(new Vector3(p.x, p.y + 200f, p.z), Vector3.down, out RaycastHit gHit, 400f))
        {
            p.y = gHit.point.y;
        }
        else
        {
            p.y = 0f;
        }
        return p;
    }

    private Quaternion Rotation(GameObject prefab)
        => randomYaw && prefab.GetComponent<AnimalNode>() == null
           ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
           : Quaternion.identity;

    private Vector3 ScaleFor(GameObject prefab)
    {
        if (Mathf.Approximately(minScale, 1f) && Mathf.Approximately(maxScale, 1f))
            return prefab.transform.localScale;
        float s = Random.Range(Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale));
        return prefab.transform.localScale * s;
    }

    private Vector3 ClampToMapWithMargin(Vector3 p, float margin)
    {
        float maxBound = limit - margin;
        p.x = Mathf.Clamp(p.x, -maxBound, maxBound);
        p.z = Mathf.Clamp(p.z, -maxBound, maxBound);
        p.y = 0f;
        return p;
    }

    private bool IsTooCloseToBase(Vector3 p, float minDist, List<Vector3> basePositions)
    {
        if (basePositions != null)
        {
            float sqr = minDist * minDist;
            for (int i = 0; i < basePositions.Count; i++)
            {
                if ((p - basePositions[i]).sqrMagnitude < sqr) return true;
            }
        }

        if (buildingPositions != null)
        {
            float sqr = minDist * minDist;
            for (int i = 0; i < buildingPositions.Count; i++)
            {
                if ((p - buildingPositions[i]).sqrMagnitude < sqr) return true;
            }
        }
        return false;
    }

    private bool IsTooCloseToPoints(Vector3 p, float minDist, List<Vector3> points)
    {
        if (points == null) return false;
        float sqr = minDist * minDist;
        for (int i = 0; i < points.Count; i++)
        {
            if ((p - points[i]).sqrMagnitude < sqr) return true;
        }
        return false;
    }

    private bool ClearOfBuildings(Vector3 p, float clearance)
    {
        if (buildingPositions.Count == 0 || clearance <= 0f) return true;
        float r = FootprintRadius(treePrefab) + clearance;
        float sq = r * r;
        for (int i = 0; i < buildingPositions.Count; i++)
            if ((p - buildingPositions[i]).sqrMagnitude < sq) return false;
        return true;
    }

    private bool ClearOfNodes(Vector3 p, float sepSqr)
    {
        if (sepSqr <= 0f) return true;
        return !hash.AnyWithin(p, sepSqr);
    }

    private float FootprintRadius(GameObject prefab)
    {
        if (prefab == null) return 0f;
        if (footprints.TryGetValue(prefab, out float cached)) return cached;

        float r = 0f;
        GameObject probe = null;
        try
        {
            probe = UnityEngine.Object.Instantiate(prefab);
            probe.name = "__ResourceSpawner_Measure";
            probe.hideFlags = HideFlags.HideAndDontSave;
            probe.SetActive(false);

            var rends = probe.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                Bounds b = rends[i].bounds;
                r = Mathf.Max(r, 0.5f * Mathf.Max(Mathf.Abs(b.size.x), Mathf.Abs(b.size.z)));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ResourceSpawner] Could not measure {prefab.name}: {e.Message}");
        }
        finally
        {
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
        }

        footprints[prefab] = Mathf.Max(0.5f, r);
        return footprints[prefab];
    }

    private int GetCount(GameObject prefab)
        => prefab != null && counts.TryGetValue(prefab, out int n) ? n : 0;

    private static void Bump(Dictionary<GameObject, int> d, GameObject k, int by)
    {
        d.TryGetValue(k, out int n);
        d[k] = n + by;
    }

    private void Report(float mapArea)
    {
        int t = GetCount(treePrefab), m = GetCount(minePrefab), a = GetCount(animalPrefab);
        int total = t + m + a;

        Debug.Log($"[ResourceSpawner] AoE4 MAP GENERATION COMPLETE: {t} trees / {m} gold mines / {a} animals = {total} total nodes " +
                  $"(clean baseClearRadius={baseClearRadius:F1}m, 0m-14.5m inner clearing).");

        float nearest = float.MaxValue;
        for (int i = 0; i < placed.Count; i++)
        {
            hash.Nearest(placed[i], out float d);
            if (d < nearest) nearest = d;
        }

        Debug.Log($"[ResourceSpawner] Closest distinct resource nodes: {nearest:F2}m (enforced tree min={minTreeDistance:F2}m, gold min={minMineDistance:F2}m).");
    }

    private void OnDestroy()
    {
        placed.Clear();
        hash.Clear();
        counts.Clear();
        misses.Clear();
        footprints.Clear();
        buildingPositions.Clear();
        goldDepositPositions.Clear();
    }

    /// <summary>
    /// Uniform spatial hash for O(1) proximity queries.
    /// </summary>
    private sealed class SpatialHash
    {
        private readonly float cell;
        private readonly Dictionary<long, List<Vector3>> buckets = new Dictionary<long, List<Vector3>>();

        public SpatialHash(float cellSize) { cell = Mathf.Max(0.5f, cellSize); }

        private long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void Add(Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / cell);
            int cz = Mathf.FloorToInt(p.z / cell);
            long k = Key(cx, cz);
            if (!buckets.TryGetValue(k, out var list)) buckets[k] = list = new List<Vector3>(4);
            list.Add(p);
        }

        public bool AnyWithin(Vector3 p, float sqr)
        {
            int span = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(sqr) / cell));
            int cx = Mathf.FloorToInt(p.x / cell);
            int cz = Mathf.FloorToInt(p.z / cell);
            for (int x = cx - span; x <= cx + span; x++)
            for (int z = cz - span; z <= cz + span; z++)
            {
                if (!buckets.TryGetValue(Key(x, z), out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                    if ((p - list[i]).sqrMagnitude < sqr) return true;
            }
            return false;
        }

        public void Nearest(Vector3 p, out float dist)
        {
            const float window = 40f;
            float best = float.MaxValue;
            int span = Mathf.Max(1, Mathf.CeilToInt(window / cell));
            int cx = Mathf.FloorToInt(p.x / cell);
            int cz = Mathf.FloorToInt(p.z / cell);
            for (int x = cx - span; x <= cx + span; x++)
            for (int z = cz - span; z <= cz + span; z++)
            {
                if (!buckets.TryGetValue(Key(x, z), out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    Vector3 q = list[i];
                    if ((q.x == p.x) && (q.z == p.z)) continue;
                    float d = (q - p).sqrMagnitude;
                    if (d > 0.0000001f && d < best) best = d;
                }
            }
            dist = Mathf.Sqrt(best);
        }

        public void Clear() => buckets.Clear();
    }
}
