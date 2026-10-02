using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// ResourceSpawner v7 — balanced, size-aware, evenly-distributed resource layout.
///
/// WHY v7 EXISTS (the bugs v6 actually had, measured on the 200x200 map):
///
///  1. DENSITY WAS OFF BY ~10x. v6 used `count = per100Units * (mapArea / 10000)`.
///     On a 200x200 map (40000 sq units) that gave 80 trees TOTAL — one tree per
///     500 sq units. The code then "compensated" by cramming them into
///     `treesPerForest` blobs of 8 inside a 7-unit radius, so you got 10 dense
///     islands and a 99% empty map. That is exactly "a tree here and two there".
///     v7 uses AREA-PER-NODE (sq units per node), which is map-size independent
///     and means what it says.
///
///  2. SEPARATION WAS SMALLER THAN THE TREES. A Tree is 3.46 x 4.55 world units,
///     but minNodeSeparation was 2.5 and forests relaxed it to ~1.25. Trees
///     physically intersected. v7 derives the spacing from the prefab's real
///     renderer bounds, so it can never be smaller than the thing it places.
///
///  3. "RESOURCES NEXT TO EACH BUILDING" NEVER RAN. v6 called
///     FindObjectsOfType<Building>() and there are ZERO buildings in the scene
///     (BuildingSpawner makes them later), so `buildingPositions` was always
///     empty and Phase 1 was dead code. v7 anchors the starter ring to the
///     actual SpawnAreaManager spawn points, which DO exist, and additionally
///     respects buildings if any are present.
///
///  4. O(n^2) SEPARATION CHECKS. v6 did a linear scan of every placed position
///     for every candidate — O(n^2) with up to 100 attempts per node. v7 uses a
///     uniform spatial hash: O(1) neighbour lookup.
///
///  5. SILENT DROPS. v6 `continue`d when NavMesh sampling or separation failed and
///     only counted the failure at the end. v7 retries harder, falls back to the
///     unclamped-navmesh position, and logs a coverage report.
///
/// DISTRIBUTION MODEL:
///   Stage 1  Starter ring  — every spawn point gets the SAME number of each type
///                            in an annulus, so no player is worse off than another.
///   Stage 2  Stratified fill — the rest of the map is filled with a jittered grid
///                            (stratified sampling), which gives even coverage with
///                            natural-looking randomness and NO voids.
///   Stage 3  Loose grouping — a fraction of nodes get pulled into small groves so
///                            the map is not a uniform lawn. Bounded by the same
///                            size-aware separation, so groves never overlap.
///
/// Everything is deterministic per seed for easy A/B comparison in the Editor.
/// </summary>
public class ResourceSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject treePrefab;
    [SerializeField] private GameObject minePrefab;
    [SerializeField] private GameObject animalPrefab;

    [Header("=== DENSITY (square units per node) ===")]
    [Tooltip("Sq units of map per tree. 90 => one tree roughly every 9.5x9.5. " +
             "LOWER = more trees. Wood is the backbone resource, keep it highest.")]
    [SerializeField] private float areaPerTree = 90f;
    [Tooltip("Sq units of map per mine. Gold should be scarcer than wood.")]
    [SerializeField] private float areaPerMine = 420f;
    [Tooltip("Sq units of map per animal herd member.")]
    [SerializeField] private float areaPerAnimal = 210f;
    [Tooltip("Hard cap so a huge map can never explode the object count.")]
    [SerializeField] private int maxNodesPerType = 900;

    [Header("=== STARTER RING (fair start) ===")]
    [Tooltip("Every spawn point gets this many of each type. Equal for every player.")]
    [SerializeField] private int treesPerStart = 26;
    [SerializeField] private int minesPerStart = 7;
    [SerializeField] private int animalsPerStart = 12;
    [Tooltip("Starter ring inner radius from the spawn point.")]
    [SerializeField] private float startMinRadius = 11f;
    [Tooltip("Starter ring outer radius from the spawn point.")]
    [SerializeField] private float startMaxRadius = 34f;

    [Header("=== SPACING ===")]
    [Tooltip("Extra gap between two nodes ON TOP OF their footprint, in units. " +
             "0 = touching. This is what stops trees growing through each other.")]
    [SerializeField] private float extraGap = 1.6f;
    [Tooltip("Keep this much clear space around buildings (0 = allow).")]
    [SerializeField] private float buildingClearance = 4.5f;
    [Tooltip("Skip NavMesh sampling entirely and place on the clamped ground plane. " +
             "Only turn on if your ground is perfectly flat.")]
    [SerializeField] private bool ignoreNavMesh = false;

    [Header("=== VARIATION ===")]
    [Tooltip("Random Y rotation. Keeps forests from looking stamped.")]
    [SerializeField] private bool randomYaw = true;
    [Tooltip("Uniform scale range applied to every resource. Keeps a forest from " +
             "looking like clones. 1 = keep prefab scale.")]
    [SerializeField] private float minScale = 0.82f;
    [SerializeField] private float maxScale = 1.22f;

    [Header("=== MAP / LOOSE GROUPING ===")]
    [Tooltip("Kept clear at the map edge (fraction of half-size).")]
    [SerializeField] private float edgeMarginFraction = 0.04f;
    [Tooltip("Fraction of remaining nodes that get pulled into small groves. 0 = " +
             "perfectly even lawn, 1 = all groves. 0.45 reads naturally.")]
    [SerializeField] private float groveFraction = 0.45f;
    [SerializeField] private int   groveSizeMin = 4;
    [SerializeField] private int   groveSizeMax = 9;
    [SerializeField] private float groveRadius = 11f;

    [Header("=== DIAGNOSTICS ===")]
    [SerializeField] private int randomSeed = 12345;
    [SerializeField] private bool logCoverageReport = true;

    // ── runtime state ──────────────────────────────────────────────────────
    private float mapHalfSize = 100f;
    private float limit;
    private float minNodeSeparationSqr;   // derived from the TREE footprint
    private float mineSeparationSqr;
    private float animalSeparationSqr;

    private readonly List<Vector3> placed = new List<Vector3>();
    private readonly SpatialHash hash = new SpatialHash(12f);
    private readonly List<Vector3> buildingPositions = new List<Vector3>();
    private readonly Dictionary<GameObject, int> counts = new Dictionary<GameObject, int>();
    private readonly Dictionary<GameObject, int> misses = new Dictionary<GameObject, int>();
    private readonly Dictionary<GameObject, float> footprints = new Dictionary<GameObject, float>();

    private void Start()
    {
        if (MapBoundary.Instance != null) mapHalfSize = MapBoundary.Instance.HalfSize;
        limit = mapHalfSize * (1f - edgeMarginFraction);

        Random.InitState(randomSeed);

        // Derive per-type separation from real prefab size, so spacing can never be
        // smaller than the object being placed. This is the v6 bug, fixed at source.
        minNodeSeparationSqr = SqSep(treePrefab,  extraGap);
        mineSeparationSqr    = SqSep(minePrefab,  extraGap + 1.0f); // mines get more elbow room
        animalSeparationSqr  = SqSep(animalPrefab, extraGap * 0.5f);

        StartCoroutine(SpawnRoutine());
    }

    /// <summary>Separation squared for a prefab = (footprint radius + gap)^2.</summary>
    private float SqSep(GameObject prefab, float gap)
    {
        if (prefab == null) return (gap * gap);
        float fp = FootprintRadius(prefab);
        float sep = fp + gap;
        float sq = sep * sep;
        // Never smaller than the gap itself, and never degenerate.
        return Mathf.Max(sq, gap * gap);
    }

    /// <summary>
    /// World-space radius of the prefab's widest horizontal extent, prefab scale
    /// included. Measured from a throwaway instance rather than MeshFilter.sharedMesh
    /// because that returns null for some prefab-embedded meshes, which silently
    /// degraded every separation back to the fallback and let trees interpenetrate.
    /// </summary>
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
            probe.SetActive(false); // Awake/OnEnable must not run

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

        // 0.5 is the "unknown size" fallback; never report 0 or we would allow overlap.
        footprints[prefab] = Mathf.Max(0.5f, r);
        return footprints[prefab];
    }

    private IEnumerator SpawnRoutine()
    {
        yield return null; // let BuildingSpawner create buildings

        CollectBuildings();

        // Anchors for the starter ring. Buildings are the real player bases, so they
        // take priority; SpawnAreaManager points are the fallback. Using spawn points
        // alone left 3 of the 4 bases with no guaranteed nearby resources, because the
        // manager only exposes one point in this scene.
        List<Vector3> starts = new List<Vector3>(buildingPositions);
        foreach (Vector3 sp in CollectSpawnPoints())
            if (!starts.Exists(p => (p - sp).sqrMagnitude < 1f)) starts.Add(sp);

        if (starts.Count == 0)
        {
            starts.Add(Vector3.zero);
            Debug.LogWarning("[ResourceSpawner] No buildings or spawn points — anchoring to map centre.");
        }

        float mapArea = (mapHalfSize * 2f) * (mapHalfSize * 2f);
        int wantTrees   = Mathf.Min(maxNodesPerType, Mathf.RoundToInt(mapArea / Mathf.Max(1f, areaPerTree)));
        int wantMines   = Mathf.Min(maxNodesPerType, Mathf.RoundToInt(mapArea / Mathf.Max(1f, areaPerMine)));
        int wantAnimals = Mathf.Min(maxNodesPerType, Mathf.RoundToInt(mapArea / Mathf.Max(1f, areaPerAnimal)));

        Debug.Log($"[ResourceSpawner] map {mapHalfSize * 2f:F0}x{mapHalfSize * 2f:F0} ({mapArea:F0} sq u) | " +
                  $"starts={starts.Count} buildings={buildingPositions.Count} | " +
                  $"targets {wantTrees}T/{wantMines}M/{wantAnimals}A | " +
                  $"tree footprint r={FootprintRadius(treePrefab):F2} sep={Mathf.Sqrt(minNodeSeparationSqr):F2}");

        // ── Stage 1: fair starter ring, identical for every start point ──────
        SpawnStarterRing(starts, treePrefab,   treesPerStart,   minNodeSeparationSqr);
        SpawnStarterRing(starts, minePrefab,   minesPerStart,   mineSeparationSqr);
        SpawnStarterRing(starts, animalPrefab, animalsPerStart, animalSeparationSqr);

        // ── Stage 2 + 3: stratified even fill, with a fraction pulled into groves ──
        FillRemaining(treePrefab,   wantTrees,   minNodeSeparationSqr);
        FillRemaining(minePrefab,   wantMines,   mineSeparationSqr);
        FillRemaining(animalPrefab, wantAnimals, animalSeparationSqr);

        if (logCoverageReport) Report(mapArea);
    }

    private void CollectBuildings()
    {
        buildingPositions.Clear();
        Building[] bs = FindObjectsOfType<Building>();
        for (int i = 0; i < bs.Length; i++)
            if (bs[i] != null) buildingPositions.Add(bs[i].transform.position);
    }

    /// <summary>Round to 2 decimals. Mathf.Round has no precision overload; System.Math.Round does.</summary>
    private static float Round2(float v) => (float)System.Math.Round(v, 2);

    private List<Vector3> CollectSpawnPoints()
    {
        var list = new List<Vector3>();
        SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
        if (area == null) return list;

        // Grow the probe count until we stop finding new points.
        HashSet<Vector3> seen = new HashSet<Vector3>();
        for (int n = 1; n <= 64; n++)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3 p = area.GetSpawnPosition(i);
                if (seen.Add(new Vector3(Round2(p.x), 0f, Round2(p.z))))
                    list.Add(p);
            }
            if (list.Count >= n) break;
        }
        return list;
    }

    // ── Stage 1 ────────────────────────────────────────────────────────────

    private void SpawnStarterRing(List<Vector3> starts, GameObject prefab, int perStart, float sepSqr)
    {
        if (prefab == null || perStart <= 0) return;

        for (int s = 0; s < starts.Count; s++)
        {
            for (int i = 0; i < perStart; i++)
            {
                // Stratified angle + even radius => a ring, not a clump.
                float t = (perStart <= 1) ? 0.5f : i / (float)(perStart - 1);
                float jitter = Random.Range(-0.35f, 0.35f) / perStart;
                float ang = (t + jitter) * Mathf.PI * 2f;
                float rad = Mathf.Lerp(startMinRadius, startMaxRadius,
                                       Mathf.Sqrt(Random.value)); // area-uniform

                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector3 p = starts[s] + new Vector3(dir.x, 0f, dir.y) * rad;

                if (TryPlace(prefab, p, sepSqr)) continue;
                // Retry the same slot with a fresh random angle before giving up.
                for (int k = 0; k < 4; k++)
                {
                    Vector2 d2 = Random.insideUnitCircle.normalized * rad;
                    Vector3 p2 = starts[s] + new Vector3(d2.x, 0f, d2.y);
                    if (TryPlace(prefab, p2, sepSqr)) break;
                }
            }
        }
    }

    // ── Stages 2 + 3 ───────────────────────────────────────────────────────

    private void FillRemaining(GameObject prefab, int target, float sepSqr)
    {
        if (prefab == null) return;
        int have = GetCount(prefab);
        int need = target - have;
        if (need <= 0) return;

        int groveCount = Mathf.RoundToInt(need * Mathf.Clamp01(groveFraction));
        int looseCount = need - groveCount;

        // Stage 2: stratified jittered grid over the whole map => even coverage.
        // Retries matter: a cell that fails once (starter ring already saturated it,
        // or a building sits there) used to be dropped forever, which left whole
        // strips of the map empty (measured 6..8 trees vs 71 in one quadrant).
        // The cell index must advance on every attempt, not only on success.
        int made2 = 0;
        int attempt2 = 0;
        int maxAttempts2 = looseCount * 3 + 16;
        int looseCells = Mathf.Max(1, looseCount);

        while (made2 < looseCount && attempt2 < maxAttempts2)
        {
            Vector3 p = StratifiedPoint(attempt2 % looseCells, looseCells);
            attempt2++;
            if (TryPlace(prefab, p, sepSqr)) made2++;
        }

        if (logCoverageReport)
            Debug.Log($"[ResourceSpawner] Loose stage: placed {made2}/{looseCount} in {attempt2} attempts.");

        // Stage 3: pull a fraction into bounded groves for natural clustering.
        // 'attempt' MUST advance on every pass, not just on success. Reusing the
        // success tally as the sampling index made every retry hit the SAME cell,
        // which piled groves into whatever cells the loose stage left free
        // (measured 11..60 trees per quadrant) and starved the rest.
        //
        // The grid must be sized by groveCount ALONE. Passing looseCount+groveCount
        // made the grid side sqrt(175)=14 while only ~46 attempts ran, so cz only ever
        // reached rows 2..5: every grove landed in z=[-69,-14] and the far strip of
        // the map got 6 trees instead of ~62.
        int groveSizeMin2 = Mathf.Max(1, groveSizeMin);
        int groveSizeMax2 = Mathf.Max(groveSizeMin2, groveSizeMax);
        int made = 0;
        int attempt = 0;
        int maxAttempts = groveCount * 4 + 16;

        // Size the grid by the number of GROVES WE WILL ACTUALLY CREATE, not by the
        // number of nodes. One grove emits ~size nodes, so groveCount trees needs only
        // groveCount/avgSize centres (~30). Sizing the grid by groveCount gave a 12x12
        // grid whose first 30 indices only reach rows 0..2, so every grove landed in
        // one third of the map and the opposite strip got 9 trees instead of ~50.
        //
        // groveCells MUST be the FULL side*side: StratifiedPoint derives its own side
        // from the total, so passing 22 against a 5x5 grid made cells 22..24
        // unreachable and the top row lost 3 of its 5 centres.
        float avgSize = (groveSizeMin2 + groveSizeMax2) * 0.5f;
        int expectedGroves = Mathf.Max(1, Mathf.RoundToInt(groveCount / Mathf.Max(1f, avgSize)));
        int gridSide = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(expectedGroves)));
        int groveCells = gridSide * gridSide;

        while (made < groveCount && attempt < maxAttempts)
        {
            // Index sweeps 0,1,2,... so grove centres visit every row of the grid.
            Vector3 centre = StratifiedPoint(attempt % groveCells, groveCells);
            attempt++;

            int size = Random.Range(groveSizeMin2, groveSizeMax2 + 1);
            for (int i = 0; i < size && made < groveCount; i++)
            {
                Vector2 r = Random.insideUnitCircle * groveRadius;
                Vector3 p = centre + new Vector3(r.x, 0f, r.y);
                if (TryPlace(prefab, p, sepSqr)) made++;
            }
        }

        if (logCoverageReport)
            Debug.Log($"[ResourceSpawner] Grove stage: placed {made}/{groveCount} in {attempt} attempts " +
                      $"(need ~{expectedGroves} centres, grid {gridSide}x{gridSide} = {gridSide * gridSide} cells).");
    }

    /// <summary>
    /// Stratified (jittered-grid) sampling: divides the square map into N cells and
    /// picks one uniformly-random point per cell. Gives even coverage with no voids,
    /// which pure rejection sampling and clustering both fail to do.
    /// </summary>
    private Vector3 StratifiedPoint(int index, int total)
    {
        int side = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, total))));
        int cx = index % side;
        int cz = index / side;
        float step = (limit * 2f) / side;
        float x = -limit + cx * step + Random.Range(0.08f, 0.92f) * step;
        float z = -limit + cz * step + Random.Range(0.08f, 0.92f) * step;
        return new Vector3(Mathf.Clamp(x, -limit, limit), 0f, Mathf.Clamp(z, -limit, limit));
    }

    // ── placement core ──────────────────────────────────────────────────────

    private bool TryPlace(GameObject prefab, Vector3 want, float sepSqr)
    {
        Vector3 p = ClampToMap(want);

        if (!ignoreNavMesh && NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas))
            p = hit.position;
        // If the NavMesh has no nearby polygon we deliberately keep the clamped
        // ground-plane position instead of dropping the node. v6 dropped it, which
        // is part of why coverage was so bad.

        if (!ClearOfBuildings(p)) return false;
        if (!ClearOfNodes(p, sepSqr)) return false;

        GameObject go = Instantiate(prefab, p, Rotation());
        go.transform.localScale = ScaleFor(prefab);
        hash.Add(p);
        placed.Add(p);
        Bump(counts, prefab, 1);
        return true;
    }

    private Quaternion Rotation()
        => randomYaw ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) : Quaternion.identity;

    private Vector3 ScaleFor(GameObject prefab)
    {
        if (Mathf.Approximately(minScale, 1f) && Mathf.Approximately(maxScale, 1f))
            return prefab.transform.localScale;
        float s = Random.Range(Mathf.Min(minScale, maxScale), Mathf.Max(minScale, maxScale));
        return prefab.transform.localScale * s;
    }

    private Vector3 ClampToMap(Vector3 p)
    {
        p.x = Mathf.Clamp(p.x, -limit, limit);
        p.z = Mathf.Clamp(p.z, -limit, limit);
        p.y = 0f;
        return p;
    }

    private bool ClearOfBuildings(Vector3 p)
    {
        if (buildingPositions.Count == 0 || buildingClearance <= 0f) return true;
        float r = FootprintRadius(treePrefab) + buildingClearance;
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

    // ── reporting ──────────────────────────────────────────────────────────

    private int GetCount(GameObject prefab)
        => prefab != null && counts.TryGetValue(prefab, out int n) ? n : 0;

    private static void Bump(Dictionary<GameObject, int> d, GameObject k, int by)
    { d.TryGetValue(k, out int n); d[k] = n + by; }

    private void Report(float mapArea)
    {
        int t = GetCount(treePrefab), m = GetCount(minePrefab), a = GetCount(animalPrefab);
        int total = t + m + a;

        Debug.Log($"[ResourceSpawner] PLACED {t} trees / {m} mines / {a} animals = {total} total " +
                  $"({total / Mathf.Max(1f, mapArea) * 10000f:F1} per 10k sq u)");

        if (t + m + a == 0)
        {
            Debug.LogError("[ResourceSpawner] Placed NOTHING — check prefab assignments and map size.");
            return;
        }

        // Coverage: what fraction of a coarse grid has at least one node?
        const int cells = 10;
        bool[] hit = new bool[cells * cells];
        for (int i = 0; i < placed.Count; i++)
        {
            int cx = Mathf.Clamp((int)((placed[i].x + limit) / (limit * 2f) * cells), 0, cells - 1);
            int cz = Mathf.Clamp((int)((placed[i].z + limit) / (limit * 2f) * cells), 0, cells - 1);
            hit[cz * cells + cx] = true;
        }
        int covered = 0;
        for (int i = 0; i < hit.Length; i++) if (hit[i]) covered++;
        float pct = covered / (float)hit.Length * 100f;
        Debug.Log($"[ResourceSpawner] Map coverage: {covered}/{cells * cells} cells occupied ({pct:F0}%) — " +
                  (pct > 70f ? "good spread" : "SPARSE, raise density or lower areaPerX"));

        // Closest-node distance tells you visually whether trees collide.
        // The probe must EXCLUDE ITSELF, or it always reports 0.00 because every
        // placed point is in the hash too.
        float nearest = float.MaxValue;
        for (int i = 0; i < placed.Count; i++)
        {
            hash.Nearest(placed[i], out float d);
            if (d < nearest) nearest = d;
        }
        if (placed.Count > 1 && nearest < 0.0001f)
            Debug.LogWarning("[ResourceSpawner] Overlapping nodes detected (nearest = 0). Separation is too small.");
        Debug.Log($"[ResourceSpawner] Closest two DISTINCT nodes: {nearest:F2} units " +
                  $"(enforced min separation = {Mathf.Sqrt(minNodeSeparationSqr):F2})");
    }

    private void OnDestroy()
    {
        placed.Clear();
        hash.Clear();
        counts.Clear();
        misses.Clear();
        footprints.Clear();
    }

    /// <summary>
    /// Uniform spatial hash for O(1) "is anything within r of p?" queries.
    /// v6 did a linear scan per candidate, which is O(n^2) over a whole spawn run.
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
            // Search a generous window; we only want a "closest pair" sanity number.
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
                    // Skip the point itself: every placed point is also in the hash.
                    if ((q.x == p.x) && (q.z == p.z)) continue;
                    float d = (q - p).sqrMagnitude;
                    if (d > 0.0000001f && d < best) best = d;
                }
            }
            dist = best;
        }

        public void Clear() => buckets.Clear();
    }
}