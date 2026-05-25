using UnityEngine;
using Mirror;

public class Cavalry : Unit
{
    [Header("Cavalry Specific")]
    [SerializeField] private float speedMultiplier = 1.8f;

    [Header("Animation")]
    public Animator animator;
    [Tooltip("Length of attack clip")]
    public float attackAnimDuration = 1.5f;
    [Tooltip("Length of death clip")]
    public float deathAnimDuration = 1.5f;

    private Vector3 _lastPosition;
    private bool _wasMoving;

    protected override void Awake()
    {
        unitName        = "Cavalry";
        unitType        = "Cavalry";
        unitDescription = "Fast mounted unit. Excels at flanking.";
        maxHealth       = 150;
        baseSpeed       = 3.5f;
        attackDamage    = 25;
        attackRange     = 1.1f;
        attackCooldown  = 1.5f;
        base.Awake();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        _lastPosition = transform.position;
    }

    protected override void Start()
    {
        base.Start();
        if (agent != null) agent.speed = baseSpeed * speedMultiplier;
    }

    protected override void Update()
    {
        base.Update();

        if (isDying || animator == null) return;

        Vector3 pos = transform.position;
        bool moving = Vector3.SqrMagnitude(pos - _lastPosition) > 0.001f;
        _lastPosition = pos;

        if (moving != _wasMoving)
        {
            _wasMoving = moving;
            animator.SetBool("IsMoving", moving);
        }
    }

    protected override void PerformAttack(Unit target)
    {
        base.PerformAttack(target);
        PlayAttackAnim();
        if (isServer) RpcPlayAttackAnim();
    }

    protected override void PerformBuildingAttack(Building target)
    {
        base.PerformBuildingAttack(target);
        PlayAttackAnim();
        if (isServer) RpcPlayAttackAnim();
    }

    [ClientRpc]
    private void RpcPlayAttackAnim()
    {
        if (isServer) return;
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

        if (isServer) RpcPlayDeathAnim();

        if (agent != null)
        {
            agent.isStopped = true;
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
        if (isServer) return;
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
