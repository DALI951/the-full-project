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
    public Transform hammer;

    [Header("Gathering")]
    [SerializeField] private float gatherRange    = 2.0f;
    [SerializeField] private float gatherInterval = 2f;
    [SerializeField] private int   gatherAmount   = 10;
    [SerializeField] private float orbitRadius    = 2.5f;
    [SerializeField] private int   maxCarried     = 10;

    private enum VState
    {
        Idle,
        MovingToCommand,
        MovingToResource,
        AttackingAnimal,
        Gathering,
        ReturningToBase,
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
    private Building     currentDropOff = null;
    private int          carriedAmount  = 0;
    private ResourceType carriedType    = ResourceType.Wood;

    private Vector3   _lastVillagerPos;
    private bool      _wasVillagerMoving;
    private int       _villagerMoveStoppedFrames;

    protected override void Awake()
    {
        if (string.IsNullOrEmpty(unitType) || unitType == "Unknown")
            unitType = "Villager";
        if (string.IsNullOrEmpty(unitName) || unitName == "Unit")
            unitName = "Villager";

        unitDescription = "Gathers resources. Right-click a tree, mine or animal.";
        maxHealth       = 60;
        baseSpeed       = 3f;
        attackDamage    = 15;
        attackRange     = 2f;
        attackCooldown  = 1f;
        base.Awake();

        if (gatherRange < 2.0f) gatherRange = 2.0f;
        if (attackRange < 1.8f) attackRange = 1.8f;

        if (string.IsNullOrEmpty(unitType) || unitType == "Unknown")
            unitType = "Villager";
        if (string.IsNullOrEmpty(unitName) || unitName == "Unit")
            unitName = "Villager";

        // Find the active rigged Animator (checking root or children)
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            var anims = GetComponentsInChildren<Animator>(true);
            foreach (var a in anims)
            {
                if (a != null && a.runtimeAnimatorController != null)
                {
                    animator = a;
                    break;
                }
            }
            if (animator == null && anims.Length > 0) animator = anims[0];
            if (animator == null) animator = GetComponent<Animator>();
        }

        if (agent != null)
        {
            agent.avoidancePriority = Random.Range(1, 100);
            agent.radius = 0.4f;
        }

        _lastVillagerPos = transform.position;
    }

    protected override void Start()
    {
        base.Start();

        // Apply active civilization economy tech bonuses if spawned in an advanced age
        if (AgeManager.Instance != null && AgeManager.Instance.CurrentAge > appliedAge)
        {
            ApplyAgeBonuses(AgeManager.Instance.CurrentAge);
        }

        // Attach compact procedural construction hammer and ensure gathering tools exist
        AttachMedievalGear();
    }

    public override void AttachMedievalGear()
    {
        Transform mg = transform.Find("MedievalGear");
        if (mg != null) Destroy(mg.gameObject);

        // Auto-discover tools in child hierarchy if not hooked in Inspector
        if (axe == null) axe = FindChildRecursive(transform, "Axe") ?? FindChildRecursive(transform, "axe");
        if (knife == null) knife = FindChildRecursive(transform, "Knife") ?? FindChildRecursive(transform, "knife");
        if (shovel == null) shovel = FindChildRecursive(transform, "Shovel") ?? FindChildRecursive(transform, "shovel") ?? FindChildRecursive(transform, "Pickaxe");
        if (hammer == null) hammer = FindChildRecursive(transform, "Hammer") ?? FindChildRecursive(transform, "hammer") ?? axe;

        HideTools();
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return child;
            Transform found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    public override void ApplyAgeBonuses(int newAge)
    {
        if (newAge <= appliedAge) return;

        int oldGather = AgeManager.GetGatherBonus(appliedAge);
        int newGather = AgeManager.GetGatherBonus(newAge);
        int bonus = (newGather - oldGather);
        gatherAmount += bonus;
        maxCarried += bonus * 2;

        float mult = AgeManager.GetVillagerSpeedMultiplier(newAge);
        baseSpeed = 3.0f * mult;
        RestoreSpeed();

        appliedAge = newAge;
        unitDescription = $"Gathers resources (+{AgeManager.GetGatherBonus(newAge)} Harvest). Speed x{mult:F2}.";
    }

    protected override void Update()
    {
        base.Update();

        if (targetNode == null)
        {
            if (state == VState.MovingToResource || state == VState.Gathering || state == VState.AttackingAnimal)
            {
                targetNode = null;
                gatherSlot = -1;
                if (carriedAmount > 0)
                    StartReturningToBase();
                else
                    EnterState(VState.Idle);
            }
        }
        if (targetSite == null)
        {
            if (state == VState.MovingToBuild || state == VState.Building)
            {
                targetSite = null;
                EnterState(VState.Idle);
            }
        }

        // ── Animation handling ─────────────────────────────────────────
        if (!isDying && animator != null)
        {
            Vector3 pos = transform.position;
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float currentSpeed = Vector3.Distance(pos, _lastVillagerPos) / dt;
            _lastVillagerPos = pos;

            bool hasNavAgentMovement = (agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath && agent.velocity.sqrMagnitude > 0.04f);
            bool moving = hasNavAgentMovement || (currentSpeed > 0.15f);

            bool isBusy = (state == VState.Gathering || state == VState.Building || state == VState.WorkingInBuilding || state == VState.AttackingAnimal);
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
            case VState.AttackingAnimal:  TickAttackingAnimal(); break;
            case VState.Gathering:        TickGathering(); break;
            case VState.ReturningToBase:  TickReturningToBase(); break;
            case VState.MovingToBuild:    TickMovingToBuild(); break;
            case VState.Building:         TickBuilding(); break;
            case VState.MovingToResourceBuilding: TickMovingToResourceBuilding(); break;
            case VState.WorkingInBuilding: break;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────

    public virtual void GatherFrom(ResourceNode node)
    {
        if (node == null || node.IsEmpty) return;

        CancelGatheringAndBuilding();

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
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.stoppingDistance = attackRange * 0.8f;
                agent.SetDestination(node.transform.position);
            }
            EnterState(VState.MovingToResource);
        }
        else
            MoveToGatherSlot();
    }

    public override void MoveTo(Vector3 dest)
    {
        CancelGatheringAndBuilding();
        base.MoveTo(dest);
        EnterState(VState.MovingToCommand);
    }

    public override void SetFirstWaypoint(Vector3 point)
    {
        CancelGatheringAndBuilding();
        base.SetFirstWaypoint(point);
        EnterState(VState.MovingToCommand);
    }

    public override void AddWaypoint(Vector3 point)
    {
        CancelGatheringAndBuilding();
        base.AddWaypoint(point);
        EnterState(VState.MovingToCommand);
    }

    public void CancelGatheringAndBuilding()
    {
        ReleaseCurrentNode();
        targetSite = null;
        currentDropOff = null;
        if (assignedBuilding != null)
        {
            assignedBuilding.OnVillagerLeft(this);
            assignedBuilding = null;
            var rends = GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends) if (r) r.enabled = true;
        }
        HideTools();
    }

    public void EnterIdle()
    {
        CancelGatheringAndBuilding();
        EnterState(VState.Idle);
    }

    public void OnSlotPromoted(ResourceNode node, int slot)
    {
        if (targetNode == node && gatherSlot < 0)
        {
            gatherSlot = slot;
            if (!node.RequiresKill || node.IsKilled)
                MoveToGatherSlot();
        }
    }

    // ── State Ticks ────────────────────────────────────────────────────

    private void TickMovingToResource()
    {
        if (targetNode == null || targetNode.IsEmpty)
        {
            RestoreSpeed();
            if (carriedAmount > 0)
            {
                StartReturningToBase();
            }
            else
            {
                EnterState(VState.Idle);
                AutoFindNext();
            }
            return;
        }

        // Hunting logic
        if (targetNode.RequiresKill && !targetNode.IsKilled)
        {
            if (agent != null) agent.speed = baseSpeed * 1.25f;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.stoppingDistance = attackRange * 0.8f;
                agent.SetDestination(targetNode.transform.position);
            }
        }
        else
        {
            RestoreSpeed();
        }

        if (gatherSlot < 0)
        {
            float dist = Dist(targetNode);
            if (dist <= gatherRange + orbitRadius)
            {
                if (agent != null && agent.isOnNavMesh) agent.ResetPath();
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
            if (d <= gatherRange + 0.5f || (gatherSlot >= 0 && AgentStopped() && d <= gatherRange + orbitRadius))
                EnterState(VState.Gathering);
        }
    }

    private void TickAttackingAnimal()
    {
        if (targetNode == null || targetNode.IsEmpty || targetNode.IsKilled)
        {
            RestoreSpeed();
            if (targetNode != null && !targetNode.IsEmpty)
            { 
                ShowKnife(false);
                MoveToGatherSlot(); 
                return; 
            }
            HideTools();
            if (carriedAmount > 0)
                StartReturningToBase();
            else
                AutoFindNext();
            return;
        }

        float dist = Dist(targetNode);
        if (dist > attackRange)
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.speed = baseSpeed * 1.25f;
                agent.stoppingDistance = 0.1f;
                agent.SetDestination(targetNode.transform.position);
            }
            return;
        }

        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        Vector3 flatTarget = targetNode.transform.position;
        flatTarget.y = transform.position.y;
        transform.LookAt(flatTarget);

        if (Time.time - lastActionTime >= attackCooldown)
        {
            lastActionTime = Time.time;

            PlayThrustAnim();
            if (NetworkServer.active && NetworkClient.active) RpcPlayThrustAnim();

            targetNode.TakeDamage(attackDamage);

            if (targetNode is AnimalNode a)
                a.StopMoving();

            if (targetNode.IsKilled)
            {
                RestoreSpeed();
                ShowKnife(false);
                
                if (targetNode != null)
                {
                    targetNode.ReleaseSlot(this);
                    int newSlot = targetNode.ReserveSlot(this);
                    if (newSlot >= 0) gatherSlot = newSlot;
                }
                MoveToGatherSlot();
            }
        }
    }

    private void TickGathering()
    {
        if (targetNode == null || targetNode.IsEmpty)
        {
            if (carriedAmount > 0)
            {
                StartReturningToBase();
            }
            else
            {
                AutoFindNext();
            }
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
        if (dist > gatherRange + orbitRadius + 0.5f)
        {
            MoveToGatherSlot();
            return;
        }

        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        Vector3 flatTarget = targetNode.transform.position;
        flatTarget.y = transform.position.y;
        transform.LookAt(flatTarget);

        if (Time.time - lastActionTime >= gatherInterval)
        {
            lastActionTime = Time.time;
            int gathered = targetNode.Gather(gatherAmount);

            if (gathered > 0)
            {
                carriedAmount += gathered;
                carriedType = targetNode.ResourceType;

                EffectManager.Instance?.PlayResourceGatherEffect(targetNode.transform.position + Vector3.up * 0.5f);
                switch (targetNode.ResourceType)
                {
                    case ResourceType.Wood: AudioManager.Instance?.PlayChop(); break;
                    case ResourceType.Gold: AudioManager.Instance?.PlayMine(); break;
                    case ResourceType.Food: AudioManager.Instance?.PlayGather(); break;
                }

                if (targetNode.IsEmpty && targetNode.ResourceType == ResourceType.Wood)
                {
                    EffectManager.Instance?.PlayTreeFallEffect(targetNode.transform.position);
                }
            }

            // If carried capacity reached or resource depleted, return to base!
            if (carriedAmount >= maxCarried || targetNode.IsEmpty)
            {
                StartReturningToBase();
            }
        }
    }

    // ── Drop-Off / Returning To Base ────────────────────────────────────

    private void StartReturningToBase()
    {
        Building dropOff = FindNearestDropOff(carriedType);
        if (dropOff != null)
        {
            currentDropOff = dropOff;
            if (agent != null && agent.isOnNavMesh)
            {
                agent.stoppingDistance = 0.5f;
                agent.SetDestination(dropOff.transform.position);
            }
            EnterState(VState.ReturningToBase);
        }
        else
        {
            // If no drop-off building exists (e.g. at start of match before TC or all destroyed), deposit immediately
            DepositResources();
        }
    }

    private void TickReturningToBase()
    {
        if (carriedAmount <= 0)
        {
            if (targetNode != null && !targetNode.IsEmpty)
                MoveToGatherSlot();
            else
                AutoFindNext();
            return;
        }

        if (currentDropOff == null || !currentDropOff.gameObject.activeInHierarchy)
        {
            currentDropOff = FindNearestDropOff(carriedType);
            if (currentDropOff == null)
            {
                DepositResources();
                return;
            }
            if (agent != null && agent.isOnNavMesh)
            {
                agent.stoppingDistance = 0.5f;
                agent.SetDestination(currentDropOff.transform.position);
            }
        }

        float surfaceDist = GetSurfaceDistance(currentDropOff.transform);
        bool reachedByDistance = surfaceDist <= 2.2f;
        bool reachedByAgent = agent != null && agent.isOnNavMesh && !agent.pathPending 
                              && agent.remainingDistance <= 2.5f && surfaceDist <= 3.0f;

        if (reachedByDistance || reachedByAgent)
        {
            DepositResources();
        }
    }

    private void DepositResources()
    {
        if (carriedAmount > 0)
        {
            string typeName = carriedType.ToString();
            Vector3 spawnPos = transform.position + Vector3.up * 1.5f;
            FloatingDamageText.SpawnResource(spawnPos, typeName, carriedAmount);

            NetworkedPlayer owner = NetworkedPlayer.Get(OwnerPlayerId);
            bool isLocalPlayer = OwnerPlayerId == PlayerColorManager.LocalPlayerIndex;

            if (owner != null)
            {
                switch (carriedType)
                {
                    case ResourceType.Wood: owner.AddResources(0, carriedAmount, 0); break;
                    case ResourceType.Gold: owner.AddResources(0, 0, carriedAmount); break;
                    case ResourceType.Food: owner.AddResources(carriedAmount, 0, 0); break;
                }
            }
            else if (isLocalPlayer)
            {
                switch (carriedType)
                {
                    case ResourceType.Wood: ResourceManager.Instance?.AddResources(0, carriedAmount, 0); break;
                    case ResourceType.Gold: ResourceManager.Instance?.AddResources(0, 0, carriedAmount); break;
                    case ResourceType.Food: ResourceManager.Instance?.AddResources(carriedAmount, 0, 0); break;
                }
            }
            else
            {
                EnemyAI.Instance?.AddEnemyResources(
                    carriedType == ResourceType.Food ? carriedAmount : 0,
                    carriedType == ResourceType.Wood ? carriedAmount : 0,
                    carriedType == ResourceType.Gold ? carriedAmount : 0);
            }

            carriedAmount = 0;
            AudioManager.Instance?.PlayBuildPlace();
        }

        currentDropOff = null;

        if (targetNode != null && !targetNode.IsEmpty)
        {
            if (targetNode.RequiresKill && !targetNode.IsKilled)
            {
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.stoppingDistance = 0.5f;
                    agent.SetDestination(targetNode.transform.position);
                }
                EnterState(VState.MovingToResource);
            }
            else
            {
                MoveToGatherSlot();
            }
        }
        else
        {
            AutoFindNext();
        }
    }

    public Building FindNearestDropOff(ResourceType type)
    {
        Building best = null;
        float bestDist = float.MaxValue;

        foreach (Building b in Building.AllBuildings)
        {
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (b.OwnerPlayerId != OwnerPlayerId) continue;

            bool canAccept = false;
            if (b.IsHomeSiteBuilding) canAccept = true;
            else if (type == ResourceType.Wood && b.IsLumberMillBuilding) canAccept = true;
            else if (type == ResourceType.Food && b.IsFarmBuilding) canAccept = true;

            if (!canAccept) continue;

            float d = GetSurfaceDistance(b.transform);
            if (d < bestDist)
            {
                bestDist = d;
                best = b;
            }
        }

        if (best == null)
        {
            foreach (Building b in Building.AllBuildings)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                if (b.OwnerPlayerId != OwnerPlayerId) continue;
                float d = GetSurfaceDistance(b.transform);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = b;
                }
            }
        }

        return best;
    }

    // ── Enemy unit combat (Punching) ───────────────────────────────────
    protected override void PerformAttack(Unit target)
    {
        base.PerformAttack(target);
        PlayPunchAnim();
        if (NetworkServer.active && NetworkClient.active) RpcPlayPunchAnim();
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
        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        if (stateInfo.IsName("Punching") || stateInfo.IsName("Punshing"))
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

        if (agent != null && agent.isOnNavMesh)
        {
            agent.stoppingDistance = 0.2f;
            agent.SetDestination(dest);
        }
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
        node == null ? float.MaxValue : GetSurfaceDistance(node.transform);

    private bool AgentStopped() =>
        agent != null && agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;

    private void EnterState(VState next)
    {
        if (next != VState.AttackingAnimal && (targetNode == null || !targetNode.RequiresKill || targetNode.IsKilled))
        {
            RestoreSpeed();
        }

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
            case VState.ReturningToBase:
                HideTools();
                break;
            case VState.MovingToBuild:
                HideTools();
                break;
            case VState.Building:
                if (agent != null && agent.isOnNavMesh) agent.ResetPath();
                ShowHammer(true);
                break;
            case VState.MovingToCommand:
                HideTools();
                break;
        }

        UpdateAnimState();
    }

    private void UpdateAnimState()
    {
        int newState = 0;
        if (state == VState.Idle || state == VState.MovingToCommand || state == VState.MovingToResource
            || state == VState.ReturningToBase || state == VState.MovingToBuild 
            || state == VState.MovingToResourceBuilding)
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
        {
            animState = newState;
        }
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
            case 5:
                ShowHammer(true);
                break;
        }
    }

    // ── Building ───────────────────────────────────────────────────────

    public void BuildAt(ConstructionSite site)
    {
        CancelGatheringAndBuilding();
        targetSite = site;
        site.AssignBuilder(this);
        if (agent != null && agent.isOnNavMesh)
        {
            agent.stoppingDistance = 0.5f;
            agent.SetDestination(site.transform.position);
        }
        EnterState(VState.MovingToBuild);
    }

    private void TickMovingToBuild()
    {
        if (targetSite == null || targetSite.gameObject == null)
        { targetSite = null; EnterState(VState.Idle); return; }
        float dist = GetSurfaceDistance(targetSite.transform);
        if (dist <= gatherRange + 0.5f || dist <= 2.5f)
            EnterState(VState.Building);
    }

    private void TickBuilding()
    {
        if (targetSite == null || targetSite.gameObject == null || targetSite.IsComplete)
        { targetSite = null; EnterState(VState.Idle); return; }
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
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
        if (b == null)
        {
            Debug.LogWarning("[Villager] AssignToResourceBuilding called with a missing building.");
            EnterState(VState.Idle);
            return;
        }

        CancelGatheringAndBuilding();
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

        if (agent != null)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = false;
        }

        HideTools();

        Transform ring = transform.Find("SelectionCircle");
        if (ring != null) ring.gameObject.SetActive(false);

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    [ClientRpc]
    private void RpcPlayDeathAnim()
    {
    }

    protected override float GetDestroyDelay() => deathAnimDuration;

    protected override void Die()
    {
        ReleaseCurrentNode();
        targetSite = null;
        currentDropOff = null;
        if (assignedBuilding != null) { assignedBuilding.OnVillagerLeft(this); assignedBuilding = null; }
        base.Die();
    }

    private void ShowAxe(bool show)
    {
        if (axe != null) axe.gameObject.SetActive(show);
        if (knife != null && knife != axe) knife.gameObject.SetActive(false);
        if (shovel != null && shovel != axe) shovel.gameObject.SetActive(false);
        if (hammer != null && hammer != axe) hammer.gameObject.SetActive(false);
    }

    private void ShowKnife(bool show)
    {
        if (knife != null) knife.gameObject.SetActive(show);
        if (axe != null && axe != knife) axe.gameObject.SetActive(false);
        if (shovel != null && shovel != knife) shovel.gameObject.SetActive(false);
        if (hammer != null && hammer != knife) hammer.gameObject.SetActive(false);
    }

    private void ShowShovel(bool show)
    {
        if (shovel != null) shovel.gameObject.SetActive(show);
        if (axe != null && axe != shovel) axe.gameObject.SetActive(false);
        if (knife != null && knife != shovel) knife.gameObject.SetActive(false);
        if (hammer != null && hammer != shovel) hammer.gameObject.SetActive(false);
    }

    private void ShowHammer(bool show)
    {
        if (hammer != null) hammer.gameObject.SetActive(show);
        if (axe != null && axe != hammer) axe.gameObject.SetActive(false);
        if (knife != null && knife != hammer) knife.gameObject.SetActive(false);
        if (shovel != null && shovel != hammer) shovel.gameObject.SetActive(false);
    }

    private void HideTools()
    {
        if (axe != null) axe.gameObject.SetActive(false);
        if (knife != null) knife.gameObject.SetActive(false);
        if (shovel != null) shovel.gameObject.SetActive(false);
        if (hammer != null) hammer.gameObject.SetActive(false);
    }

    public bool IsGathering => state == VState.Gathering
                            || state == VState.MovingToResource
                            || state == VState.AttackingAnimal
                            || state == VState.ReturningToBase
                            || state == VState.MovingToResourceBuilding
                            || state == VState.WorkingInBuilding;

    public bool IsIdle => state == VState.Idle && assignedBuilding == null && carriedAmount == 0;
}
