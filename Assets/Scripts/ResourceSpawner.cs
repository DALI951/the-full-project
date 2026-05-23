using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// ResourceSpawner v6 — FIXED for resources touching buildings.
/// 
/// KEY FIXES:
/// 1. Waits 1 frame for BuildingSpawner to finish, then finds buildings.
/// 2. Spawns resources at building positions + small radius (5-15 units).
/// 3. Resources spawn INWARD from buildings toward center.
/// 4. Extra clusters near buildings so player sees resources immediately.
/// 5. Phase 2 scatter fills rest of map.
/// </summary>
public class ResourceSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject treePrefab;
    [SerializeField] private GameObject minePrefab;
    [SerializeField] private GameObject animalPrefab;

    private float mapHalfSize = 500f;

    [Header("Building-Adjacent Spawning")]
    [Tooltip("Resources spawn this close to buildings. 3 = touching/near buildings.")]
    [SerializeField] private float buildingSpawnRadius = 8f;
    [Tooltip("Min distance from building. 2 = very close.")]
    [SerializeField] private float buildingSpawnMin = 2f;
    [Tooltip("Resources per building cluster.")]
    [SerializeField] private int treesNearBuilding = 3;
    [SerializeField] private int minesNearBuilding = 1;
    [SerializeField] private int animalsNearBuilding = 2;

    [Header("Global Density")]
    [SerializeField] private int treesPer100Units = 4;
    [SerializeField] private int minesPer100Units = 2;
    [SerializeField] private int animalsPer100Units = 3;

    [Header("Spacing")]
    [SerializeField] private float minNodeSeparation = 2f;
    [SerializeField] private float edgeMarginFraction = 0.05f;

    [Header("Placement")]
    [SerializeField] private int maxAttempts = 100;
    [SerializeField] private float navMeshMaxDistance = 20f;

    [Header("Forest Clustering")]
    [SerializeField] private int   treesPerForest     = 8;
    [SerializeField] private float forestClusterRadius = 7f;

    [Header("Animal Herds")]
    [SerializeField] private int   animalsPerHerd  = 4;
    [SerializeField] private float herdSpawnRadius = 5f;

    // Runtime
    private List<Vector3> placedPositions = new List<Vector3>();
    private float minNodeSeparationSqr;
    private float edgeMargin;
    private float limit;
    private int spawnedTrees, spawnedMines, spawnedAnimals;
    private int failedTrees, failedMines, failedAnimals;

    private void Start()
    {
        if (MapBoundary.Instance != null)
            mapHalfSize = MapBoundary.Instance.HalfSize;

        edgeMargin = mapHalfSize * edgeMarginFraction;
        limit = mapHalfSize - edgeMargin;
        minNodeSeparationSqr = minNodeSeparation * minNodeSeparation;

        // Wait one frame for BuildingSpawner to create buildings
        StartCoroutine(SpawnAfterDelay());
    }

    private IEnumerator SpawnAfterDelay()
    {
        yield return null; // Wait 1 frame for buildings to exist

        // Find all player buildings
        List<Vector3> buildingPositions = new List<Vector3>();
        foreach (var b in FindObjectsOfType<Building>())
        {
            if (b != null) buildingPositions.Add(b.transform.position);
        }

        // Also get spawn positions as fallback
        SpawnAreaManager spawnArea = FindObjectOfType<SpawnAreaManager>();
        List<Vector3> spawnPositions = new List<Vector3>();
        if (spawnArea != null)
        {
            for (int i = 0; i < 10; i++)
            {
                Vector3 sp = spawnArea.GetSpawnPosition(i);
                sp.x = Mathf.Clamp(sp.x, -limit, limit);
                sp.z = Mathf.Clamp(sp.z, -limit, limit);
                spawnPositions.Add(sp);
            }
        }

        float mapArea = (mapHalfSize * 2f) * (mapHalfSize * 2f);
        float scaleFactor = mapArea / 10000f;

        int totalTrees = Mathf.RoundToInt(treesPer100Units * scaleFactor);
        int totalMines = Mathf.RoundToInt(minesPer100Units * scaleFactor);
        int totalAnimals = Mathf.RoundToInt(animalsPer100Units * scaleFactor);

        Debug.Log($"[ResourceSpawner] Map: {mapHalfSize*2}×{mapHalfSize*2} | " +
                  $"Buildings found: {buildingPositions.Count} | " +
                  $"Building radius: {buildingSpawnRadius:F1} | " +
                  $"Targets: {totalTrees}T/{totalMines}M/{totalAnimals}A");

        // ── Phase 1: Spawn resources RIGHT NEXT to each building ──────────
        foreach (Vector3 buildingPos in buildingPositions)
        {
            // Direction from building toward map center
            Vector3 toCenter = (Vector3.zero - buildingPos).normalized;
            if (toCenter.sqrMagnitude < 0.001f) toCenter = Vector3.forward;

            SpawnNearPoint(treePrefab,   treesNearBuilding,   buildingPos, toCenter, buildingSpawnMin, buildingSpawnRadius);
            SpawnNearPoint(minePrefab,   minesNearBuilding,   buildingPos, toCenter, buildingSpawnMin, buildingSpawnRadius);
            SpawnNearPoint(animalPrefab, animalsNearBuilding, buildingPos, toCenter, buildingSpawnMin, buildingSpawnRadius);
        }

        // ── Phase 2: Scatter remaining across entire map ─────────────────
        int remainingTrees = Mathf.Max(0, totalTrees - spawnedTrees);
        int remainingMines = Mathf.Max(0, totalMines - spawnedMines);
        int remainingAnimals = Mathf.Max(0, totalAnimals - spawnedAnimals);

        SpawnForests(remainingTrees);
        SpawnRandom(minePrefab, remainingMines);   // mines unchanged
        SpawnHerds(remainingAnimals);

        // ── Report ───────────────────────────────────────────────────────
        Debug.Log($"[ResourceSpawner] RESULTS — Spawned: {spawnedTrees}T/{spawnedMines}M/{spawnedAnimals}A | " +
                  $"Failed: {failedTrees}T/{failedMines}M/{failedAnimals}A | Total: {placedPositions.Count}");

        // Verify distance from first building
        if (buildingPositions.Count > 0 && placedPositions.Count > 0)
        {
            float nearestDist = float.MaxValue;
            foreach (var pos in placedPositions)
            {
                float d = Vector3.Distance(buildingPositions[0], pos);
                if (d < nearestDist) nearestDist = d;
            }
            Debug.Log($"[ResourceSpawner] Nearest resource to first building: {nearestDist:F1} units");
        }
    }

    private void SpawnNearPoint(GameObject prefab, int count, Vector3 center, Vector3 inwardDir, float minDist, float maxDist)
    {
        if (prefab == null || count <= 0) return;

        for (int i = 0; i < count; i++)
        {
            // Random in circle, biased inward toward center
            Vector2 randCircle = Random.insideUnitCircle;
            Vector3 offset = new Vector3(randCircle.x, 0, randCircle.y) * maxDist;

            // Strong inward bias so resources are between building and center
            float inwardBias = Random.Range(minDist, maxDist * 0.8f);
            offset += inwardDir * inwardBias;

            Vector3 candidate = center + offset;
            candidate.x = Mathf.Clamp(candidate.x, -limit, limit);
            candidate.z = Mathf.Clamp(candidate.z, -limit, limit);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshMaxDistance, NavMesh.AllAreas))
            {
                // Fallback: closer to center point
                candidate = center + inwardDir * Random.Range(minDist, maxDist * 0.5f);
                candidate.x = Mathf.Clamp(candidate.x, -limit, limit);
                candidate.z = Mathf.Clamp(candidate.z, -limit, limit);
                if (!NavMesh.SamplePosition(candidate, out hit, navMeshMaxDistance * 2f, NavMesh.AllAreas))
                {
                    IncrementCounter(prefab, false);
                    continue;
                }
            }

            Vector3 snapped = hit.position;
            if (!ClearOfOtherNodes(snapped, minNodeSeparationSqr))
            {
                if (!ClearOfOtherNodes(snapped, minNodeSeparationSqr * 0.25f))
                {
                    IncrementCounter(prefab, false);
                    continue;
                }
            }

            Instantiate(prefab, snapped, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            placedPositions.Add(snapped);
            IncrementCounter(prefab, true);
        }
    }

    private void SpawnRandom(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0) return;

        for (int i = 0; i < count; i++)
        {
            Vector3 center = new Vector3(
                Random.Range(-limit, limit), 0,
                Random.Range(-limit, limit));

            Vector3? pos = FindValidPos(center, 0, limit * 0.9f);
            if (pos.HasValue)
            {
                Instantiate(prefab, pos.Value, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
                placedPositions.Add(pos.Value);
                IncrementCounter(prefab, true);
            }
            else
            {
                IncrementCounter(prefab, false);
            }
        }
    }

    private Vector3? FindValidPos(Vector3 center, float minDist, float maxDist)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float effectiveSepSqr = minNodeSeparationSqr;
            if (attempt > 50) effectiveSepSqr = (minNodeSeparation * 0.5f) * (minNodeSeparation * 0.5f);
            if (attempt > 80) effectiveSepSqr = 0f;

            Vector2 rand = Random.insideUnitCircle;
            if (rand.sqrMagnitude < 0.001f) rand = Vector2.right;
            rand = rand.normalized * Random.Range(Mathf.Max(0.5f, minDist), maxDist);
            Vector3 candidate = center + new Vector3(rand.x, 0, rand.y);

            candidate.x = Mathf.Clamp(candidate.x, -limit, limit);
            candidate.z = Mathf.Clamp(candidate.z, -limit, limit);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navMeshMaxDistance, NavMesh.AllAreas))
                continue;

            Vector3 snapped = hit.position;
            if (!ClearOfOtherNodes(snapped, effectiveSepSqr))
                continue;

            return snapped;
        }
        return null;
    }

    private bool ClearOfOtherNodes(Vector3 pos, float sqrSeparation)
    {
        if (sqrSeparation <= 0f) return true;
        foreach (Vector3 p in placedPositions)
        {
            if ((pos - p).sqrMagnitude < sqrSeparation)
                return false;
        }
        return true;
    }

    private void IncrementCounter(GameObject prefab, bool success)
    {
        if (prefab == treePrefab) { if (success) spawnedTrees++; else failedTrees++; }
        else if (prefab == minePrefab) { if (success) spawnedMines++; else failedMines++; }
        else if (prefab == animalPrefab) { if (success) spawnedAnimals++; else failedAnimals++; }
    }
    // ── Forest clusters ───────────────────────────────────────────────────
    private void SpawnForests(int totalTrees)
    {
        if (treePrefab == null || totalTrees <= 0) return;
        int forests = Mathf.Max(1, totalTrees / Mathf.Max(1, treesPerForest));
        for (int f = 0; f < forests; f++)
        {
            Vector3 center = new Vector3(
                Random.Range(-limit, limit), 0f,
                Random.Range(-limit, limit));
            SpawnTreeCluster(center, treesPerForest);
        }
    }

    private void SpawnTreeCluster(Vector3 center, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 rand = Random.insideUnitCircle * forestClusterRadius;
            Vector3 candidate = center + new Vector3(rand.x, 0f, rand.y);
            candidate.x = Mathf.Clamp(candidate.x, -limit, limit);
            candidate.z = Mathf.Clamp(candidate.z, -limit, limit);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit,
                    navMeshMaxDistance, NavMesh.AllAreas))
                continue;

            Vector3 snapped = hit.position;
            // Relax separation inside cluster for dense, natural forest look
            if (!ClearOfOtherNodes(snapped, minNodeSeparationSqr * 0.25f))
                continue;

            Instantiate(treePrefab, snapped,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            placedPositions.Add(snapped);
            IncrementCounter(treePrefab, true);
        }
    }

    // ── Animal herds ──────────────────────────────────────────────────────

    private void SpawnHerds(int totalAnimals)
    {
        if (animalPrefab == null || totalAnimals <= 0) return;
        int herds = Mathf.Max(1, totalAnimals / Mathf.Max(1, animalsPerHerd));
        for (int h = 0; h < herds; h++)
        {
            Vector3 center = new Vector3(
                Random.Range(-limit, limit), 0f,
                Random.Range(-limit, limit));
            SpawnAnimalHerdAt(center, animalsPerHerd);
        }
    }

    private void SpawnAnimalHerdAt(Vector3 center, int count)
    {
        if (animalPrefab == null) return;
        int herdId = AnimalNode.AllocateHerdId();

        for (int i = 0; i < count; i++)
        {
            Vector2 rand = Random.insideUnitCircle * herdSpawnRadius;
            Vector3 candidate = center + new Vector3(rand.x, 0f, rand.y);
            candidate.x = Mathf.Clamp(candidate.x, -limit, limit);
            candidate.z = Mathf.Clamp(candidate.z, -limit, limit);

            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit,
                    navMeshMaxDistance, NavMesh.AllAreas))
            {
                Vector3 fallback = center;
                fallback.x = Mathf.Clamp(fallback.x, -limit, limit);
                fallback.z = Mathf.Clamp(fallback.z, -limit, limit);
                if (!NavMesh.SamplePosition(fallback, out hit,
                        navMeshMaxDistance * 2f, NavMesh.AllAreas))
                { IncrementCounter(animalPrefab, false); continue; }
            }

            GameObject go = Instantiate(animalPrefab, hit.position,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            AnimalNode an = go.GetComponent<AnimalNode>();
            if (an != null) an.herdId = herdId;

            placedPositions.Add(hit.position);
            IncrementCounter(animalPrefab, true);
        }
    }
}