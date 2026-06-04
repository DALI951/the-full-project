using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Mirror;

public class Building : NetworkBehaviour
{
    [Header("Ownership")]
    [SyncVar] [SerializeField] private int ownerPlayerId = 0;

    [Header("Spawn Settings")]
    [SerializeField] protected Transform        spawnPoint;
    [SerializeField] protected List<GameObject> spawnablePrefabs = new List<GameObject>();
    [SerializeField] private   float            spawnSpacing     = 1.5f;

    [Header("Building Info")]
    [SerializeField] protected string buildingName = "Building";
    [SerializeField] protected string buildingDescription = "";

    [Header("Health")]
    [SerializeField] public int maxBuildingHealth = 500;
    public int currentBuildingHealth;

    [SyncVar(hook = nameof(OnSyncHealthChanged))]
    private int syncHealth;

    [Header("Training Costs & Times")]
    [SerializeField] private List<float> unitTrainingTimes = new List<float>();
    [SerializeField] private List<int>   unitCostFood      = new List<int>();
    [SerializeField] private List<int>   unitCostWood      = new List<int>();
    [SerializeField] private List<int>   unitCostGold      = new List<int>();
    [SerializeField] private int         populationPerUnit = 1;

    public struct TrainingBatch
    {
        public int unitIndex;
        public int count;
        public TrainingBatch(int idx, int cnt) { unitIndex = idx; count = cnt; }
    }

    private List<TrainingBatch> trainingQueue   = new List<TrainingBatch>();
    private bool                isTraining      = false;
    private float               trainingTimer   = 0f;
    private float               currentUnitTime = 0f;
    private TrainingBatch       currentBatch;
    private int                 pendingPopulation = 0;
    private int                 batchSpawnedSoFar = 0;
    private Renderer[]          cachedRenderers;
    private bool                _visibilityInitialized;

    [SyncVar] private bool   syncIsTraining;
    [SyncVar] private float  syncTrainingProgress;
    [SyncVar] private string syncTrainingLabel;
    [SyncVar] private int    syncQueueCount;

    public static List<Building> AllBuildings { get; private set; } = new List<Building>();

    protected virtual void Start()
    {
        if (isServer) syncHealth = maxBuildingHealth;
        currentBuildingHealth = maxBuildingHealth;

        Collider col = GetComponent<Collider>();
        if (col == null) col = GetComponentInChildren<Collider>();
        if (col == null)
        {
            BoxCollider bc = gameObject.AddComponent<BoxCollider>();
            bc.size   = new Vector3(2.5f, 2.5f, 2.5f);
            bc.center = new Vector3(0f, 1.25f, 0f);
        }
        else
        {
            // Slightly scale down existing colliders to prevent z-fighting
            if (col is BoxCollider bc)
            {
                bc.size *= 0.98f; // 2% smaller
            }
            else if (col is CapsuleCollider cc)
            {
                cc.radius *= 0.98f;
                cc.height *= 0.98f;
            }
            else if (col is SphereCollider sc)
            {
                sc.radius *= 0.98f;
            }
        }

        if (spawnPoint == null)
            Debug.LogWarning($"[{buildingName}] No spawnPoint assigned!");

        if (!AllBuildings.Contains(this))
            AllBuildings.Add(this);

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        MinimapSystem.Instance?.TrackBuilding(this);

        // Only register own buildings so training UI shows correct player's progress
        if (ownerPlayerId == PlayerColorManager.LocalPlayerIndex)
            GameUI.Instance?.RegisterBuilding(this);
    }

    protected virtual void Update()
    {
        if (isClient && ownerPlayerId != PlayerColorManager.LocalPlayerIndex)
        {
            bool isAlly = NetworkedPlayer.LocalInstance != null
                && NetworkedPlayer.SameTeam(PlayerColorManager.LocalPlayerIndex, ownerPlayerId);
            if (!isAlly)
            {
                // Use IsExplored: once player has seen the building it stays visible (standard RTS behaviour)
                bool visible = FogOfWar.Instance == null || FogOfWar.Instance.IsExplored(transform.position);
                if (cachedRenderers != null)
                    foreach (Renderer r in cachedRenderers)
                        if (r != null) r.enabled = visible;
            }
            else if (!_visibilityInitialized)
            {
                if (cachedRenderers != null)
                    foreach (Renderer r in cachedRenderers)
                        if (r != null) r.enabled = true;
                _visibilityInitialized = true;
            }
        }

        if (isClient && !isServer) return;

        syncIsTraining = isTraining;
        syncTrainingProgress = isTraining && currentUnitTime > 0f
            ? 1f - (trainingTimer / currentUnitTime) : 0f;
        syncTrainingLabel = GetServerTrainingLabel();
        syncQueueCount = trainingQueue.Count;

        if (!isTraining) return;
        trainingTimer -= Time.deltaTime;
        if (trainingTimer <= 0f)
            TryCompleteBatch();
    }

    private void OnDestroy()
    {
        AllBuildings.Remove(this);
        trainingQueue.Clear();
        isTraining        = false;
        pendingPopulation = 0;
    }

    private void OnMouseDown()
    {
        if (IsClickOnInteractiveUI()) return;
        bool isEnemy = ownerPlayerId != PlayerColorManager.LocalPlayerIndex;
        if (!isEnemy)
            SelectionManager.Instance?.SelectBuilding(this);
        BuildingInfoUI.Instance?.ShowBuilding(this);
    }

    private bool IsClickOnInteractiveUI()
    {
        if (EventSystem.current == null) return false;
        var pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = Input.mousePosition;
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        foreach (var result in results)
        {
            string n = result.gameObject.name.ToLower();
            if (n.Contains("viewport")) continue;
            if (n.Contains("fog")) continue;
            if (n.Contains("rawimage")) continue;
            if (n.Contains("image") && !n.Contains("button")) continue;
            if (result.gameObject.GetComponent<Button>() != null) return true;
            if (result.gameObject.GetComponent<TMPro.TMP_InputField>() != null) return true;
            if (result.gameObject.GetComponent<UnityEngine.UI.Slider>() != null) return true;
            if (result.gameObject.GetComponent<UnityEngine.UI.Dropdown>() != null) return true;
            if (result.gameObject.GetComponent<UnityEngine.UI.Scrollbar>() != null) return true;
            if (n.Contains("button")) return true;
            if (n.Contains("close")) return true;
        }
        return false;
    }

    public void SetSpawnPoint(Vector3 worldPos)
    {
        if (spawnPoint != null) spawnPoint.position = worldPos;
    }

    public virtual void Select()
    {
        Debug.Log($"[{buildingName}] Selected.");
        GameUI.Instance?.ShowBuildingUI(this);
    }

    public virtual void Deselect()
    {
    }

    public void SpawnUnit(int index)
    {
        if (isClient && !isServer) return;
        if (index < 0 || index >= spawnablePrefabs.Count)
        { Debug.LogError($"[{buildingName}] Bad index {index}"); return; }
        if (spawnablePrefabs[index] == null)
        { Debug.LogError($"[{buildingName}] Null prefab at {index}"); return; }

        int food = GetCost(unitCostFood, index);
        int wood = GetCost(unitCostWood, index);
        int gold = GetCost(unitCostGold, index);

        bool isEnemyOwned = ownerPlayerId != PlayerColorManager.LocalPlayerIndex;

        NetworkedPlayer owner = NetworkedPlayer.Get(ownerPlayerId);
        if (owner != null)
        {
            if (!owner.TrySpend(food, wood, gold))
            { Debug.Log($"[{buildingName}] Not enough resources (networked)."); return; }
        }
        else if (!isEnemyOwned)
        {
            if (ResourceManager.Instance != null && !ResourceManager.Instance.TrySpend(food, wood, gold))
            { Debug.Log($"[{buildingName}] Not enough resources."); return; }
        }
        else
        {
            if (EnemyAI.Instance != null && !EnemyAI.Instance.TrySpend(food, wood, gold)) return;
        }

        int currentPop = 0;
        int maxPop = 20;
        if (owner != null)
        {
            currentPop = owner.CurrentPopulation;
            maxPop = owner.MaxPopulation;
        }
        else if (!isEnemyOwned)
        {
            if (ResourceManager.Instance != null) { currentPop = ResourceManager.Instance.CurrentPopulation; maxPop = ResourceManager.Instance.MaxPopulation; }
        }
        else
        {
            if (EnemyAI.Instance != null) { currentPop = EnemyAI.Instance.CurrentPopulation; maxPop = EnemyAI.Instance.MaxPopulation; }
        }
        if (currentPop + pendingPopulation + populationPerUnit > maxPop)
        {
            if (owner != null) owner.AddResources(food, wood, gold);
            else if (!isEnemyOwned) ResourceManager.Instance?.AddResources(food, wood, gold);
            else EnemyAI.Instance?.AddEnemyResources(food, wood, gold);
            Debug.Log($"[{buildingName}] Population limit reached.");
            return;
        }

        if (isTraining && currentBatch.unitIndex == index && currentBatch.count < 5)
        {
            currentBatch = new TrainingBatch(index, currentBatch.count + 1);
            pendingPopulation += populationPerUnit;
            return;
        }

        bool lastBatchFull = trainingQueue.Count > 0
            && trainingQueue[trainingQueue.Count - 1].count >= 5;
        if (trainingQueue.Count >= 5 && lastBatchFull)
        {
            if (owner != null) owner.AddResources(food, wood, gold);
            else if (!isEnemyOwned) ResourceManager.Instance?.AddResources(food, wood, gold);
            else EnemyAI.Instance?.AddEnemyResources(food, wood, gold);
            Debug.Log($"[{buildingName}] Queue full (max 5 batches of 5).");
            return;
        }

        bool canMerge = trainingQueue.Count > 0
            && trainingQueue[trainingQueue.Count - 1].unitIndex == index
            && trainingQueue[trainingQueue.Count - 1].count < 5;
        if (canMerge)
        {
            var last = trainingQueue[trainingQueue.Count - 1];
            trainingQueue[trainingQueue.Count - 1] = new TrainingBatch(last.unitIndex, last.count + 1);
        }
        else
        {
            trainingQueue.Add(new TrainingBatch(index, 1));
        }

        pendingPopulation += populationPerUnit;

        if (!isTraining)
            StartNextBatch();
    }

    private void StartNextBatch()
    {
        if (trainingQueue.Count == 0) { isTraining = false; return; }

        currentBatch    = trainingQueue[0];
        trainingQueue.RemoveAt(0);
        currentUnitTime = GetTrainingTime(currentBatch.unitIndex);
        trainingTimer   = currentUnitTime;
        isTraining      = true;
        batchSpawnedSoFar = 0;
    }

    private void TryCompleteBatch()
    {
        if (currentBatch.unitIndex < 0 || currentBatch.unitIndex >= spawnablePrefabs.Count)
        { StartNextBatch(); return; }

        GameObject prefab = spawnablePrefabs[currentBatch.unitIndex];
        if (prefab == null) { StartNextBatch(); return; }

        Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : transform.rotation;
        int spawned = 0;

        for (int i = batchSpawnedSoFar; i < currentBatch.count; i++)
        {
            Vector3 pos = FindFreeSpawnPosition(batchSpawnedSoFar + spawned);
            if (pos == Vector3.zero)
            {
                if (spawned > 0)
                {
                    int partialPop = spawned * populationPerUnit;
                    NetworkedPlayer owner = NetworkedPlayer.Get(ownerPlayerId);
                    if (owner != null) owner.AddPopulation(partialPop);
                    else if (ownerPlayerId == PlayerColorManager.LocalPlayerIndex)
                        ResourceManager.Instance?.AddPopulation(partialPop);
                    else
                        EnemyAI.Instance?.AddPopulation(partialPop);
                    pendingPopulation  = Mathf.Max(0, pendingPopulation - partialPop);
                    batchSpawnedSoFar += spawned;
                }
                trainingTimer = 0.3f;
                return;
            }
            GameObject go = Instantiate(prefab, pos, spawnRot);
            if (go.TryGetComponent(out Unit unit))
            {
                unit.SetOwner(ownerPlayerId);
                if (spawnPoint != null && Vector3.Distance(pos, spawnPoint.position) > 2f)
                    unit.SetFirstWaypoint(spawnPoint.position);
            }
            if (NetworkServer.active)
                NetworkServer.Spawn(go, netIdentity.connectionToClient);
            spawned++;
        }

        int popCost = spawned * populationPerUnit;
        NetworkedPlayer pwner = NetworkedPlayer.Get(ownerPlayerId);
        if (pwner != null) pwner.AddPopulation(popCost);
        else if (ownerPlayerId == PlayerColorManager.LocalPlayerIndex)
            ResourceManager.Instance?.AddPopulation(popCost);
        else
            EnemyAI.Instance?.AddPopulation(popCost);
        pendingPopulation = Mathf.Max(0, pendingPopulation - popCost);
        batchSpawnedSoFar = 0;

        StartNextBatch();
    }

    private Vector3 FindFreeSpawnPosition(int spawnIndex)
    {
        Vector3 basePos = transform.position + transform.forward * 2.5f;

        for (int ring = 0; ring <= 3; ring++)
        {
            int steps = ring == 0 ? 1 : ring * 8;
            for (int i = 0; i < steps; i++)
            {
                Vector3 candidate;
                if (ring == 0)
                {
                    candidate = basePos;
                }
                else
                {
                    float angle = (i + spawnIndex) * (360f / steps) * Mathf.Deg2Rad;
                    candidate = basePos + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle))
                                         * (spawnSpacing * ring);
                }

                if (IsPosClear(candidate, out Vector3 snapped))
                    return snapped;
            }
        }
        return Vector3.zero;
    }

    private bool IsPosClear(Vector3 pos, out Vector3 snapped)
    {
        snapped = pos;
        if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 30f, NavMesh.AllAreas))
            return false;
        snapped = hit.position;
        Collider[] cols = Physics.OverlapSphere(snapped, 0.45f);
        foreach (Collider c in cols)
            if (c.GetComponentInParent<Unit>() != null) return false;
        return true;
    }

    private float GetTrainingTime(int index)
    {
        if (unitTrainingTimes != null && index < unitTrainingTimes.Count && unitTrainingTimes[index] > 0f)
            return unitTrainingTimes[index];
        return 5f;
    }

    private int GetCost(List<int> costList, int index)
    {
        if (costList != null && index < costList.Count) return costList[index];
        return 0;
    }

    public bool  IsTraining      => (!isClient || isServer) ? isTraining : syncIsTraining;
    public float TrainingProgress => (!isClient || isServer)
        ? (isTraining && currentUnitTime > 0f ? 1f - (trainingTimer / currentUnitTime) : 0f)
        : syncTrainingProgress;
    public int   QueueBatchCount => (!isClient || isServer) ? trainingQueue.Count : syncQueueCount;

    public string CurrentTrainingLabel => (!isClient || isServer) ? GetServerTrainingLabel() : syncTrainingLabel;

    private string GetServerTrainingLabel()
    {
        if (!isTraining) return "";
        int idx = currentBatch.unitIndex;
        if (idx < 0 || idx >= spawnablePrefabs.Count || spawnablePrefabs[idx] == null) return "";
        string n = spawnablePrefabs[idx].name;
        return currentBatch.count > 1 ? $"{n} x{currentBatch.count}" : n;
    }

    public string[] GetQueueLabels()
    {
        if (isClient && !isServer) return new string[0];
        string[] labels = new string[trainingQueue.Count];
        for (int i = 0; i < trainingQueue.Count; i++)
        {
            int idx = trainingQueue[i].unitIndex;
            string n = (idx >= 0 && idx < spawnablePrefabs.Count && spawnablePrefabs[idx] != null)
                ? spawnablePrefabs[idx].name : "?";
            labels[i] = trainingQueue[i].count > 1 ? $"{n} x{trainingQueue[i].count}" : n;
        }
        return labels;
    }

    public void TakeDamage(int amount)
    {
        if (isClient && !isServer) return;
        syncHealth = Mathf.Max(0, syncHealth - amount);

        if (isServer) RpcOnBuildingDamage(transform.position);

        if (syncHealth <= 0) OnBuildingDestroyed();
    }

    [ClientRpc]
    private void RpcOnBuildingDamage(Vector3 pos)
    {
        if (!isClient) return;
        EffectManager.Instance?.PlayBuildingHitEffect(pos);
        ScreenShake.Instance?.LightShake();
    }

    private void OnSyncHealthChanged(int oldVal, int newVal)
    {
        currentBuildingHealth = newVal;
    }

    private void OnBuildingDestroyed()
    {
        if (isServer) RpcOnBuildingDestroyed(transform.position);
        EnemyAI.Instance?.OnBuildingDestroyed(this);
        AllBuildings.Remove(this);
        if (GameOverManager.Instance != null && NetworkServer.active)
            GameOverManager.Instance.CheckGameOver(OwnerPlayerId);
        if (NetworkServer.active)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    [ClientRpc]
    private void RpcOnBuildingDestroyed(Vector3 pos)
    {
        if (!isClient) return;
        EffectManager.Instance?.PlayDeathEffect(pos);
        ScreenShake.Instance?.HeavyShake();
    }

    public string            BuildingName     => buildingName;
    public string BuildingDescription => buildingDescription;
    public List<GameObject>  SpawnablePrefabs => spawnablePrefabs;
    public int               OwnerPlayerId    => ownerPlayerId;
    public int               CurrentBuildingHealth => currentBuildingHealth;
    public int               MaxBuildingHealth     => maxBuildingHealth;
    public virtual int       TrainingPriority      => 0;
    public (int food, int wood, int gold) GetUnitCost(int index)
        => (GetCost(unitCostFood, index), GetCost(unitCostWood, index), GetCost(unitCostGold, index));

    public float GetUnitTrainingTime(int index) => GetTrainingTime(index);

    public void SetOwner(int playerId) { ownerPlayerId = playerId; }
}
