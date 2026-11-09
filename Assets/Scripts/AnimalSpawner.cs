using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// AnimalSpawner — secondary animal spawner safeguard.
/// Ensures wildlife does not overpopulate or double-spawn on top of ResourceSpawner.
/// Caps any additional animal spawning to <= 12 and defers if ResourceSpawner has already populated the map.
/// </summary>
public class AnimalSpawner : MonoBehaviour
{
    [Header("Safeguard Settings")]
    [Tooltip("Maximum allowed animals to spawn if ResourceSpawner is not present. Capped at 12.")]
    [SerializeField] private int maxAnimals = 12;
    [SerializeField] private int initialCount = 8;
    [SerializeField] private float spawnRadius = 25f;
    [SerializeField] private GameObject animalPrefab;

    private void Start()
    {
        // Check if animals already exist on the map (e.g. from ResourceSpawner)
        AnimalNode[] existing = FindObjectsOfType<AnimalNode>();
        if (existing != null && existing.Length > 0)
        {
            Debug.Log($"[AnimalSpawner] Detected {existing.Length} existing animals on the map. Deferring to ResourceSpawner AoE4 wildlife distribution.");
            return;
        }

        ResourceSpawner rs = FindObjectOfType<ResourceSpawner>();
        if (rs != null)
        {
            // ResourceSpawner handles balanced AoE4 herds
            Debug.Log("[AnimalSpawner] ResourceSpawner is active in scene. Deferring animal spawning to ResourceSpawner.");
            return;
        }

        // Hard cap to <= 12
        int countToSpawn = Mathf.Clamp(initialCount, 1, Mathf.Min(maxAnimals, 12));
        SpawnCappedHerd(countToSpawn);
    }

    private void SpawnCappedHerd(int count)
    {
        if (animalPrefab == null) return;
        int herdId = AnimalNode.AllocateHerdId();

        for (int i = 0; i < count; i++)
        {
            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            Vector3 pos = transform.position + new Vector3(offset.x, 0f, offset.y);
            if (MapBoundary.Instance != null) pos = MapBoundary.Instance.Clamp(pos);

            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 8f, UnityEngine.AI.NavMesh.AllAreas))
                pos = hit.position;
            else pos.y = 0f;

            GameObject animal = Instantiate(animalPrefab, pos, Quaternion.identity);
            if (animal.TryGetComponent(out AnimalNode node))
            {
                node.herdId = herdId;
            }
        }

        Debug.Log($"[AnimalSpawner] Spawned {count} animals in standalone fallback mode.");
    }
}
