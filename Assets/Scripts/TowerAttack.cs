using System.Collections;
using UnityEngine;
using Mirror;

public class TowerAttack : NetworkBehaviour
{
    [Header("Combat Settings")]
    public float attackRange = 12f;
    public int attackDamage = 15;
    public float attackCooldown = 1.5f;
    public LayerMask unitLayer = ~0;

    [Header("Visuals & Audio")]
    [SerializeField] private GameObject customArrowPrefab;
    [SerializeField] private float launchHeightOffset = 3.4f;

    private Building building;
    private float lastAttackTime = -99f;

    private void Start()
    {
        building = GetComponent<Building>();
    }

    private void Update()
    {
        if (NetworkClient.active && !NetworkServer.active) return;

        if (Time.time - lastAttackTime < attackCooldown) return;

        int buildingOwnerId = building != null ? building.OwnerPlayerId : 0;
        Collider[] hits = Physics.OverlapSphere(transform.position, attackRange, unitLayer);
        Unit closest = null;
        float closestDist = float.MaxValue;

        foreach (Collider hit in hits)
        {
            Unit u = hit.GetComponentInParent<Unit>();
            if (u == null || u.IsDead) continue;
            if (AreSameTeam(u.OwnerPlayerId, buildingOwnerId)) continue;

            float d = Vector3.Distance(transform.position, u.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                closest = u;
            }
        }

        if (closest != null)
        {
            lastAttackTime = Time.time;
            Vector3 launchPos = transform.position + Vector3.up * launchHeightOffset;
            Vector3 targetPos = closest.transform.position + Vector3.up * 0.8f;

            if (netIdentity != null && NetworkServer.active)
            {
                uint targetNetId = closest.netIdentity != null ? closest.netIdentity.netId : 0;
                RpcLaunchArrow(launchPos, targetNetId, targetPos);

                // If running as headless dedicated server (no client), deal damage on delayed timer
                if (!isClient)
                {
                    float dist = Vector3.Distance(launchPos, targetPos);
                    float flightDuration = Mathf.Clamp(dist / 22f, 0.2f, 1.2f);
                    StartCoroutine(ServerDelayedDamage(closest, flightDuration));
                }
            }
            else
            {
                // Single-player / offline mode
                SpawnArrow(launchPos, closest, targetPos, isServerAuth: true);
            }
        }
    }

    [ClientRpc]
    private void RpcLaunchArrow(Vector3 origin, uint targetNetId, Vector3 fallbackTargetPos)
    {
        Unit target = null;
        if (targetNetId != 0 && NetworkClient.spawned != null && NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity ni))
        {
            if (ni != null) target = ni.GetComponent<Unit>();
        }

        bool isServerAuth = isServer || !NetworkServer.active;
        SpawnArrow(origin, target, fallbackTargetPos, isServerAuth);
    }

    private void SpawnArrow(Vector3 origin, Unit target, Vector3 fallbackPos, bool isServerAuth)
    {
        AudioManager.Instance?.PlayBowRelease();

        GameObject arrowGo = customArrowPrefab != null
            ? Instantiate(customArrowPrefab, origin, Quaternion.identity)
            : new GameObject("TowerArrow");

        TowerArrow arrow = arrowGo.GetComponent<TowerArrow>();
        if (arrow == null) arrow = arrowGo.AddComponent<TowerArrow>();

        arrow.Initialize(origin, target, fallbackPos, attackDamage, isServerAuth);
    }

    private IEnumerator ServerDelayedDamage(Unit target, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (target != null && !target.IsDead)
        {
            target.TakeDamage(attackDamage);
        }
    }

    private static bool AreSameTeam(int playerA, int playerB)
    {
        if (playerA == playerB) return true;

        var m = typeof(PlayerColorManager).GetMethod("AreSameTeam", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (m != null)
            return (bool)m.Invoke(null, new object[] { playerA, playerB });

        return NetworkedPlayer.SameTeam(playerA, playerB);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}

/// <summary>
/// Ballistic 3D Arrow Projectile:
/// Launches from the watchtower battlement, traces a parabolic flight arc with a radiant
/// golden trail, and upon impact triggers a hit spark, arrow impact thud, and damage.
/// </summary>
public class TowerArrow : MonoBehaviour
{
    private Unit targetUnit;
    private Vector3 lastTargetPos;
    private Vector3 startPos;
    private int damage;
    private bool isServerAuth;
    private float flightDuration;
    private float arcHeight;
    private float elapsedTime = 0f;
    private TrailRenderer trail;
    private Vector3 prevPos;
    private bool impacted = false;

    public void Initialize(Vector3 start, Unit target, Vector3 fallbackTarget, int damageAmount, bool isServer)
    {
        startPos = start;
        targetUnit = target;
        lastTargetPos = target != null ? target.transform.position + Vector3.up * 0.8f : fallbackTarget;
        damage = damageAmount;
        isServerAuth = isServer;

        float dist = Vector3.Distance(start, lastTargetPos);
        float speed = 22f; // AoE4 realistic arrow speed
        flightDuration = Mathf.Clamp(dist / speed, 0.2f, 1.2f);
        arcHeight = Mathf.Clamp(dist * 0.22f, 1.0f, 3.2f);

        transform.position = start;
        prevPos = start;
        elapsedTime = 0f;

        BuildArrowVisuals();
    }

    private void BuildArrowVisuals()
    {
        // 1. Wooden shaft
        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(shaft.GetComponent<Collider>());
        shaft.name = "Shaft";
        shaft.transform.SetParent(transform, false);
        shaft.transform.localPosition = Vector3.zero;
        shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        shaft.transform.localScale = new Vector3(0.04f, 0.32f, 0.04f);

        Material woodMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Sprites/Default"));
        woodMat.color = new Color(0.38f, 0.24f, 0.12f);
        var shaftRend = shaft.GetComponent<Renderer>();
        if (shaftRend != null) shaftRend.material = woodMat;

        // 2. Steel tip
        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(tip.GetComponent<Collider>());
        tip.name = "Tip";
        tip.transform.SetParent(transform, false);
        tip.transform.localPosition = new Vector3(0f, 0f, 0.34f);
        tip.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        tip.transform.localScale = new Vector3(0.08f, 0.08f, 0.15f);

        Material tipMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Sprites/Default"));
        tipMat.color = new Color(0.82f, 0.85f, 0.9f);
        var tipRend = tip.GetComponent<Renderer>();
        if (tipRend != null) tipRend.material = tipMat;

        // 3. Fletching fins (feathers)
        GameObject f1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(f1.GetComponent<Collider>());
        f1.name = "Fletch1";
        f1.transform.SetParent(transform, false);
        f1.transform.localPosition = new Vector3(0f, 0f, -0.3f);
        f1.transform.localScale = new Vector3(0.12f, 0.015f, 0.12f);

        GameObject f2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(f2.GetComponent<Collider>());
        f2.name = "Fletch2";
        f2.transform.SetParent(transform, false);
        f2.transform.localPosition = new Vector3(0f, 0f, -0.3f);
        f2.transform.localScale = new Vector3(0.015f, 0.12f, 0.12f);

        Material fletchMat = new Material(Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Sprites/Default"));
        fletchMat.color = new Color(0.88f, 0.18f, 0.15f);
        var r1 = f1.GetComponent<Renderer>();
        var r2 = f2.GetComponent<Renderer>();
        if (r1 != null) r1.material = fletchMat;
        if (r2 != null) r2.material = fletchMat;

        // 4. Golden Flight Trail
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = 0.32f;
        trail.startWidth = 0.12f;
        trail.endWidth = 0.01f;
        trail.minVertexDistance = 0.08f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.92f, 0.35f), 0.0f),
                new GradientColorKey(new Color(1f, 0.65f, 0.1f), 0.5f),
                new GradientColorKey(new Color(0.9f, 0.35f, 0.05f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.85f, 0.0f),
                new GradientAlphaKey(0.55f, 0.6f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        trail.colorGradient = gradient;

        Material trailMat = EffectManager.GetOrCreateParticleMaterial();
        if (trailMat != null) trail.material = trailMat;
    }

    private void Update()
    {
        if (impacted) return;

        elapsedTime += Time.deltaTime;
        float t = Mathf.Clamp01(elapsedTime / flightDuration);

        if (targetUnit != null && !targetUnit.IsDead)
        {
            lastTargetPos = targetUnit.transform.position + Vector3.up * 0.8f;
        }

        Vector3 basePos = Vector3.Lerp(startPos, lastTargetPos, t);
        float arc = Mathf.Sin(t * Mathf.PI) * arcHeight;
        Vector3 currentPos = basePos + Vector3.up * arc;

        transform.position = currentPos;

        Vector3 moveDir = currentPos - prevPos;
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(moveDir);
        }
        prevPos = currentPos;

        if (t >= 1f)
        {
            OnImpact();
        }
    }

    private void OnImpact()
    {
        if (impacted) return;
        impacted = true;

        Vector3 hitPos = lastTargetPos;
        if (targetUnit != null && !targetUnit.IsDead)
        {
            hitPos = targetUnit.transform.position + Vector3.up * 0.8f;
            if (isServerAuth)
            {
                targetUnit.TakeDamage(damage);
            }
        }

        // Hit Spark & Arrow Impact Audio
        EffectManager.Instance?.PlayHitSpark(hitPos);
        AudioManager.Instance?.PlayArrowHit();

        // Gracefully detach trail so tail finishes fading smoothly
        if (trail != null)
        {
            trail.transform.parent = null;
            trail.autodestruct = true;
        }

        Destroy(gameObject);
    }
}
