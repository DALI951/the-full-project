using UnityEngine;
using UnityEngine.AI;
using Mirror;

public class Villager : Unit
{
    [Header("Animation")]
    public Animator animator;
    [Tooltip("Length of Punching clip (enemy units)")]
    public float punchAnimDuration = 0.8f;
    [Tooltip("Length of Thrust clip (animals)")]
    public float thrustAnimDuration = 0.8f;
    [Tooltip("Length of death clip")]
    public float deathAnimDuration = 2.0f;

    [Header("Tools")]
    public Transform axe;
    public Transform knife;
    public Transform shovel;

    [Header("Gathering")]
    [SerializeField] private float gatherRange    = 1.2f;
    [SerializeField] private float gatherInterval = 2f;
    [SerializeField] private int   gatherAmount   = 10;
    [SerializeField] private float orbitRadius    = 2.5f;

    private enum VState
    {
        Idle,
        MovingToCommand,
        MovingToResource,
        AttackingAnimal,
        Gathering,
        MovingToBuild,
        Building,
        MovingToResourceBuilding,
        WorkingInBuilding
    }

    private VState      state          = VState.Idle;
    [SyncVar(hook = nameof(OnAnimStateChanged))]
    private int animState = 0; // 0=idle/moving, 1=chopping, 2=digging, 3=gathering, 4=attackingAnimal, 5=building, 6=workingInBuilding
    private ResourceNode targetNode    = null;
    private int          gatherSlot     = -1;
    private float        lastActionTime = -99f;
    private ConstructionSite targetSite = null;
    private ResourceBuilding assignedBuilding = null;
    private Vector3   _lastVillagerPos;
    private bool      _wasVillagerMoving;
    private int       _villagerMoveStoppedFrames;

    protected override void Awake()
    {
        unitName        = "Villager";
        unitType        = "Villager";
        unitDescription = "Gathers resources. Right-click a tree, mine or animal.";
        maxHealth       = 60;
        baseSpeed       = 3f;
        attackDamage    = 15;
        attackRange     = 2f;
        attackCooldown  = 1f;
        base.Awake();

        if (animator == null)
            animator = GetComponent<Animator>();

        if (agent != null)
        {
            agent.avoidancePriority = Random.Range(1, 100);
            agent.radius = 0.4f;
        }

        _lastVillagerPos = transform.position;
    }

    protected override void Update()
    {
        base.Update();

        if (targetNode == null)
        {
            targetNode = null;
            gatherSlot = -1;
            EnterState(VState.Idle);
        }
        if (targetSite == null)
        {
            targetSite = null;
            EnterState(VState.Idle);
        }

        // ── Animation handling ─────────────────────────────────────────
        if (!isDying && animator != null)
        {
            Vector3 pos = transform.position;
            bool moving = Vector3.SqrMagnitude(pos - _lastVillagerPos) > 0.001f;
            _lastVillagerPos = pos;

            bool isBusy = animState > 0;
            bool canMove = moving && !isBusy;

            if (canMove)
            {
                _villagerMoveStoppedFrames = 0;
                if (!_wasVillagerMoving)
                {
                    _wasVillagerMoving = true;
                    animator.SetBool("IsMoving", true);
                }
            }
            else
            {
                _villagerMoveStoppedFrames++;
                if (_villagerMoveStoppedFrames >= 3 && _wasVillagerMoving)
                {
                    _wasVillagerMoving = false;
                    animator.SetBool("IsMoving", false);
                }
            }
        }

        if (NetworkClient.active && !isServer) return;

        switch (state)
        {
            case VState.Idle: break;
            case VState.MovingToCommand:
                if (AgentStopped()) EnterState(VState.Idle);
                break;
            case VState.MovingToResource: TickMovingToResource(); break;
            case VState.AttackingAnimal:    TickAttackingAnimal(); break;
            case VState.Gathering:          TickGathering(); break;
            case VState.MovingToBuild:      TickMovingToBuild(); break;
            case VState.Building:           TickBuilding(); break;
            case VState.MovingToResourceBuilding: TickMovingToResourceBuilding(); break;
            case VState.WorkingInBuilding: break;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────

    public virtual void GatherFrom(ResourceNode node)
    {
        if (node == null || node.IsEmpty) return;

        ClearWaypoints();
        ReleaseCurrentNode();

        int slot = node.ReserveSlot(this);

        if (slot < 0)
        {
            gatherSlot = -1;
            MoveTo(node.transform.position);
            targetNode = node;
            EnterState(VState.MovingToResource);
            return;
        }

        targetNode = node;
        gatherSlot = slot;

        if (node.RequiresKill && !node.IsKilled)
            EnterState(VState.MovingToResource);
        else
            MoveToGatherSlot();
    }

    public override void MoveTo(Vector3 dest)
    {
        if (assignedBuilding != null)
        {
            assignedBuilding.OnVillagerLeft(this);
            assignedBuilding = null;
            var rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) if (r) r.enabled = true;
        }
        ReleaseCurrentNode();
        targetSite = null;
        ClearWaypoints();
        base.MoveTo(dest);
        EnterState(VState.MovingToCommand);
    }

    public void OnSlotPromoted(ResourceNode node, int slot)
    {
        if (targetNode == node && gatherSlot < 0)
        {
            gatherSlot = slot;
            Debug.Log($"[{name}] Promoted to slot {slot} at {node.name}");
            if (!node.RequiresKill || node.IsKilled)
                MoveToGatherSlot();
        }
    }

    // ── State Ticks ────────────────────────────────────────────────────

    private void TickMovingToResource()
    {
        if (targetNode == null || targetNode.IsEmpty)
        {
            EnterState(VState.Idle);
            AutoFindNext();
            return;
        }

        if (gatherSlot < 0)
        {
            float dist = Dist(targetNode);
            if (dist <= gatherRange + orbitRadius)
            {
                agent.ResetPath();
                if (targetNode != null)
                {
                    int newSlot = targetNode.ReserveSlot(this);
                    if (newSlot >= 0)
                    {
                        gatherSlot = newSlot;
                        if (!targetNode.RequiresKill || targetNode.IsKilled)
                            MoveToGatherSlot();
                    }
                }
            }
            return;
        }

        float d = Dist(targetNode);

        if (targetNode.RequiresKill && !targetNode.IsKilled)
        {
            if (d <= attackRange) EnterState(VState.AttackingAnimal);
        }
        else
        {
            if (d <= gatherRange + 0.5f) EnterState(VState.Gathering);
        }
    }

    private void TickAttackingAnimal()
    {
        if (targetNode == null || targetNode.IsEmpty || targetNode.IsKilled)
        {
            if (targetNode != null && !targetNode.IsEmpty)
            { 
                ShowKnife(false);
                MoveToGatherSlot(); 
                return; 
            }
            HideTools();
            AutoFindNext();
            return;
        }

        float dist = Dist(targetNode);
        if (dist > attackRange)
        {
            agent.SetDestination(targetNode.transform.position);
            return;
        }

        agent.ResetPath();
        Vector3 flatTarget = targetNode.transform.position;
        flatTarget.y = transform.position.y;
        transform.LookAt(flatTarget);

        if (Time.time - lastActionTime >= attackCooldown)
        {
            lastActionTime = Time.time;

            PlayThrustAnim();
            if (isServer) RpcPlayThrustAnim();

            targetNode.TakeDamage(attackDamage);

            if (targetNode.IsKilled)
            {
                // Animal died — hide knife, switch to gathering meat
                ShowKnife(false);
                
                if (targetNode != null)
                {
                    targetNode.ReleaseSlot(this);
                    int newSlot = targetNode.ReserveSlot(this);
                    gatherSlot = Mathf.Max(0, newSlot);
                }
                MoveToGatherSlot();
            }
        }
    }

    private void TickGathering()
    {
        if (targetNode == null || targetNode.IsEmpty)
        {
            AutoFindNext();
            return;
        }

        if (gatherSlot < 0)
        {
            int newSlot = targetNode.ReserveSlot(this);
            if (newSlot < 0)
            {
                MoveTo(targetNode.transform.position);
                return;
            }
            gatherSlot = newSlot;
        }

        float dist = Dist(targetNode);
        if (dist > gatherRange + 1.0f)
        {
            MoveToGatherSlot();
            return;
        }

        agent.ResetPath();
        Vector3 flatTarget = targetNode.transform.position;
        flatTarget.y = transform.position.y; // Lock to same height
        transform.LookAt(flatTarget);

        if (Time.time - lastActionTime >= gatherInterval)
        {
            lastActionTime = Time.time;
            int gathered = targetNode.Gather(gatherAmount);

            NetworkedPlayer owner = NetworkedPlayer.Get(OwnerPlayerId);
            bool isLocalPlayer = OwnerPlayerId == PlayerColorManager.LocalPlayerIndex;
            if (owner != null)
            {
                switch (targetNode.ResourceType)
                {
                    case ResourceType.Wood: owner.AddResources(0, gathered, 0); break;
                    case ResourceType.Gold: owner.AddResources(0, 0, gathered); break;
                    case ResourceType.Food: owner.AddResources(gathered, 0, 0); break;
                }
            }
            else if (isLocalPlayer)
            {
                switch (targetNode.ResourceType)
                {
                    case ResourceType.Wood: ResourceManager.Instance?.AddResources(0, gathered, 0); break;
                    case ResourceType.Gold: ResourceManager.Instance?.AddResources(0, 0, gathered); break;
                    case ResourceType.Food: ResourceManager.Instance?.AddResources(gathered, 0, 0); break;
                }
            }
            else EnemyAI.Instance?.AddEnemyResources(
                targetNode.ResourceType == ResourceType.Food ? gathered : 0,
                targetNode.ResourceType == ResourceType.Wood ? gathered : 0,
                targetNode.ResourceType == ResourceType.Gold ? gathered : 0);

            if (targetNode.IsEmpty) AutoFindNext();
        }
    }

    // ── Enemy unit combat (Punching) ───────────────────────────────────
    protected override void PerformAttack(Unit target)
    {
        base.PerformAttack(target);
        PlayPunchAnim();
        if (isServer) RpcPlayPunchAnim();
    }

    [ClientRpc]
    private void RpcPlayPunchAnim()
    {
        if (isServer) return;
        PlayPunchAnim();
    }

    private void PlayPunchAnim()
    {
        if (animator == null) return;
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Punching"))
            return;
        animator.SetBool("IsPunching", true);
        CancelInvoke(nameof(ResetPunch));
        Invoke(nameof(ResetPunch), punchAnimDuration);
    }

    private void ResetPunch()
    {
        animator?.SetBool("IsPunching", false);
    }

    private void ResetThrust()
    {
        animator?.SetBool("IsThrusting", false);
    }

    private void PlayThrustAnim()
    {
        if (animator == null) return;
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Thrust"))
            return;
        animator.SetBool("IsThrusting", true);
        CancelInvoke(nameof(ResetThrust));
        Invoke(nameof(ResetThrust), thrustAnimDuration);
    }

    [ClientRpc]
    private void RpcPlayThrustAnim()
    {
        if (isServer) return;
        PlayThrustAnim();
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private void MoveToGatherSlot()
    {
        if (targetNode == null) return;
        if (gatherSlot < 0) return;

        int   totalSlots = Mathf.Max(targetNode.MaxGatherers, 1);
        float angleStep  = 360f / totalSlots;
        float angle      = angleStep * gatherSlot;
        
        Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * orbitRadius;
        Vector3 dest   = targetNode.transform.position + offset;

        if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            dest = hit.position;

        agent.SetDestination(dest);
        EnterState(VState.MovingToResource);
    }

    private void AutoFindNext()
    {
        ResourceType type = targetNode != null ? targetNode.ResourceType : ResourceType.Wood;
        ReleaseCurrentNode();
        EnterState(VState.Idle);

        ResourceNode next = ResourceNode.FindNearest(transform.position, type, null, 50f);
        if (next != null)
        {
            GatherFrom(next);
            return;
        }

        ResourceNode any = ResourceNode.FindNearestAny(transform.position, 80f, null);
        if (any != null)
        {
            GatherFrom(any);
            return;
        }

        Debug.Log($"[{name}] No resources available nearby — going idle");
    }

    private void ReleaseCurrentNode()
    {
        if (targetNode != null)
        {
            targetNode.ReleaseSlot(this);
            targetNode = null;
        }
        gatherSlot = -1;
    }

    private float Dist(ResourceNode node) =>
        node == null ? float.MaxValue : Vector3.Distance(transform.position, node.transform.position);

    private bool AgentStopped() =>
        agent != null && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;

    private void EnterState(VState next)
    {
        // Always clear all work/attack animation bools when leaving any state
        if (animator != null)
        {
            animator.SetBool("IsChopping", false);
            animator.SetBool("IsDigging", false);
            animator.SetBool("IsGathering", false);
            animator.SetBool("IsThrusting", false);
            animator.SetBool("IsPunching", false);
        }

        state = next;

        if ((state == VState.Gathering || state == VState.AttackingAnimal) && animator != null)
        {
            animator.SetBool("IsMoving", false);
            _wasVillagerMoving = false;
        }

        if (state == VState.Gathering && targetNode != null && animator != null)
        {
            if (targetNode.ResourceType == ResourceType.Wood)
            {
                animator.SetBool("IsChopping", true);
                ShowAxe(true);
            }
            else if (targetNode.ResourceType == ResourceType.Gold)
            {
                animator.SetBool("IsDigging", true);
                ShowShovel(true);
            }
            else if (targetNode.ResourceType == ResourceType.Food)
            {
                animator.SetBool("IsGathering", true);
                HideTools();
            }
            else
            {
                animator.SetBool("IsChopping", true);
                ShowAxe(true);
            }
        }

        switch (state)
        {
            case VState.Idle:
                if (agent != null && agent.isOnNavMesh) agent.ResetPath();
                HideTools();
                break;
            case VState.AttackingAnimal:
                if (agent != null && agent.isOnNavMesh) agent.ResetPath();
                ShowKnife(true);
                break;
            case VState.MovingToCommand:
                HideTools();
                break;
        }

        // Sync persistent animation state to clients
        if (isServer) ServerSyncAnimState();
    }

    [Server]
    private void ServerSyncAnimState()
    {
        int newState = 0;
        if (state == VState.Idle || state == VState.MovingToCommand || state == VState.MovingToResource
            || state == VState.MovingToBuild || state == VState.MovingToResourceBuilding)
            newState = 0;
        else if (state == VState.Gathering && targetNode != null)
        {
            if (targetNode.ResourceType == ResourceType.Wood) newState = 1;
            else if (targetNode.ResourceType == ResourceType.Gold) newState = 2;
            else if (targetNode.ResourceType == ResourceType.Food) newState = 3;
            else newState = 1;
        }
        else if (state == VState.AttackingAnimal) newState = 4;
        else if (state == VState.Building) newState = 5;
        else if (state == VState.WorkingInBuilding) newState = 6;

        if (newState != animState)
            animState = newState;
    }

    private void OnAnimStateChanged(int oldVal, int newVal)
    {
        if (!isClient) return;

        animator.SetBool("IsMoving", false);
        animator.SetBool("IsChopping", false);
        animator.SetBool("IsDigging", false);
        animator.SetBool("IsGathering", false);
        animator.SetBool("IsThrusting", false);
        animator.SetBool("IsPunching", false);
        HideTools();

        switch (newVal)
        {
            case 1:
                animator.SetBool("IsChopping", true);
                ShowAxe(true);
                break;
            case 2:
                animator.SetBool("IsDigging", true);
                ShowShovel(true);
                break;
            case 3:
                animator.SetBool("IsGathering", true);
                break;
            case 4:
                animator.SetBool("IsThrusting", true);
                ShowKnife(true);
                break;
        }
    }

    [ClientRpc]
    private void RpcPlayDeathAnim()
    {
        if (isServer) return;
        if (animator != null)
        {
            animator.SetBool("Die", true);
            animator.SetBool("IsMoving", false);
            animator.SetBool("IsChopping", false);
            animator.SetBool("IsDigging", false);
            animator.SetBool("IsGathering", false);
            animator.SetBool("IsPunching", false);
            animator.SetBool("IsThrusting", false);
        }
        HideTools();
    }

    // ── Building ───────────────────────────────────────────────────────

    public void BuildAt(ConstructionSite site)
    {
        ReleaseCurrentNode();
        ClearWaypoints(); 
        targetSite = site;
        site.AssignBuilder(this);
        agent.SetDestination(site.transform.position);
        EnterState(VState.MovingToBuild);
    }

    private void TickMovingToBuild()
    {
        if (targetSite == null || targetSite.gameObject == null)
        { targetSite = null; EnterState(VState.Idle); return; }
        if (Vector3.Distance(transform.position, targetSite.transform.position) <= 2.5f)
            EnterState(VState.Building);
    }

    private void TickBuilding()
    {
        if (targetSite == null || targetSite.gameObject == null || targetSite.IsComplete)
        { targetSite = null; EnterState(VState.Idle); return; }
        agent.ResetPath();
        Vector3 flatTarget = targetSite.transform.position;
        flatTarget.y = transform.position.y;
        transform.LookAt(flatTarget);
    }

    public void OnBuildingComplete()
    {
        targetSite = null;
        EnterState(VState.Idle);
    }

    // ── Resource Building Work ─────────────────────────────────────────

    public void AssignToResourceBuilding(ResourceBuilding b)
    {
        ReleaseCurrentNode();
        ClearWaypoints();
        targetSite = null;
        if (assignedBuilding != null) assignedBuilding.OnVillagerLeft(this);
        assignedBuilding = b;
        agent.SetDestination(b.transform.position);
        EnterState(VState.MovingToResourceBuilding);
    }

    public void ExitResourceBuilding(Vector3 spawnPos)
    {
        assignedBuilding = null;
        suppressRenderers = false;
        var rends = GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends) if (r) r.enabled = true;
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
        if (agent != null && agent.isOnNavMesh) agent.Warp(spawnPos);
        EnterState(VState.Idle);
    }

    private void TickMovingToResourceBuilding()
    {
        if (assignedBuilding == null) { EnterState(VState.Idle); return; }
        if (Vector3.Distance(transform.position, assignedBuilding.transform.position) <= 2f)
        {
            suppressRenderers = true;
            var rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) if (r) r.enabled = false;
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (agent != null && agent.isOnNavMesh) agent.ResetPath();
            assignedBuilding.OnVillagerArrived(this);
            EnterState(VState.WorkingInBuilding);
        }
    }

    // ── Death ──────────────────────────────────────────────────────────

    protected override void OnBeforeDestroy()
    {
        if (animator != null)
        {
            animator.SetBool("Die", true);
            animator.SetBool("IsMoving", false);
            animator.SetBool("IsChopping", false);
            animator.SetBool("IsDigging", false);
            animator.SetBool("IsGathering", false);
            animator.SetBool("IsPunching", false);
            animator.SetBool("IsThrusting", false);
        }

        if (isServer) RpcPlayDeathAnim();

        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        HideTools();

        Transform ring = transform.Find("SelectionCircle");
        if (ring != null) ring.gameObject.SetActive(false);

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    protected override float GetDestroyDelay() => deathAnimDuration;

    protected override void Die()
    {
        ReleaseCurrentNode();
        targetSite = null;
        if (assignedBuilding != null) { assignedBuilding.OnVillagerLeft(this); assignedBuilding = null; }
        base.Die();
    }

    private void ShowAxe(bool show)
    {
        if (axe != null) axe.gameObject.SetActive(show);
        if (knife != null) knife.gameObject.SetActive(false);
        if (shovel != null) shovel.gameObject.SetActive(false);
    }

    private void ShowKnife(bool show)
    {
        if (knife != null) knife.gameObject.SetActive(show);
        if (axe != null) axe.gameObject.SetActive(false);
        if (shovel != null) shovel.gameObject.SetActive(false);
    }

    private void ShowShovel(bool show)
    {
        if (shovel != null) shovel.gameObject.SetActive(show);
        if (axe != null) axe.gameObject.SetActive(false);
        if (knife != null) knife.gameObject.SetActive(false);
    }

    private void HideTools()
    {
        if (axe != null) axe.gameObject.SetActive(false);
        if (knife != null) knife.gameObject.SetActive(false);
        if (shovel != null) shovel.gameObject.SetActive(false);
    }

    public bool IsGathering => state == VState.Gathering
                            || state == VState.MovingToResource
                            || state == VState.AttackingAnimal
                            || state == VState.MovingToResourceBuilding
                            || state == VState.WorkingInBuilding;

    public bool IsIdle => state == VState.Idle && assignedBuilding == null;
}