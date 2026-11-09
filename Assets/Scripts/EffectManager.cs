using System.Collections.Generic;
using UnityEngine;

public class EffectManager : MonoBehaviour
{
    private static EffectManager _instance;
    public static EffectManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<EffectManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("EffectManager");
                    _instance = go.AddComponent<EffectManager>();
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

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

    [Header("Age Up & Tech")]
    [SerializeField] private GameObject ageUpFanfarePrefab;

    private static Material s_ParticleMaterial;
    private float buildingVFXCheckTimer = 0f;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    private void Update()
    {
        // Periodically verify all active buildings have BuildingDamageVFX attached
        buildingVFXCheckTimer += Time.deltaTime;
        if (buildingVFXCheckTimer >= 0.5f)
        {
            buildingVFXCheckTimer = 0f;
            EnsureBuildingDamageVFX();
        }
    }

    private void EnsureBuildingDamageVFX()
    {
        var buildings = Building.AllBuildings;
        if (buildings == null) return;

        for (int i = 0; i < buildings.Count; i++)
        {
            Building b = buildings[i];
            if (b != null && b.gameObject != null)
            {
                if (b.GetComponent<BuildingDamageVFX>() == null)
                {
                    b.gameObject.AddComponent<BuildingDamageVFX>();
                }
            }
        }
    }

    public void PlayHitEffect(Vector3 position, Vector3 normal = default)
    {
        if (hitParticlePrefab != null)
            SpawnParticle(hitParticlePrefab, position, normal);
        else
            CreateProceduralBurst(position, new Color(1f, 0.65f, 0.15f), 12, 0.18f, 3.5f, 0.25f);
    }

    public void PlayHitSpark(Vector3 position)
    {
        CreateProceduralBurst(position, new Color(1f, 0.95f, 0.4f), 8, 0.12f, 4.5f, 0.15f);
    }

    public void PlayBuildingHitEffect(Vector3 position)
    {
        if (buildingHitParticlePrefab != null)
            SpawnParticle(buildingHitParticlePrefab, position);
        else
            CreateProceduralBurst(position, new Color(0.65f, 0.6f, 0.5f), 16, 0.25f, 2.5f, 0.4f);
    }

    public void PlayDeathEffect(Vector3 position)
    {
        if (deathParticlePrefab != null)
            SpawnParticle(deathParticlePrefab, position);
        else
            CreateProceduralBurst(position, new Color(0.85f, 0.25f, 0.2f, 0.8f), 18, 0.35f, 2.2f, 0.45f);

        ScreenShake.Instance?.LightShake();
    }

    public void PlayBuildDustEffect(Vector3 position)
    {
        if (buildDustParticlePrefab != null)
            SpawnParticle(buildDustParticlePrefab, position);
        else
            CreateProceduralBurst(position, new Color(0.75f, 0.7f, 0.55f, 0.6f), 10, 0.3f, 1.5f, 0.4f);
    }

    public void PlayBuildCompleteEffect(Vector3 position)
    {
        if (buildCompleteParticlePrefab != null)
            SpawnParticle(buildCompleteParticlePrefab, position);
        else
            CreateProceduralBurst(position, new Color(1f, 0.85f, 0.2f), 22, 0.25f, 3f, 0.55f);

        ScreenShake.Instance?.MediumShake();
    }

    public void PlayResourceGatherEffect(Vector3 position)
    {
        if (resourceGatherParticlePrefab != null)
            SpawnParticle(resourceGatherParticlePrefab, position);
        else
            CreateProceduralBurst(position, new Color(0.35f, 0.9f, 0.45f, 0.8f), 6, 0.15f, 1.2f, 0.35f);
    }

    public void PlayTreeFallEffect(Vector3 position)
    {
        if (treeFallParticlePrefab != null)
            SpawnParticle(treeFallParticlePrefab, position);
        else
            CreateProceduralBurst(position, new Color(0.45f, 0.35f, 0.2f), 14, 0.22f, 2.2f, 0.5f);
    }

    public void PlaySelectionFlash(Vector3 position)
    {
        if (selectionFlashPrefab != null)
            SpawnParticle(selectionFlashPrefab, position, lifetime: 0.5f);
        else
            CreateProceduralBurst(position, new Color(0.3f, 0.7f, 1f, 0.7f), 8, 0.15f, 1.5f, 0.3f);
    }

    /// <summary>
    /// Age of Empires IV Age-Up / Tech Fanfare:
    /// Launches a majestic rising pillar of golden particles, an expanding golden ground ring,
    /// triggers the brass horn audio cue, and performs a medium screen shake.
    /// </summary>
    public void PlayAgeUpFanfare(Vector3 position)
    {
        if (ageUpFanfarePrefab != null)
        {
            SpawnParticle(ageUpFanfarePrefab, position, lifetime: 3f);
        }
        else
        {
            CreateProceduralAgeUpFanfare(position);
        }

        AudioManager.Instance?.PlayAgeUpFanfare();
        ScreenShake.Instance?.MediumShake();
    }

    private void CreateProceduralAgeUpFanfare(Vector3 position)
    {
        GameObject root = new GameObject("AgeUpFanfare");
        root.transform.position = position;

        Material mat = GetParticleMaterial();

        // 1. Rising Golden Pillar of Light & Sparkles
        GameObject pillarGo = new GameObject("PillarParticles");
        pillarGo.transform.SetParent(root.transform, false);

        ParticleSystem psPillar = pillarGo.AddComponent<ParticleSystem>();
        psPillar.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var rendPillar = pillarGo.GetComponent<ParticleSystemRenderer>();
        if (rendPillar != null && mat != null) rendPillar.material = mat;

        var mainP = psPillar.main;
        mainP.loop = false;
        mainP.duration = 1.0f;
        mainP.startLifetime = 1.8f;
        mainP.startSpeed = new ParticleSystem.MinMaxCurve(6f, 10f);
        mainP.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        mainP.startColor = new Color(1f, 0.92f, 0.4f, 0.95f);
        mainP.simulationSpace = ParticleSystemSimulationSpace.World;
        mainP.gravityModifier = -0.1f; // Gentle upward lift

        var emP = psPillar.emission;
        emP.rateOverTime = 0f;
        emP.SetBursts(new ParticleSystem.Burst[] {
            new ParticleSystem.Burst(0.0f, 45),
            new ParticleSystem.Burst(0.2f, 30),
            new ParticleSystem.Burst(0.45f, 25)
        });

        var shapeP = psPillar.shape;
        shapeP.shapeType = ParticleSystemShapeType.Circle;
        shapeP.radius = 1.5f;
        shapeP.rotation = new Vector3(-90f, 0f, 0f); // Face straight up

        var colP = psPillar.colorOverLifetime;
        colP.enabled = true;
        Gradient gradP = new Gradient();
        gradP.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0.0f),
                new GradientColorKey(new Color(1f, 0.75f, 0.15f), 0.6f),
                new GradientColorKey(new Color(0.9f, 0.45f, 0.05f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.85f, 0.6f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        colP.color = gradP;

        psPillar.Play();

        // 2. Expanding Golden Ground Ring / Shockwave
        GameObject ringGo = new GameObject("RingParticles");
        ringGo.transform.SetParent(root.transform, false);
        ringGo.transform.localPosition = Vector3.up * 0.1f;

        ParticleSystem psRing = ringGo.AddComponent<ParticleSystem>();
        psRing.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var rendRing = ringGo.GetComponent<ParticleSystemRenderer>();
        if (rendRing != null && mat != null) rendRing.material = mat;

        var mainR = psRing.main;
        mainR.loop = false;
        mainR.duration = 0.5f;
        mainR.startLifetime = 1.0f;
        mainR.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 6.5f);
        mainR.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        mainR.startColor = new Color(1f, 0.85f, 0.25f, 0.9f);
        mainR.simulationSpace = ParticleSystemSimulationSpace.World;

        var emR = psRing.emission;
        emR.rateOverTime = 0f;
        emR.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0.0f, 36) });

        var shapeR = psRing.shape;
        shapeR.shapeType = ParticleSystemShapeType.Circle;
        shapeR.radius = 0.5f;
        shapeR.rotation = new Vector3(90f, 0f, 0f); // Lie flat on horizontal ground

        var colR = psRing.colorOverLifetime;
        colR.enabled = true;
        Gradient gradR = new Gradient();
        gradR.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.95f, 0.5f), 0.0f),
                new GradientColorKey(new Color(1f, 0.6f, 0.1f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.9f, 0.0f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        colR.color = gradR;

        psRing.Play();

        Destroy(root, 2.6f);
    }

    public void PlayDamageText(Vector3 position, int amount, Color? color = null)
    {
        FloatingDamageText.Spawn(position, amount, color);
    }

    public void PlayFloatingText(Vector3 position, string text, Color? color = null)
    {
        FloatingDamageText.Spawn(position, text, color);
    }

    private void SpawnParticle(GameObject prefab, Vector3 position, Vector3 normal = default, float lifetime = 2f)
    {
        if (prefab == null) return;
        GameObject go = Instantiate(prefab, position, Quaternion.identity);
        if (normal != default)
            go.transform.up = normal;

        if (go.transform.localScale.x > 1.5f || go.transform.localScale.y > 1.5f || go.transform.localScale.z > 1.5f)
        {
            go.transform.localScale = Vector3.one;
        }

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

    public static Material GetParticleMaterial()
    {
        if (s_ParticleMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Mobile/Particles/Additive");
            if (shader != null)
                s_ParticleMaterial = new Material(shader);
        }
        return s_ParticleMaterial;
    }

    public static Material GetOrCreateParticleMaterial() => GetParticleMaterial();

    private void CreateProceduralBurst(Vector3 position, Color color, int count, float size, float speed, float lifetime)
    {
        GameObject go = new GameObject("ProceduralEffect");
        go.transform.position = position;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
        Material mat = GetParticleMaterial();
        if (psr != null && mat != null)
        {
            psr.material = mat;
        }

        var main = ps.main;
        main.loop = false;
        main.duration = 0.1f;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = size;
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;

        ps.Play();
        Destroy(go, lifetime + 0.2f);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}

/// <summary>
/// Automatic AoE4 Building Damage VFX Helper:
/// Monitors Building health in real-time and automatically billows dark smoke
/// and orange fiery embers whenever building health drops below 50%.
/// Intensifies at critical health (&lt; 25%), and smoothly stops if repaired.
/// </summary>
public class BuildingDamageVFX : MonoBehaviour
{
    private Building building;
    private ParticleSystem smokePS;
    private ParticleSystem embersPS;
    private bool isEmitting = false;

    private void Awake()
    {
        building = GetComponent<Building>();
    }

    private void Start()
    {
        if (building == null) building = GetComponentInParent<Building>();
        SetupVFX();
    }

    private void SetupVFX()
    {
        if (smokePS != null) return;

        float vfxHeight = 2.0f;
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            vfxHeight = Mathf.Max(1.2f, col.bounds.size.y * 0.75f);
        }

        Vector3 spawnOffset = new Vector3(0f, vfxHeight, 0f);
        Material mat = EffectManager.GetOrCreateParticleMaterial();

        // 1. Dark Billowing Smoke Particle System
        GameObject smokeGo = new GameObject("DamageSmokeVFX");
        smokeGo.transform.SetParent(transform, false);
        smokeGo.transform.localPosition = spawnOffset;

        smokePS = smokeGo.AddComponent<ParticleSystem>();
        smokePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var smokeRend = smokeGo.GetComponent<ParticleSystemRenderer>();
        if (smokeRend != null && mat != null) smokeRend.material = mat;

        var sMain = smokePS.main;
        sMain.loop = true;
        sMain.duration = 1.0f;
        sMain.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
        sMain.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.0f);
        sMain.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
        sMain.startColor = new Color(0.22f, 0.22f, 0.24f, 0.65f);
        sMain.simulationSpace = ParticleSystemSimulationSpace.World;
        sMain.playOnAwake = false;

        var sEmission = smokePS.emission;
        sEmission.rateOverTime = 12f;

        var sShape = smokePS.shape;
        sShape.shapeType = ParticleSystemShapeType.Cone;
        sShape.angle = 14f;
        sShape.radius = 0.5f;

        var sSize = smokePS.sizeOverLifetime;
        sSize.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.4f);
        sizeCurve.AddKey(1f, 2.2f);
        sSize.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var sColor = smokePS.colorOverLifetime;
        sColor.enabled = true;
        Gradient sGrad = new Gradient();
        sGrad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.28f, 0.28f, 0.28f), 0.0f),
                new GradientColorKey(new Color(0.12f, 0.12f, 0.12f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.7f, 0.0f),
                new GradientAlphaKey(0.5f, 0.5f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        sColor.color = sGrad;

        // 2. Fiery Orange Embers Particle System
        GameObject embersGo = new GameObject("DamageEmbersVFX");
        embersGo.transform.SetParent(transform, false);
        embersGo.transform.localPosition = spawnOffset;

        embersPS = embersGo.AddComponent<ParticleSystem>();
        embersPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var embersRend = embersGo.GetComponent<ParticleSystemRenderer>();
        if (embersRend != null && mat != null) embersRend.material = mat;

        var eMain = embersPS.main;
        eMain.loop = true;
        eMain.duration = 1.0f;
        eMain.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
        eMain.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
        eMain.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
        eMain.startColor = new Color(1f, 0.5f, 0.08f, 1f);
        eMain.simulationSpace = ParticleSystemSimulationSpace.World;
        eMain.playOnAwake = false;

        var eEmission = embersPS.emission;
        eEmission.rateOverTime = 10f;

        var eShape = embersPS.shape;
        eShape.shapeType = ParticleSystemShapeType.Sphere;
        eShape.radius = 0.45f;

        var eColor = embersPS.colorOverLifetime;
        eColor.enabled = true;
        Gradient eGrad = new Gradient();
        eGrad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.88f, 0.25f), 0.0f),
                new GradientColorKey(new Color(1f, 0.42f, 0.05f), 0.6f),
                new GradientColorKey(new Color(0.75f, 0.12f, 0.0f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(0.85f, 0.7f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        eColor.color = eGrad;

        smokePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        embersPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Update()
    {
        if (building == null) return;

        float hpRatio = (float)building.CurrentBuildingHealth / Mathf.Max(1, building.MaxBuildingHealth);

        // Emit when building health drops below 50%
        bool shouldEmit = hpRatio < 0.5f && building.CurrentBuildingHealth > 0;

        if (shouldEmit != isEmitting)
        {
            isEmitting = shouldEmit;
            if (isEmitting)
            {
                if (smokePS != null) smokePS.Play();
                if (embersPS != null) embersPS.Play();
            }
            else
            {
                if (smokePS != null) smokePS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (embersPS != null) embersPS.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        // Critical damage (< 25% health): double particle intensity
        if (isEmitting)
        {
            float intensity = hpRatio < 0.25f ? 2.0f : 1.0f;
            if (smokePS != null)
            {
                var em = smokePS.emission;
                em.rateOverTime = 14f * intensity;
            }
            if (embersPS != null)
            {
                var em = embersPS.emission;
                em.rateOverTime = 12f * intensity;
            }
        }
    }
}
