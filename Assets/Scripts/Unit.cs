using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Collections;
using Mirror;

[RequireComponent(typeof(NavMeshAgent))]
public class Unit : NetworkBehaviour
{
    [Header("Ownership")]
    [SyncVar] [SerializeField] private int ownerPlayerId = 0;

    [Header("Stats")]
    [SerializeField] protected string unitName        = "Unit";
    [SerializeField] protected string unitType        = "Unknown";
    [SerializeField] protected string unitDescription = "";
    [SerializeField] protected int    maxHealth       = 100;
    [SerializeField] protected float  baseSpeed       = 3.5f;

    [Header("Combat")]
    [SerializeField] protected int   attackDamage   = 10;
    [SerializeField] protected float attackRange    = 2f;
    [SerializeField] protected float attackCooldown = 1.5f;

    [Header("Selection Visual (optional)")]
    [SerializeField] private GameObject selectionCircle;
    [SerializeField] private Renderer   unitRenderer;
    [SerializeField] private Material   outlineMaterial;

    [Header("Effects")]
    [SerializeField] private GameObject damageTextPrefab;

    [Header("Ground Snapping")]
    [SerializeField] private LayerMask groundLayer = ~0;
    [SerializeField] private float groundSnapOffset = 0.01f;
    [SerializeField] private float pivotToFeetOffset = 1.0f;

    [SyncVar(hook = nameof(OnSyncHealthChanged))]
    private int syncHealth;

    protected int          currentHealth;
    protected NavMeshAgent agent;
    private   bool         isSelected     = false;
    private   Material[]   originalMats;
    private   float        lastClickTime  = -99f;
    private   float        lastAttackTime = -99f;
    private   const float  DOUBLE_CLICK   = 0.3f;
    protected Unit         attackTarget;
    public string huntingUnitName = "";
    private List<Vector3> waypoints = new List<Vector3>();
    private Coroutine waypointCoroutine;
    private bool _movementOverride = false;
    public bool processingWaypoints = false;
    protected bool isDying = false;
    protected bool suppressRenderers = false;
    private Renderer[] allRenderers;
    private Building buildingTarget = null;
    protected virtual void OnBeforeDestroy() { }
    protected virtual float GetDestroyDelay() => 0f;

    private float nextTargetSearchTime = 0f;
    private const float TARGET_SEARCH_INTERVAL = 0.5f;

    private void SnapToGround()
    {
        Vector3 origin = transform.position + Vector3.up * 5f;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 10f, groundLayer, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<Unit>() != null) return;
            if (hit.collider.GetComponentInParent<Building>() != null) return;
            if (hit.collider.GetComponentInParent<ResourceNode>() != null) return;

            float targetY = hit.point.y + groundSnapOffset + pivotToFeetOffset;
            float currentY = transform.position.y;
            if (currentY > targetY + 0.02f)
            {
                Vector3 pos = transform.position;
                pos.y = targetY;
                transform.position = pos;
            }
        }

        Debug.DrawLine(origin, origin + Vector3.down * 10f, Color.green, 0f, false);
    }

    protected virtual void Awake()
    {
        agent         = GetComponent<NavMeshAgent>();
        if (isServer) syncHealth = maxHealth;
        currentHealth = maxHealth;
        agent.speed   = baseSpeed;

        if (unitRenderer == null)
            unitRenderer = GetComponentInChildren<Renderer>();
        if (unitRenderer != null)
            originalMats = unitRenderer.sharedMaterials;
        if (selectionCircle != null)
            selectionCircle.SetActive(false);

        if (GetComponent<Collider>() == null &&
            GetComponentInChildren<Collider>() == null)
        {
            CapsuleCollider cc = gameObject.AddComponent<CapsuleCollider>();
            cc.height = 1f; cc.radius = 0.25f;
            cc.center = new Vector3(0, 0.5f, 0);
        }

        allRenderers = GetComponentsInChildren<Renderer>(true);
        UnitSelectionManager.Instance?.Register(this);
        MinimapSystem.Instance?.TrackUnit(this);
    }

    protected virtual void Start() { }

    protected virtual void Update()
    {
        if (isDying) return;

        if (isServer)
        {
            if (!_movementOverride)
            {
                if (!string.IsNullOrEmpty(huntingUnitName) && attackTarget == null)
                {
                    if (Time.time >= nextTargetSearchTime)
                    {
                        nextTargetSearchTime = Time.time + TARGET_SEARCH_INTERVAL;
                        FindNextTarget();
                    }
                }

                if (attackTarget != null)
                    ChaseAndAttack();

                bool hadBT = buildingTarget != null;
                if (buildingTarget != null)
                    ChaseAndAttackBuilding();

                if (hadBT && buildingTarget == null && attackTarget == null &&
                    !(this is Villager))
                {
                    buildingTarget = FindNearestEnemyBuilding(25f);
                }

                if (!(this is Villager) &&
                    attackTarget == null && buildingTarget == null &&
                    string.IsNullOrEmpty(huntingUnitName) &&
                    ownerPlayerId != PlayerColorManager.LocalPlayerIndex &&
                    Time.time >= nextTargetSearchTime)
                {
                    nextTargetSearchTime = Time.time + TARGET_SEARCH_INTERVAL * 4f;
                    buildingTarget = FindNearestEnemyBuilding(25f);
                }
            }
            else if (agent != null && agent.isOnNavMesh && !agent.pathPending &&
                    agent.remainingDistance <= agent.stoppingDistance + 0.2f &&
                    !processingWaypoints)
            {
                _movementOverride = false;
            }
        }
    }

    private void LateUpdate()
    {
        if (MapBoundary.Instance == null) return;
        Vector3 pos = transform.position;
        Vector3 clamped = MapBoundary.Instance.Clamp(pos);
        if (pos != clamped)
        {
            transform.position = clamped;
            if (agent != null && agent.isOnNavMesh)
                agent.Warp(clamped);
        }
        SnapToGround();

        if (isClient)
        {
            if (ownerPlayerId != PlayerColorManager.LocalPlayerIndex)
            {
                bool isAlly = NetworkedPlayer.LocalInstance != null
                    && NetworkedPlayer.SameTeam(PlayerColorManager.LocalPlayerIndex, ownerPlayerId);
                bool vis = isAlly
                    || FogOfWar.Instance == null
                    || FogOfWar.Instance.IsVisible(transform.position);
                if (allRenderers != null)
                    foreach (Renderer r in allRenderers)
                        if (r != null) r.enabled = vis;
            }
            else
            {
                if (suppressRenderers) { }
                else if ((Time.frameCount + GetInstanceID()) % 8 == 0)
                {
                    Camera cam = Camera.main;
                    bool inFrustum = true;
                    if (cam != null)
                    {
                        Vector3 vp = cam.WorldToViewportPoint(transform.position);
                        inFrustum = vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f;
                    }
                    if (allRenderers != null)
                        foreach (Renderer r in allRenderers)
                            if (r != null) r.enabled = inFrustum;
                }
            }
        }
    }

    private float mouseDownTime = 0f;

    private void OnMouseDown()
    {
        if (!IsSelectable) return;
        if (SelectionManager.IsPointerOverInteractiveUI()) return;
        mouseDownTime = Time.unscaledTime;
    }

    private void OnMouseUp()
    {
        if (!IsSelectable) return;
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject()) return;

        if (Time.unscaledTime - mouseDownTime > 0.2f) return;
        if (UnitSelectionBox.Instance != null && UnitSelectionBox.Instance.JustFinishedDrag)
            return;

        float now      = Time.unscaledTime;
        bool  isDouble = (now - lastClickTime) <= DOUBLE_CLICK;
        lastClickTime  = now;

        if (ownerPlayerId != PlayerColorManager.LocalPlayerIndex)
        {
            UnitInfoUI.Instance?.ShowEnemyUnit(this);
            return;
        }

        if (isDouble)
            SelectionManager.Instance?.SelectAllVisibleOfType(unitName);
        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            SelectionManager.Instance?.ShiftClickUnit(this);
        else
            SelectionManager.Instance?.SelectSingleUnit(this);
    }

    private void ChaseAndAttack()
    {
        if (attackTarget == null || !attackTarget.gameObject.activeInHierarchy)
        {
            attackTarget = null;
            return;
        }

        float dist = Vector3.Distance(transform.position, attackTarget.transform.position);
        if (dist <= attackRange)
        {
            agent.ResetPath();
            transform.LookAt(attackTarget.transform);
            if (Time.time - lastAttackTime >= attackCooldown)
            {
                lastAttackTime = Time.time;
                PerformAttack(attackTarget);
            }
        }
        else agent.SetDestination(attackTarget.transform.position);
    }

    private void ChaseAndAttackBuilding()
    {
        if (attackTarget != null) return;
        if (buildingTarget == null || !buildingTarget.gameObject.activeInHierarchy)
        {
            buildingTarget = null;
            return;
        }
        float dist = Vector3.Distance(transform.position, buildingTarget.transform.position);
        if (dist <= attackRange + 1.5f)
        {
            agent.ResetPath();
            transform.LookAt(new Vector3(
                buildingTarget.transform.position.x,
                transform.position.y,
                buildingTarget.transform.position.z));
            if (Time.time - lastAttackTime >= attackCooldown)
            {
                lastAttackTime = Time.time;
                PerformBuildingAttack(buildingTarget);
            }
        }
        else
        {
            agent.SetDestination(buildingTarget.transform.position);
        }
    }

    public void FindNextTarget()
    {
        attackTarget = null;
        if (string.IsNullOrEmpty(huntingUnitName)) return;

        float    searchRadius = 20f;
        Collider[] nearby     = Physics.OverlapSphere(transform.position, searchRadius);
        Unit      closest     = null;
        float     closestDist = float.MaxValue;

        foreach (Collider c in nearby)
        {
            Unit u = c.GetComponentInParent<Unit>();
            if (u == null || u == this) continue;
            if (!IsEnemy(u)) continue;
            if (u.UnitName != huntingUnitName) continue;

            float d = Vector3.Distance(transform.position, u.transform.position);
            if (d < closestDist) { closestDist = d; closest = u; }
        }

        attackTarget = closest;
        if (attackTarget == null)
            huntingUnitName = "";
    }

    protected virtual void PerformAttack(Unit target)
    {
        target.TakeDamage(attackDamage, this);
    }

    protected virtual void PerformBuildingAttack(Building target)
    {
        target.TakeDamage(attackDamage);
    }

    public void SetAttackTarget(Unit t) { _movementOverride = false; attackTarget = t; }
    public void ClearAttackTarget()     { attackTarget = null; }

    public virtual void Select(Color c)
    {
        isSelected = true;
        if (selectionCircle != null)
        {
            selectionCircle.SetActive(true);
            Renderer r = selectionCircle.GetComponent<Renderer>();
            if (r != null)
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                Color circleColor = PlayerColorManager.Instance != null
                    ? PlayerColorManager.Instance.GetColor(ownerPlayerId) : c;
                mpb.SetColor("_Color", circleColor);
                r.SetPropertyBlock(mpb);
            }
        }
        if (unitRenderer != null && outlineMaterial != null)
        {
            var mats = new Material[originalMats.Length + 1];
            originalMats.CopyTo(mats, 0);
            mats[mats.Length - 1] = outlineMaterial;
            unitRenderer.materials = mats;
        }
    }

    public virtual void Deselect()
    {
        isSelected = false;
        if (selectionCircle != null) selectionCircle.SetActive(false);
        if (unitRenderer != null && originalMats != null)
            unitRenderer.materials = originalMats;
    }

    public void SetMoveSpeed(float s) { if (agent != null) agent.speed = s; }
    public virtual void RestoreSpeed() { if (agent != null) agent.speed = baseSpeed; }

    public virtual void TakeDamage(int amount, Unit damageSource = null)
    {
        if (isClient && !isServer) return;
        if (isDying) return;
        syncHealth -= amount;
        currentHealth = syncHealth;

        if (isServer) RpcOnTakeDamage(amount, transform.position);

        if (syncHealth <= 0) { Die(); return; }

        if (attackTarget == null)
        {
            if (damageSource != null && IsEnemy(damageSource))
                attackTarget = damageSource;
            else
                attackTarget = FindAttacker();
        }

        Unit threatForAllies = attackTarget;
        if (threatForAllies == null && damageSource != null && IsEnemy(damageSource))
            threatForAllies = damageSource;
        if (threatForAllies == null)
            threatForAllies = FindAttacker();

        AlertNearbyAllies(threatForAllies);
    }

    [ClientRpc]
    private void RpcOnTakeDamage(int amount, Vector3 pos)
    {
        if (isServer) return;
        EffectManager.Instance?.PlayHitEffect(pos);
        if (damageTextPrefab != null)
        {
            FloatingDamageText dmg = Instantiate(damageTextPrefab, pos + Vector3.up * 1.5f, Quaternion.identity)
                .GetComponent<FloatingDamageText>();
            if (dmg != null) dmg.SetDamage(amount);
        }
    }

    [ClientRpc]
    private void RpcOnDeath(Vector3 pos)
    {
        if (isServer) return;
        EffectManager.Instance?.PlayDeathEffect(pos);
    }

    private void OnSyncHealthChanged(int oldVal, int newVal)
    {
        currentHealth = newVal;
    }

    public bool IsEnemy(Unit other)
    {
        if (other == null || other == this) return false;
        if (other.ownerPlayerId == ownerPlayerId) return false;
        return !NetworkedPlayer.SameTeam(ownerPlayerId, other.ownerPlayerId);
    }

    public void SetOwner(int playerId) { ownerPlayerId = playerId; }

    private Unit FindAttacker()
    {
        float      radius = 15f;
        Collider[] hits   = Physics.OverlapSphere(transform.position, radius);
        Unit       best   = null;
        float      bestD  = float.MaxValue;

        foreach (Collider c in hits)
        {
            Unit u = c.GetComponentInParent<Unit>();
            if (u == null || u == this) continue;
            if (!IsEnemy(u)) continue;
            float d = Vector3.Distance(transform.position, u.transform.position);
            if (d < bestD) { bestD = d; best = u; }
        }
        return best;
    }

    private void AlertNearbyAllies(Unit threat)
    {
        if (threat == null) return;
        float      radius = 12f;
        Collider[] hits   = Physics.OverlapSphere(transform.position, radius);

        foreach (Collider c in hits)
        {
            Unit ally = c.GetComponentInParent<Unit>();
            if (ally == null || ally == this) continue;
            if (ally.OwnerPlayerId != ownerPlayerId) continue;
            if (ally is Villager) continue;
            if (ally.attackTarget != null) continue;

            ally.attackTarget    = threat;
            ally.huntingUnitName = threat.UnitName;
            ally.buildingTarget  = null;
        }
    }

    private Building FindNearestEnemyBuilding(float maxDist)
    {
        Building nearest = null;
        float    best    = float.MaxValue;
        foreach (Building b in Building.AllBuildings)
        {
            if (b == null || b.OwnerPlayerId == ownerPlayerId) continue;
            if (NetworkedPlayer.SameTeam(ownerPlayerId, b.OwnerPlayerId)) continue;
            float d = Vector3.Distance(transform.position, b.transform.position);
            if (d < maxDist && d < best) { best = d; nearest = b; }
        }
        return nearest;
    }

    protected virtual void Die()
    {
        if (isClient && !isServer) return;
        if (isDying) return;
        isDying = true;

        if (isServer) RpcOnDeath(transform.position);

        Collider[] nearby = Physics.OverlapSphere(transform.position, 20f);
        foreach (Collider c in nearby)
        {
            Unit u = c.GetComponentInParent<Unit>();
            if (u == null || u == this) continue;
            if (u.attackTarget == this)
                u.FindNextTarget();
        }

        UnitSelectionManager.Instance?.Unregister(this);
        MinimapSystem.Instance?.Untrack(transform);
        if (isSelected) UnitInfoUI.Instance?.Hide();

        if (agent != null && agent.isOnNavMesh)
            agent.isStopped = true;
        buildingTarget = null;
        NetworkedPlayer owner = NetworkedPlayer.Get(ownerPlayerId);
        if (owner != null) owner.RemovePopulation(1);
        else if (ownerPlayerId == PlayerColorManager.LocalPlayerIndex)
            ResourceManager.Instance?.RemovePopulation(1);
        else
            EnemyAI.Instance?.RemovePopulation(1);
        OnBeforeDestroy();
        if (NetworkServer.active)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    public void SetHuntTarget(string unitName)
    {
        _movementOverride = false;
        huntingUnitName = unitName;
    }

    public virtual void MoveTo(Vector3 dest)
    {
        _movementOverride = true;
        attackTarget     = null;
        buildingTarget   = null;
        huntingUnitName  = "";
        if (MapBoundary.Instance != null)
            dest = MapBoundary.Instance.Clamp(dest);

        ClearAttackTarget();

        if (agent == null || !agent.isOnNavMesh)
            return;

        agent.SetDestination(dest);
    }

    public void SetBuildingTarget(Building b) { _movementOverride = false; buildingTarget = b; attackTarget = null; }

    private IEnumerator ProcessWaypoints()
    {
        processingWaypoints = true;
        while (waypoints.Count > 0)
        {
            Vector3 target = waypoints[0];

            if (agent != null && agent.isOnNavMesh && agent.destination != target)
                agent.SetDestination(target);

            while (true)
            {
                if (agent == null || !agent.isOnNavMesh) break;
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
                    break;
                yield return null;
            }

            waypoints.RemoveAt(0);
        }
        processingWaypoints = false;
        _movementOverride    = false;
        waypointCoroutine = null;
    }

    public void AddWaypoint(Vector3 point)
    {
        if (MapBoundary.Instance != null)
            point = MapBoundary.Instance.Clamp(point);
        waypoints.Add(point);
        if (!processingWaypoints)
            waypointCoroutine = StartCoroutine(ProcessWaypoints());
    }

    public void SetFirstWaypoint(Vector3 point)
    {
        if (MapBoundary.Instance != null)
            point = MapBoundary.Instance.Clamp(point);

        waypoints.Clear();
        waypoints.Add(point);

        MoveTo(point);

        if (!processingWaypoints)
            waypointCoroutine = StartCoroutine(ProcessWaypoints());
    }

    public void ClearWaypoints()
    {
        _movementOverride = false;
        waypoints.Clear();
        processingWaypoints = false;
        if (waypointCoroutine != null)
        {
            StopCoroutine(waypointCoroutine);
            waypointCoroutine = null;
        }
    }

    public bool     HasActiveTarget   => attackTarget != null || buildingTarget != null;
    public bool     IsInsideBuilding  => suppressRenderers;
    public string   UnitName          => unitName;
    public string   UnitType          => unitType;
    public string   UnitDescription   => unitDescription;
    public int      MaxHealth         => maxHealth;
    public int      CurrentHealth     => currentHealth;
    public float    BaseSpeed         => baseSpeed;
    public bool     IsSelected        => isSelected;
    public int      OwnerPlayerId     => ownerPlayerId;
    public int      AttackDamage       => attackDamage;
    public float    AttackRange        => attackRange;
    public virtual bool IsSelectable => true;
}
