using UnityEngine;
using Mirror;

public class Cavalry : Unit
{
    [Header("Cavalry Specific")]
    [SerializeField] private float speedMultiplier = 1.8f;

    [Header("Animation")]
    public Animator animator;
    [Tooltip("Length of attack clip")]
    public float attackAnimDuration = 1.2f;
    [Tooltip("Length of death clip")]
    public float deathAnimDuration = 1.5f;

    private Vector3 _lastPosition;
    private bool _wasMoving;
    private int _moveStoppedFrames;

    protected override void Awake()
    {
        unitName        = "Cavalry";
        unitType        = "Cavalry";
        unitDescription = "Fast mounted unit. Excels at flanking.";
        maxHealth       = 150;
        baseSpeed       = 3.5f;
        attackDamage    = 25;
        attackRange     = 2.0f;
        attackCooldown  = 1.3f;
        base.Awake();

        if (attackRange < 2.0f) attackRange = 2.0f;
        attackDamage = 25;
        attackCooldown = 1.3f;

        if (string.IsNullOrEmpty(unitType) || unitType == "Unknown" || unitType == "Cavarly")
            unitType = "Cavalry";
        if (string.IsNullOrEmpty(unitName) || unitName == "Unit" || unitName == "Cavarly")
            unitName = "Cavalry";

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        _lastPosition = transform.position;
    }

    protected override void Start()
    {
        base.Start();
        if (agent != null) agent.speed = baseSpeed * speedMultiplier;

        // Apply active civilization tech bonuses if spawned in an advanced age
        if (AgeManager.Instance != null && AgeManager.Instance.CurrentAge > appliedAge)
        {
            ApplyAgeBonuses(AgeManager.Instance.CurrentAge);
        }

        // Attach AoE4 mounted couch lance, knight shield & armored rider
        AttachMedievalGear();
    }

    // ── AoE4 Procedural Mounted Equipment & Knight Rider ──────────────

    public override void AttachMedievalGear()
    {
        Transform mg = transform.Find("MedievalGear");
        if (mg != null) Destroy(mg.gameObject);
    }

    // ── Age Progression & Veterancy Upgrades ──────────────────────────

    public override void ApplyAgeBonuses(int newAge)
    {
        if (newAge <= appliedAge) return;

        int oldHp    = AgeManager.GetMilitaryHpBonus(appliedAge);
        int newHp    = AgeManager.GetMilitaryHpBonus(newAge);
        int deltaHp  = newHp - oldHp;

        int oldDmg   = AgeManager.GetMilitaryDamageBonus(appliedAge);
        int newDmg   = AgeManager.GetMilitaryDamageBonus(newAge);
        int deltaDmg = newDmg - oldDmg;

        AddBonusHealth(deltaHp);
        AddBonusDamage(deltaDmg);
        appliedAge = newAge;

        // Historical AoE4 Unit Title & Veterancy Progression
        switch (newAge)
        {
            case 2:
                unitName        = "Cavalry Lancer";
                unitType        = "Cavalry Lancer";
                unitDescription = "Fast mounted lancer. Feudal Age upgraded (+4 DMG, +25 HP).";
                break;
            case 3:
                unitName        = "Heavy Knight";
                unitType        = "Heavy Knight";
                unitDescription = "Armored heavy knight. Castle Age upgraded (+10 DMG, +60 HP).";
                break;
            case 4:
                unitName        = "Imperial Paladin";
                unitType        = "Imperial Paladin";
                unitDescription = "Elite paladin of the realm. Imperial Age upgraded (+18 DMG, +110 HP).";
                break;
        }

        EffectManager.Instance?.PlayBuildingHitEffect(transform.position + Vector3.up * 1.3f);
    }

    protected override void Update()
    {
        base.Update();

        if (isDying || animator == null) return;

        Vector3 pos = transform.position;
        float dt = Mathf.Max(0.0001f, Time.deltaTime);
        float currentSpeed = Vector3.Distance(pos, _lastPosition) / dt;
        _lastPosition = pos;

        bool hasNavAgentMovement = (agent != null && agent.enabled && agent.hasPath && agent.velocity.sqrMagnitude > 0.04f);
        bool moving = hasNavAgentMovement || (currentSpeed > 0.15f);

        if (moving)
        {
            _moveStoppedFrames = 0;
            if (!_wasMoving)
            {
                _wasMoving = true;
                animator.SetBool("IsMoving", true);
            }
        }
        else
        {
            _moveStoppedFrames++;
            if (_moveStoppedFrames >= 3 && _wasMoving)
            {
                _wasMoving = false;
                animator.SetBool("IsMoving", false);
            }
        }
    }

    protected override void PerformAttack(Unit target)
    {
        base.PerformAttack(target);
        PlayAttackAnim();
        if (NetworkServer.active && NetworkClient.active) RpcPlayAttackAnim();
    }

    protected override void PerformBuildingAttack(Building target)
    {
        base.PerformBuildingAttack(target);
        PlayAttackAnim();
        if (NetworkServer.active && NetworkClient.active) RpcPlayAttackAnim();
    }

    [ClientRpc]
    private void RpcPlayAttackAnim()
    {
        if (!isClient) return;
        PlayAttackAnim();
    }

    private void PlayAttackAnim()
    {
        if (animator == null) return;
        animator.SetTrigger("Attack");
    }

    protected override void OnBeforeDestroy()
    {
        if (animator != null)
        {
            animator.SetBool("Die", true);
            animator.SetBool("IsMoving", false);
        }

        if (agent != null)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = false;
        }

        Transform ring = transform.Find("SelectionCircle");
        if (ring != null) ring.gameObject.SetActive(false);

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    [ClientRpc]
    private void RpcPlayDeathAnim()
    {
        if (!isClient) return;
        if (animator != null)
        {
            animator.SetBool("Die", true);
            animator.SetBool("IsMoving", false);
        }
    }

    protected override float GetDestroyDelay() => deathAnimDuration;

    public override void RestoreSpeed()
    {
        if (agent != null) agent.speed = baseSpeed * speedMultiplier;
    }
}
