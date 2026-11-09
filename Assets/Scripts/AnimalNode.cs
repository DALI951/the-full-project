using UnityEngine;
using System.Collections.Generic;

public class AnimalNode : ResourceNode
{
    [Header("Wandering")]
    [SerializeField] private float wanderRadius   = 6f;
    [SerializeField] private float wanderInterval = 3f;
    [SerializeField] private float moveSpeed      = 1.5f;

    [Header("Animation")]
    public Animator animator;

    [Header("Death")]
    [SerializeField] private float dieRotateSpeed = 360f;

    private Vector3 wanderTarget;
    private float   wanderTimer;
    private bool    stopped;
    private bool    isDying;
    private bool    dieComplete;
    private string  walkParamName = "IsWalking";
    private bool    hasWalkAnim   = false;
    private Quaternion startRotation;
    private float    dieProgress;
    private static int s_nextHerdId = 0;
    private static readonly HashSet<string> s_warnedControllers = new HashSet<string>();

    [HideInInspector] public int herdId = -1;   // set by ResourceSpawner
    [SerializeField] private Vector3 spawnOrigin;
    private System.Random deterministicRng;

    public static int AllocateHerdId() => s_nextHerdId++;

    protected override void Awake()
    {
        base.Awake();
        spawnOrigin = transform.position;
        wanderTarget = transform.position;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Start()
    {
        if (spawnOrigin == Vector3.zero)
            spawnOrigin = transform.position;

        int seed = Mathf.RoundToInt(spawnOrigin.x * 100f) * 73856093 ^ Mathf.RoundToInt(spawnOrigin.z * 100f) * 19349663;
        deterministicRng = new System.Random(seed);
        wanderTimer = (float)(deterministicRng.NextDouble() * wanderInterval);
        wanderTarget = transform.position;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        hasWalkAnim = false;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            var pars = animator.parameters;
            for (int i = 0; i < pars.Length; i++)
            {
                if (pars[i].name == "IsWalking" && pars[i].type == AnimatorControllerParameterType.Bool)
                {
                    walkParamName = "IsWalking";
                    hasWalkAnim = true;
                    break;
                }
            }
            if (!hasWalkAnim)
            {
                for (int i = 0; i < pars.Length; i++)
                {
                    if (pars[i].name == "IsMoving" && pars[i].type == AnimatorControllerParameterType.Bool)
                    {
                        walkParamName = "IsMoving";
                        hasWalkAnim = true;
                        break;
                    }
                }
            }

            if (!hasWalkAnim)
            {
                string ctrl = animator.runtimeAnimatorController.name;
                if (s_warnedControllers.Add(ctrl))
                    Debug.LogWarning($"[AnimalNode] Animator '{ctrl}' has no bool 'IsWalking' or 'IsMoving' parameter " +
                                     "— walk animation disabled for this animal type.", this);
            }
        }
    }

    private void SetWalking(bool walking)
    {
        if (animator != null && hasWalkAnim)
            animator.SetBool(walkParamName, walking);
    }

    private void Update()
    {
        if (dieComplete) return;

        if (isDying)
        {
            dieProgress += dieRotateSpeed * Time.deltaTime;
            float t = Mathf.Clamp01(dieProgress / 90f);
            
            transform.rotation = Quaternion.Slerp(
                startRotation, 
                startRotation * Quaternion.Euler(0, 0, 90), 
                t
            );

            if (t >= 1f)
            {
                dieComplete = true;
                transform.rotation = startRotation * Quaternion.Euler(0, 0, 90);
            }
            return;
        }

        if (stopped || IsEmpty) return;

        // Wandering with deterministic RNG across server and clients
        wanderTimer -= Time.deltaTime;
        if (wanderTimer <= 0f)
        {
            wanderTimer = wanderInterval;
            PickNewTarget();
        }

        float distToTarget = Vector3.Distance(transform.position, wanderTarget);
        bool isMoving = distToTarget > 0.05f;

        SetWalking(isMoving);

        if (isMoving)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, wanderTarget, moveSpeed * Time.deltaTime);

            Vector3 direction = wanderTarget - transform.position;
            direction.y = 0;
            
            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }
    }

    private void PickNewTarget()
    {
        if (deterministicRng == null) return;
        float angle = (float)(deterministicRng.NextDouble() * System.Math.PI * 2.0);
        float radius = (float)(deterministicRng.NextDouble() * wanderRadius);
        Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        Vector3 candidate = spawnOrigin + offset;
        if (MapBoundary.Instance != null)
            candidate = MapBoundary.Instance.Clamp(candidate);
        wanderTarget = candidate;
    }

    public void StopMoving()
    {
        stopped = true;
        wanderTarget = transform.position;
        SetWalking(false);
    }

    protected override void OnKilled()
    {
        base.OnKilled();
        StopMoving();

        isDying = true;
        startRotation = transform.rotation;
        dieProgress = 0f;
    }
}
