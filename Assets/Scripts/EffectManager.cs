using UnityEngine;

public class EffectManager : MonoBehaviour
{
    public static EffectManager Instance { get; private set; }

    [Header("Hit Effects")]
    [SerializeField] private GameObject hitParticlePrefab;
    [SerializeField] private GameObject buildingHitParticlePrefab;
    [SerializeField] private GameObject deathParticlePrefab;

    [Header("Construction")]
    [SerializeField] private GameObject buildDustParticlePrefab;
    [SerializeField] private GameObject buildCompleteParticlePrefab;

    [Header("Resource Effects")]
    [SerializeField] private GameObject resourceGatherParticlePrefab;
    [SerializeField] private GameObject treeFallParticlePrefab;

    [Header("Selection")]
    [SerializeField] private GameObject selectionFlashPrefab;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void PlayHitEffect(Vector3 position, Vector3 normal = default)
    {
        if (hitParticlePrefab != null)
            SpawnParticle(hitParticlePrefab, position, normal);
    }

    public void PlayBuildingHitEffect(Vector3 position)
    {
        if (buildingHitParticlePrefab != null)
            SpawnParticle(buildingHitParticlePrefab, position);
    }

    public void PlayDeathEffect(Vector3 position)
    {
        if (deathParticlePrefab != null)
            SpawnParticle(deathParticlePrefab, position);
        ScreenShake.Instance?.LightShake();
    }

    public void PlayBuildDustEffect(Vector3 position)
    {
        if (buildDustParticlePrefab != null)
            SpawnParticle(buildDustParticlePrefab, position);
    }

    public void PlayBuildCompleteEffect(Vector3 position)
    {
        if (buildCompleteParticlePrefab != null)
            SpawnParticle(buildCompleteParticlePrefab, position);
        ScreenShake.Instance?.MediumShake();
    }

    public void PlayResourceGatherEffect(Vector3 position)
    {
        if (resourceGatherParticlePrefab != null)
            SpawnParticle(resourceGatherParticlePrefab, position);
    }

    public void PlayTreeFallEffect(Vector3 position)
    {
        if (treeFallParticlePrefab != null)
            SpawnParticle(treeFallParticlePrefab, position);
    }

    public void PlaySelectionFlash(Vector3 position)
    {
        if (selectionFlashPrefab != null)
            SpawnParticle(selectionFlashPrefab, position, lifetime: 0.5f);
    }

    private void SpawnParticle(GameObject prefab, Vector3 position, Vector3 normal = default, float lifetime = 2f)
    {
        GameObject go = Instantiate(prefab, position, Quaternion.identity);
        if (normal != default)
            go.transform.up = normal;

        ParticleSystem ps = go.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            Destroy(go, Mathf.Max(ps.main.duration + ps.main.startLifetime.constantMax, lifetime));
        }
        else
        {
            Destroy(go, lifetime);
        }
    }
}