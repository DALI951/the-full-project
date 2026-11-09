using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Procedural secondary motion animator for RTS units and wildlife (Age of Empires IV feel).
/// Adds responsive stride bounce, forward lean, side-to-side sway, horse gallop/canter,
/// combat strike lunges, villager gathering chop arcs, and wildlife grazing cycles.
/// Modifies the visual model child in LateUpdate() without touching the root Transform or NavMeshAgent.
/// </summary>
[DisallowMultipleComponent]
public class ProceduralUnitAnimator : MonoBehaviour
{
    [Header("Target Visual")]
    [SerializeField] private Transform visualTransform;
    [SerializeField] private Vector3 initialLocalPos;
    [SerializeField] private Quaternion initialLocalRot;

    [Header("Locomotion Tuning")]
    [Tooltip("Bounce amplitude along local Y axis during locomotion")]
    [SerializeField] private float bounceAmp = 0.035f;
    [Tooltip("Forward pitch lean in degrees during locomotion (4-7 deg)")]
    [SerializeField] private float forwardLeanAngle = 5.5f;
    [Tooltip("Side-to-side roll sway in degrees during locomotion (~2.5 deg)")]
    [SerializeField] private float swayRollAngle = 2.5f;
    [Tooltip("Locomotion stride frequency (rad/s)")]
    [SerializeField] private float strideFrequency = 10.5f;

    [Header("Cavalry Tuning")]
    [Tooltip("Gallop pitch wave amplitude in degrees for cavalry (5 deg)")]
    [SerializeField] private float cavalryCanterPitch = 5.0f;
    [Tooltip("Gallop pitch wave frequency (rad/s) for cavalry (11 rad/s)")]
    [SerializeField] private float cavalryCanterFreq = 11.0f;

    [Header("Combat & Gathering Tuning")]
    [Tooltip("Strike / chop pitch arc in degrees")]
    [SerializeField] private float strikePitchAngle = 13.0f;
    [Tooltip("Strike / chop forward translation offset")]
    [SerializeField] private float strikeForwardOffset = 0.05f;

    [Header("Wildlife Grazing Tuning")]
    [Tooltip("Grazing head dip angle in degrees for wildlife")]
    [SerializeField] private float grazingDipAngle = 15.0f;
    [Tooltip("Bounding stride vertical leap for wildlife")]
    [SerializeField] private float wildlifeBoundHeight = 0.08f;

    // Cached component references
    private Unit _unit;
    private NavMeshAgent _agent;
    private Villager _villager;
    private Cavalry _cavalry;
    private AnimalNode _animalNode;
    private AnimalBehavior _animalBehavior;
    private Animator _animator;

    private bool _isCavalry;
    private bool _isVillager;
    private bool _isAnimal;

    // Runtime state tracking
    private Vector3 _lastRootPos;
    private float _locomotionCycle;
    private float _strikeCycle;
    private float _grazingPhase;
    private Vector3 _currentProceduralPosOffset;
    private Vector3 _currentProceduralRotEuler;

    private readonly HashSet<string> _animatorParams = new HashSet<string>();

    private bool _initialized = false;

    private void Awake()
    {
        InitializeComponents();
    }

    private void Start()
    {
        if (!_initialized)
            InitializeComponents();
    }

    private void InitializeComponents()
    {
        if (_initialized) return;

        _unit = GetComponent<Unit>();
        _agent = GetComponent<NavMeshAgent>();
        _villager = GetComponent<Villager>();
        _cavalry = GetComponent<Cavalry>();
        _animalNode = GetComponent<AnimalNode>();
        _animalBehavior = GetComponent<AnimalBehavior>();
        _animator = GetComponentInChildren<Animator>();

        string objName = gameObject.name;
        _isCavalry = (_cavalry != null) ||
                     (_unit != null && _unit.GetType().Name.IndexOf("Cavalry", StringComparison.OrdinalIgnoreCase) >= 0) ||
                     objName.IndexOf("Cavarly", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     objName.IndexOf("Cavalry", StringComparison.OrdinalIgnoreCase) >= 0;

        _isVillager = (_villager != null) ||
                      (_unit != null && _unit.GetType().Name.IndexOf("Villager", StringComparison.OrdinalIgnoreCase) >= 0) ||
                      (_unit != null && _unit.GetType().Name.IndexOf("Builder", StringComparison.OrdinalIgnoreCase) >= 0) ||
                      objName.IndexOf("Villager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      objName.IndexOf("Builder", StringComparison.OrdinalIgnoreCase) >= 0;

        _isAnimal = (_animalNode != null) ||
                    (_animalBehavior != null) ||
                    objName.IndexOf("Animal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objName.IndexOf("Deer", StringComparison.OrdinalIgnoreCase) >= 0;

        if (visualTransform == null)
            visualTransform = FindVisualChild(transform);

        if (visualTransform != null && visualTransform != transform)
        {
            initialLocalPos = visualTransform.localPosition;
            initialLocalRot = visualTransform.localRotation;
        }

        _lastRootPos = transform.position;

        // Custom tuning based on entity type
        if (_isCavalry)
        {
            bounceAmp = 0.045f;
            forwardLeanAngle = 5.5f;
            strideFrequency = cavalryCanterFreq; // 11 rad/s
        }
        else if (_isAnimal)
        {
            bounceAmp = wildlifeBoundHeight;
            forwardLeanAngle = 4.5f;
            strideFrequency = 9.0f;
        }
        else
        {
            bounceAmp = 0.035f;
            forwardLeanAngle = 5.8f;
            strideFrequency = 10.5f;
        }

        CacheAnimatorParams();
        _initialized = true;
    }

    private void CacheAnimatorParams()
    {
        _animatorParams.Clear();
        if (_animator != null && _animator.runtimeAnimatorController != null)
        {
            var pars = _animator.parameters;
            for (int i = 0; i < pars.Length; i++)
            {
                _animatorParams.Add(pars[i].name);
            }
        }
    }

    private bool GetAnimBoolSafe(string paramName)
    {
        if (_animator != null && _animatorParams.Contains(paramName))
        {
            return _animator.GetBool(paramName);
        }
        return false;
    }

    /// <summary>
    /// Finds the visual model child of a unit or entity.
    /// Excludes selection circles, canvas, spawn points, etc. Never returns the root transform.
    /// </summary>
    public static Transform FindVisualChild(Transform root)
    {
        if (root == null) return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            string cName = child.name;

            if (cName.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase) ||
                cName.Equals("SpawnPoint", StringComparison.OrdinalIgnoreCase) ||
                cName.Equals("Pole", StringComparison.OrdinalIgnoreCase) ||
                cName.Equals("FlagHead", StringComparison.OrdinalIgnoreCase) ||
                cName.IndexOf("Canvas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                child.GetComponent<Canvas>() != null)
            {
                continue;
            }

            if (child.GetComponentsInChildren<Renderer>(true).Length > 0)
            {
                return child;
            }
        }

        // Fallback to first non-UI child
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (!child.name.Equals("SelectionCircle", StringComparison.OrdinalIgnoreCase) && child.GetComponent<Canvas>() == null)
                return child;
        }

        return root.childCount > 0 ? root.GetChild(0) : null;
    }

    private void OnDisable()
    {
        ResetPose();
    }

    private void ResetPose()
    {
        if (visualTransform != null && visualTransform != transform)
        {
            visualTransform.localPosition = initialLocalPos;
            visualTransform.localRotation = initialLocalRot;
        }
        _currentProceduralPosOffset = Vector3.zero;
        _currentProceduralRotEuler = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (visualTransform == null || visualTransform == transform || Time.deltaTime <= 0f)
            return;

        // If dead, gracefully restore baseline pose so death animation / ragdoll isn't perturbed
        if (IsDead())
        {
            _currentProceduralPosOffset = Vector3.Lerp(_currentProceduralPosOffset, Vector3.zero, Time.deltaTime * 6f);
            _currentProceduralRotEuler = Vector3.Lerp(_currentProceduralRotEuler, Vector3.zero, Time.deltaTime * 6f);
            visualTransform.localPosition = initialLocalPos + _currentProceduralPosOffset;
            visualTransform.localRotation = initialLocalRot * Quaternion.Euler(_currentProceduralRotEuler);
            _lastRootPos = transform.position;
            return;
        }

        Vector3 targetPosOffset = Vector3.zero;
        Vector3 targetRotEuler = Vector3.zero;

        // 1. Detect movement speed and state
        float speed = 0f;
        if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
        {
            speed = _agent.velocity.magnitude;
        }
        else
        {
            float dist = Vector3.Distance(transform.position, _lastRootPos);
            speed = dist / Mathf.Max(0.0001f, Time.deltaTime);
        }
        _lastRootPos = transform.position;

        bool isMoving = speed > 0.15f || (_agent != null && _agent.velocity.sqrMagnitude > 0.05f);

        // 2. Locomotion Bob & Stride Lean
        if (isMoving)
        {
            _locomotionCycle += Time.deltaTime * strideFrequency;

            if (_isAnimal)
            {
                // Wildlife (Deer) graceful bounding stride
                float boundWave = Mathf.Sin(_locomotionCycle);
                float boundY = Mathf.Max(0f, boundWave) * wildlifeBoundHeight;
                float boundPitch = boundWave * 5.5f;

                targetPosOffset.y += boundY;
                targetRotEuler.x += (boundPitch + forwardLeanAngle * 0.7f);
                targetRotEuler.z += Mathf.Sin(_locomotionCycle * 0.5f) * (swayRollAngle * 0.8f);
            }
            else
            {
                // Stride bounce: Mathf.Abs(Mathf.Sin(Time.time * strideFreq)) * bounceAmp
                float bounce = Mathf.Abs(Mathf.Sin(_locomotionCycle)) * bounceAmp;
                targetPosOffset.y += bounce;

                // Gentle forward lean (4f–7f degrees)
                float currentLean = forwardLeanAngle;

                // Side-to-side sway (2.5f degrees)
                float sway = Mathf.Sin(_locomotionCycle * 0.5f) * swayRollAngle;

                // Cavalry Canter/Gallop pitch wave: Mathf.Sin(Time.time * 11f) * 5f degrees
                if (_isCavalry)
                {
                    float canterPitch = Mathf.Sin(Time.time * cavalryCanterFreq) * cavalryCanterPitch;
                    targetRotEuler.x += (currentLean + canterPitch);
                    targetPosOffset.y += Mathf.Sin(Time.time * cavalryCanterFreq) * 0.025f;
                }
                else
                {
                    targetRotEuler.x += currentLean;
                }

                targetRotEuler.z += sway;
            }
        }
        else
        {
            // Smoothly damp locomotion cycle towards integer half-pi
            _locomotionCycle = Mathf.MoveTowards(_locomotionCycle, Mathf.Round(_locomotionCycle / Mathf.PI) * Mathf.PI, Time.deltaTime * 5f);
        }

        // 3. Combat / Gathering Strike Lunge
        bool isStriking = IsActivelyAttackingOrGathering(isMoving);
        if (isStriking)
        {
            float strikeFreq = _isVillager ? 5.2f : 4.6f;
            _strikeCycle += Time.deltaTime * strikeFreq;
            float phase = (_strikeCycle % (Mathf.PI * 2f)) / (Mathf.PI * 2f); // 0.0 to 1.0

            float strikePitch = 0f;
            float strikeFwd = 0f;
            float strikeY = 0f;

            if (phase < 0.35f)
            {
                // Wind-up: slight raise & lean back
                float t = phase / 0.35f;
                strikePitch = Mathf.Lerp(0f, -4f, t);
                strikeFwd = Mathf.Lerp(0f, -0.015f, t);
                strikeY = Mathf.Lerp(0f, 0.02f, t);
            }
            else if (phase < 0.52f)
            {
                // Punchy Strike / Chop impact: snap forward and down
                float t = (phase - 0.35f) / 0.17f;
                float snapT = t * t;
                strikePitch = Mathf.Lerp(-4f, strikePitchAngle, snapT);
                strikeFwd = Mathf.Lerp(-0.015f, strikeForwardOffset, snapT);
                strikeY = Mathf.Lerp(0.02f, -0.035f, snapT);
            }
            else
            {
                // Recovery: smooth ease back to rest pose
                float t = (phase - 0.52f) / 0.48f;
                float recoverT = Mathf.SmoothStep(0f, 1f, t);
                strikePitch = Mathf.Lerp(strikePitchAngle, 0f, recoverT);
                strikeFwd = Mathf.Lerp(strikeForwardOffset, 0f, recoverT);
                strikeY = Mathf.Lerp(-0.035f, 0f, recoverT);
            }

            targetRotEuler.x += strikePitch;
            targetPosOffset.z += strikeFwd;
            targetPosOffset.y += strikeY;
        }
        else
        {
            _strikeCycle = 0f;
        }

        // 4. Wildlife Grazing Cycle (Idle / stopped wildlife)
        if (_isAnimal && !isMoving)
        {
            _grazingPhase += Time.deltaTime * 0.45f;
            float cycle = (_grazingPhase % (Mathf.PI * 2f));
            float dip = Mathf.Clamp01(Mathf.Sin(cycle) * 1.5f);

            if (dip > 0.05f)
            {
                // Grazing head/body dip down
                float chew = (dip > 0.5f) ? Mathf.Sin(Time.time * 6.5f) * 1.3f : 0f;
                float dipAngle = (dip * grazingDipAngle) + chew;
                float dipY = -dip * 0.05f;
                float dipZ = dip * 0.025f;

                targetRotEuler.x += dipAngle;
                targetPosOffset.y += dipY;
                targetPosOffset.z += dipZ;
            }
            else
            {
                // Peaceful head raised / alert look
                targetRotEuler.x += Mathf.Sin(Time.time * 1.5f) * 1.5f;
                targetRotEuler.y += Mathf.Sin(Time.time * 0.8f) * 3.0f;
            }
        }

        // 5. Smooth blending and apply to visual child transform (NEVER ROOT!)
        float blendSpeed = isMoving ? 14f : 8f;
        _currentProceduralPosOffset = Vector3.Lerp(_currentProceduralPosOffset, targetPosOffset, Time.deltaTime * blendSpeed);
        _currentProceduralRotEuler = Vector3.Lerp(_currentProceduralRotEuler, targetRotEuler, Time.deltaTime * blendSpeed);

        visualTransform.localPosition = initialLocalPos + _currentProceduralPosOffset;
        visualTransform.localRotation = initialLocalRot * Quaternion.Euler(_currentProceduralRotEuler);
    }

    private bool IsDead()
    {
        if (_unit != null && _unit.IsDead) return true;
        if (_animalNode != null && (_animalNode.IsEmpty || _animalNode.IsKilled)) return true;
        if (GetAnimBoolSafe("Die")) return true;
        return false;
    }

    private bool IsActivelyAttackingOrGathering(bool isMoving)
    {
        // Villager gathering, chopping, digging, building
        if (_isVillager)
        {
            if (GetAnimBoolSafe("IsChopping") ||
                GetAnimBoolSafe("IsDigging") ||
                GetAnimBoolSafe("IsGathering") ||
                GetAnimBoolSafe("IsThrusting") ||
                GetAnimBoolSafe("IsPunching"))
            {
                return true;
            }

            if (_villager != null && _villager.IsGathering && !isMoving)
            {
                return true;
            }

            return false;
        }

        // Unit attacking (Infantry, Cavalry, etc.)
        if (_unit != null && !isMoving)
        {
            if (GetAnimBoolSafe("Attack")) return true;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                try
                {
                    var stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
                    if (stateInfo.IsName("Attack") ||
                        stateInfo.IsName("Punching") ||
                        stateInfo.IsName("swaord slash") ||
                        stateInfo.IsName("slash"))
                    {
                        return true;
                    }
                }
                catch { }
            }

            if (_unit.HasActiveTarget)
            {
                return true;
            }
        }

        return false;
    }

    // =========================================================================
    // Bootstrap Tracker
    // Automatically attaches ProceduralUnitAnimator to every Unit and AnimalBehavior
    // in the scene on load and monitors for dynamically spawned units/animals.
    // =========================================================================
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        GameObject bootstrapperGO = new GameObject("[ProceduralUnitAnimatorBootstrapper]");
        DontDestroyOnLoad(bootstrapperGO);
        bootstrapperGO.AddComponent<ProceduralUnitBootstrapper>();
    }
}

/// <summary>
/// Alias / marker component for wildlife animal behavior if referenced.
/// </summary>
public class AnimalBehavior : MonoBehaviour
{
    // Marker component for wildlife entities
}

/// <summary>
/// Persistent bootstrapper that ensures all scene and newly spawned units / wildlife have ProceduralUnitAnimator attached.
/// </summary>
public class ProceduralUnitBootstrapper : MonoBehaviour
{
    private float _nextScanTime = 0f;
    private const float SCAN_INTERVAL = 0.5f;

    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ScanAndAttach();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ScanAndAttach();
    }

    private void Update()
    {
        if (Time.time >= _nextScanTime)
        {
            _nextScanTime = Time.time + SCAN_INTERVAL;
            ScanAndAttach();
        }
    }

    public static void ScanAndAttach()
    {
        // 1. Attach to all Unit components (Infantry, Cavalry, Villager, Builder, etc.)
        Unit[] units = FindObjectsOfType<Unit>(true);
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] != null && units[i].GetComponent<ProceduralUnitAnimator>() == null)
            {
                units[i].gameObject.AddComponent<ProceduralUnitAnimator>();
            }
        }

        // 2. Attach to all AnimalNode components
        AnimalNode[] animals = FindObjectsOfType<AnimalNode>(true);
        for (int i = 0; i < animals.Length; i++)
        {
            if (animals[i] != null && animals[i].GetComponent<ProceduralUnitAnimator>() == null)
            {
                animals[i].gameObject.AddComponent<ProceduralUnitAnimator>();
            }
        }

        // 3. Attach to any AnimalBehavior components
        AnimalBehavior[] animalBehaviors = FindObjectsOfType<AnimalBehavior>(true);
        for (int i = 0; i < animalBehaviors.Length; i++)
        {
            if (animalBehaviors[i] != null && animalBehaviors[i].GetComponent<ProceduralUnitAnimator>() == null)
            {
                animalBehaviors[i].gameObject.AddComponent<ProceduralUnitAnimator>();
            }
        }
    }
}
