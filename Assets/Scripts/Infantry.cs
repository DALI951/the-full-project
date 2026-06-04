using UnityEngine;
using Mirror;

public class Infantry : Unit
{
    [Header("Animation")]
    public Animator animator;
    [Tooltip("Must match your sword clip length exactly")]
    public float attackAnimDuration = 2.05f;
    [Tooltip("Must be >= attackAnimDuration or the animation will overlap and freeze")]
    public float attackCooldownOverride = 2.1f;
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
        attackRange     = 1f;
        attackCooldown  = attackCooldownOverride; // 2.1s to match 2.05s animation

        base.Awake();

        if (animator == null)
            animator = GetComponent<Animator>();

        _lastPosition = transform.position;
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