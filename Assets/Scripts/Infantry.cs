using UnityEngine;
using Mirror;

public class Infantry : Unit
{
    [Header("Animation")]
    public Animator animator;
    [Tooltip("Must match your sword clip length exactly")]
    public float attackAnimDuration = 1.2f;
    [Tooltip("Must be >= attackAnimDuration or the animation will overlap and freeze")]
    public float attackCooldownOverride = 1.4f;
    [Tooltip("Length of your death animation clip")]
    public float deathAnimDuration = 2.0f;

    private Vector3 _lastPosition;
    private bool _wasMoving;
    private int _moveStoppedFrames;

    protected override void Awake()
    {
        unitName        = "Infantry";
        unitType        = "Infantry";
        unitDescription = "Frontline soldier. Strong and reliable.";
        maxHealth       = 120;
        baseSpeed       = 3.5f;
        attackDamage    = 20;
        attackRange     = 1.8f;
        attackCooldown  = 1.4f;
        attackAnimDuration = 1.2f;

        base.Awake();

        if (attackRange < 1.8f) attackRange = 1.8f;
        attackDamage = 20;
        attackCooldown = 1.4f;
        attackAnimDuration = 1.2f;

        if (string.IsNullOrEmpty(unitType) || unitType == "Unknown")
            unitType = "Infantry";
        if (string.IsNullOrEmpty(unitName) || unitName == "Unit")
            unitName = "Infantry";

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        _lastPosition = transform.position;
    }

    protected override void Start()
    {
        base.Start();

        // Ensure active AgeManager tech bonuses are applied upon spawning
        if (AgeManager.Instance != null && AgeManager.Instance.CurrentAge > appliedAge)
        {
            ApplyAgeBonuses(AgeManager.Instance.CurrentAge);
        }

        // Attach AoE4 steel broadsword & team heater shield
        AttachMedievalGear();
    }

    // ── AoE4 Procedural Weapon & Shield Equipment ─────────────────────

    public override void AttachMedievalGear()
    {
        Transform mg = transform.Find("MedievalGear");
        if (mg != null) Destroy(mg.gameObject);

        // Ensure the authored FBX sword is active and visible
        Transform swordT = transform.Find("sword");
        if (swordT == null) swordT = transform.Find("infantryBreathing Idle/sword");
        if (swordT != null)
        {
            swordT.gameObject.SetActive(true);
            foreach (var r in swordT.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true;
            }
        }
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
                unitName        = "Veteran Infantry";
                unitType        = "Veteran Infantry";
                unitDescription = "Hardened swordsman. Feudal Age upgraded (+4 DMG, +25 HP).";
                break;
            case 3:
                unitName        = "Castle Guard";
                unitType        = "Castle Guard";
                unitDescription = "Elite man-at-arms. Castle Age upgraded (+10 DMG, +60 HP).";
                break;
            case 4:
                unitName        = "Imperial Champion";
                unitType        = "Imperial Champion";
                unitDescription = "Legendary champion of the realm. Imperial Age upgraded (+18 DMG, +110 HP).";
                break;
        }

        EffectManager.Instance?.PlayBuildingHitEffect(transform.position + Vector3.up * 0.9f);
    }

    protected override void Update()
    {
        base.Update();

        if (isDying || animator == null) return;

        Vector3 pos = transform.position;
        bool moving = Vector3.SqrMagnitude(pos - _lastPosition) > 0.001f;
        _lastPosition = pos;

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
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("swaord slash"))
            return;
        animator.SetBool("Attack", true);
        CancelInvoke(nameof(ResetAttack));
        Invoke(nameof(ResetAttack), attackAnimDuration);
    }

    private void ResetAttack()
    {
        if (animator != null)
            animator.SetBool("Attack", false);
    }

    protected override void OnBeforeDestroy()
    {
        if (animator != null)
        {
            animator.SetBool("Die", true);
            animator.SetBool("IsMoving", false);
            animator.SetBool("Attack", false);
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
            animator.SetBool("Attack", false);
        }
    }

    protected override float GetDestroyDelay() => deathAnimDuration;
}
