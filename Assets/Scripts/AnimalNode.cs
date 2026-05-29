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
    private float   lastWanderTime;
    private bool    stopped;
    private bool    isDying;
    private bool    dieComplete;
    private Quaternion startRotation;
    private float    dieProgress;
    private static int s_nextHerdId = 0;
    private static readonly Dictionary<int, Vector3> s_herdTargets  = new Dictionary<int, Vector3>();
    private static readonly Dictionary<int, float>   s_herdNextMove = new Dictionary<int, float>();
    private static readonly Dictionary<int, int>     s_herdCount    = new Dictionary<int, int>();

    [HideInInspector] public int herdId = -1;   // set by ResourceSpawner
    private Vector3 personalOffset;             // keeps animals from stacking

    public static int AllocateHerdId() => s_nextHerdId++;

    private void Start()
    {
        if (herdId < 0) return;
        if (!s_herdTargets.ContainsKey(herdId))
            s_herdTargets[herdId]  = transform.position;
        if (!s_herdNextMove.ContainsKey(herdId))
            s_herdNextMove[herdId] = Time.time + wanderInterval;
        if (!s_herdCount.ContainsKey(herdId))
            s_herdCount[herdId]    = 0;
        s_herdCount[herdId]++;
    }
    protected override void Awake()
    {
        base.Awake();
        // Fixed personal offset so animals in same herd don't stack
        float pa = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float pr = Random.Range(0.5f, 2f);
        personalOffset = new Vector3(Mathf.Cos(pa) * pr, 0f, Mathf.Sin(pa) * pr);
        wanderTarget = transform.position;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        if (animator != null && animator.GetBool("Die"))
        {
            Debug.LogError("Die was set! Stack trace:", this);
            animator.SetBool("Die", false); // Force reset to see when it happens again
        }
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

        // Wandering
        if (herdId >= 0 && s_herdTargets.ContainsKey(herdId))
        {
            // First animal to reach the timer updates the shared herd target
            if (Time.time >= s_herdNextMove[herdId])
            {
                s_herdNextMove[herdId] = Time.time + wanderInterval
                                    + Random.Range(0f, wanderInterval * 0.3f);
                Vector2 rand = Random.insideUnitCircle * wanderRadius;
                Vector3 candidate = s_herdTargets[herdId] + new Vector3(rand.x, 0f, rand.y);
                if (MapBoundary.Instance != null) candidate = MapBoundary.Instance.Clamp(candidate);
                s_herdTargets[herdId] = candidate;
            }
            wanderTarget = s_herdTargets[herdId] + personalOffset;
            if (MapBoundary.Instance != null) wanderTarget = MapBoundary.Instance.Clamp(wanderTarget);
        }
        else
        {
            // Solo wandering — original behaviour preserved
            if (Time.time - lastWanderTime > wanderInterval)
            {
                lastWanderTime = Time.time;
                Vector2 rand = Random.insideUnitCircle * wanderRadius;
                Vector3 candidate = transform.position + new Vector3(rand.x, 0f, rand.y);
                if (MapBoundary.Instance != null) candidate = MapBoundary.Instance.Clamp(candidate);
                wanderTarget = candidate;
            }
        }

        float distToTarget = Vector3.Distance(transform.position, wanderTarget);
        bool isMoving = distToTarget > 0.05f;

        if (animator != null)
            animator.SetBool("IsMoving", isMoving);

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

    public void StopMoving()
    {
        stopped = true;
        wanderTarget = transform.position;
        if (animator != null)
            animator.SetBool("IsMoving", false);
    }

    protected override void OnKilled()
    {
        base.OnKilled();
        StopMoving();

        // Reset visual rotation before death so body falls correctly

        isDying = true;
        startRotation = transform.rotation;
        dieProgress = 0f;
    }
    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (herdId >= 0 && s_herdCount.ContainsKey(herdId))
        {
            s_herdCount[herdId]--;
            if (s_herdCount[herdId] <= 0)
            {
                s_herdTargets.Remove(herdId);
                s_herdNextMove.Remove(herdId);
                s_herdCount.Remove(herdId);
            }
        }
    }
}