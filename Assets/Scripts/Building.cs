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

    [Header("AoE4 Upgrades & State")]
    public bool hasMerchantGuilds = false;
    private float merchantGuildTimer = 0f;
    public bool hasArrowSlits = false;
    public bool hasBoilingOil = false;
    public bool isGateOpen = false;
    private float townBellCooldownTimer = 0f;

    // Static Progression & Tech Buffs
    public static int CurrentAge = 1;
    public static int BonusMilitaryDamage = 0;
    public static int BonusMilitaryHP = 0;
    public static bool TechWheelbarrowResearched = false;
    public static bool TechDamascusSteelResearched = false;
    public static bool TechPlateMailResearched = false;
    public static bool TechDoubleBroadaxeResearched = false;
    public static bool TechForestryResearched = false;
    public static bool TechHorticultureResearched = false;
    public static float VillagerSpeedMultiplier = 1f;
    public static int VillagerCarryBonus = 0;
    public static float WoodYieldMultiplier = 1f;

    public GameObject[] trainableUnits
    {
        get => spawnablePrefabs != null ? spawnablePrefabs.ToArray() : new GameObject[0];
        set => spawnablePrefabs = value != null ? new List<GameObject>(value) : new List<GameObject>();
    }

    public class BuildingAction
    {
        public string id;
        public string title;
        public string description;
        public int costFood;
        public int costWood;
        public int costGold;
        public bool isOneTimeTech;
        public bool isResearched;
        public System.Func<bool> isAvailable;
        public System.Func<string> getDynamicLabel;
        public System.Action onExecute;
    }

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

    protected virtual void Awake()
    {
        EnsureDefaultTrainableUnits();
    }

    protected virtual void Start()
    {
        EnsureDefaultTrainableUnits();

        if (isServer || !NetworkServer.active) syncHealth = maxBuildingHealth;
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

        var fr = GetComponent<FogRevealer>();
        if (fr == null) fr = gameObject.AddComponent<FogRevealer>();
        fr.VisionRadius = 18f;

        var hb = GetComponent<WorldHealthBar>();
        if (hb == null) hb = gameObject.AddComponent<WorldHealthBar>();

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        MinimapSystem.Instance?.TrackBuilding(this);

        // Only register own buildings so training UI shows correct player's progress
        if (ownerPlayerId == PlayerColorManager.LocalPlayerIndex)
            GameUI.Instance?.RegisterBuilding(this);
    }

    public void EnsureDefaultTrainableUnits()
    {
        bool isHome = IsHomeSiteBuilding;
        if (isHome)
        {
            if (spawnablePrefabs == null) spawnablePrefabs = new List<GameObject>();
            if (spawnablePrefabs.Count == 0 || spawnablePrefabs[0] == null)
            {
                GameObject villager = FindPrefabByName("Villager");
                if (villager != null)
                {
                    if (spawnablePrefabs.Count == 0) spawnablePrefabs.Add(villager);
                    else spawnablePrefabs[0] = villager;

                    EnsureCostListsSize(1);
                    unitCostFood[0] = 50;
                    unitCostWood[0] = 0;
                    unitCostGold[0] = 0;
                    unitTrainingTimes[0] = 5f;
                }
            }
            else
            {
                EnsureCostListsSize(spawnablePrefabs.Count);
                if (unitCostFood[0] == 0 && unitCostWood[0] == 0 && unitCostGold[0] == 0) unitCostFood[0] = 50;
                if (unitTrainingTimes[0] <= 0f) unitTrainingTimes[0] = 5f;
            }
        }

        bool isBarracks = IsBarracksBuilding;
        if (isBarracks)
        {
            if (spawnablePrefabs == null) spawnablePrefabs = new List<GameObject>();
            while (spawnablePrefabs.Count < 2) spawnablePrefabs.Add(null);

            if (spawnablePrefabs[0] == null)
                spawnablePrefabs[0] = FindPrefabByName("Infantry");
            if (spawnablePrefabs[1] == null)
                spawnablePrefabs[1] = FindPrefabByName("Cavalry");

            EnsureCostListsSize(2);
            // Infantry ("Man-at-Arms"): costFood = 60, costWood = 0, costGold = 20, trainTime = 7f
            unitCostFood[0] = 60;
            unitCostWood[0] = 0;
            unitCostGold[0] = 20;
            unitTrainingTimes[0] = 7f;

            // Cavalry ("Royal Knight"): costFood = 100, costWood = 0, costGold = 60, trainTime = 10f
            unitCostFood[1] = 100;
            unitCostWood[1] = 0;
            unitCostGold[1] = 60;
            unitTrainingTimes[1] = 10f;
        }
    }

    private void EnsureCostListsSize(int size)
    {
        if (unitCostFood == null) unitCostFood = new List<int>();
        if (unitCostWood == null) unitCostWood = new List<int>();
        if (unitCostGold == null) unitCostGold = new List<int>();
        if (unitTrainingTimes == null) unitTrainingTimes = new List<float>();

        while (unitCostFood.Count < size) unitCostFood.Add(0);
        while (unitCostWood.Count < size) unitCostWood.Add(0);
        while (unitCostGold.Count < size) unitCostGold.Add(0);
        while (unitTrainingTimes.Count < size) unitTrainingTimes.Add(5f);
    }

    private static GameObject FindPrefabByName(string targetName)
    {
        // 1. Check existing buildings
        if (AllBuildings != null)
        {
            foreach (var b in AllBuildings)
            {
                if (b != null && b.spawnablePrefabs != null)
                {
                    foreach (var p in b.spawnablePrefabs)
                    {
                        if (p != null)
                        {
                            if (p.name.IndexOf(targetName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                                return p;
                            if (targetName.IndexOf("cavalry", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                                p.name.IndexOf("cavarly", System.StringComparison.OrdinalIgnoreCase) >= 0)
                                return p;
                        }
                    }
                }
            }
        }

        // 2. Search Resources.FindObjectsOfTypeAll<GameObject>()
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var go in all)
        {
            if (go != null && !go.scene.IsValid())
            {
                if (go.name.Equals(targetName, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (go.GetComponent<Unit>() != null) return go;
                }
                if (targetName.IndexOf("cavalry", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                    go.name.IndexOf("cavarly", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (go.GetComponent<Unit>() != null) return go;
                }
            }
        }

        // 3. Fallback: Any GameObject matching name with Unit
        foreach (var go in all)
        {
            if (go != null)
            {
                if (go.name.IndexOf(targetName, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (targetName.IndexOf("cavalry", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                     go.name.IndexOf("cavarly", System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    if (go.GetComponent<Unit>() != null) return go;
                }
            }
        }
        return null;
    }

    private float passiveEcoTimer = 0f;

    protected virtual void Update()
    {
        if (townBellCooldownTimer > 0f)
            townBellCooldownTimer -= Time.deltaTime;

        UpdatePassiveResourceGeneration();

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

    private void UpdatePassiveResourceGeneration()
    {
        // Only run generation on server or offline single player
        if (isClient && !isServer) return;

        passiveEcoTimer += Time.deltaTime;
        if (passiveEcoTimer < 5f) return;
        passiveEcoTimer = 0f;

        int foodGen = 0;
        int woodGen = 0;
        int goldGen = 0;

        if (IsFarmBuilding)
        {
            foodGen = TechHorticultureResearched ? 15 : 10;
        }
        else if (IsLumberMillBuilding)
        {
            woodGen = Mathf.RoundToInt(10 * WoodYieldMultiplier);
        }
        else if (IsMarketBuilding && hasMerchantGuilds)
        {
            goldGen = 12;
        }

        if (foodGen == 0 && woodGen == 0 && goldGen == 0) return;

        bool isLocalPlayer = (ownerPlayerId == PlayerColorManager.LocalPlayerIndex);
        NetworkedPlayer ownerNp = NetworkedPlayer.Get(ownerPlayerId);

        if (ownerNp != null)
        {
            ownerNp.AddResources(foodGen, woodGen, goldGen);
        }
        else if (isLocalPlayer)
        {
            ResourceManager.Instance?.AddResources(foodGen, woodGen, goldGen);
            if (foodGen > 0) EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2.2f, $"+{foodGen} Food", Color.green);
            if (woodGen > 0) EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2.2f, $"+{woodGen} Wood", Color.green);
            if (goldGen > 0) EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2.2f, $"+{goldGen} Gold", Color.yellow);
        }
        else if (EnemyAI.Instance != null && (ownerPlayerId == EnemyAI.Instance.enemyPlayerId || ownerPlayerId != PlayerColorManager.LocalPlayerIndex))
        {
            EnemyAI.Instance.AddEnemyResources(foodGen, woodGen, goldGen);
        }
    }

    private void OnDestroy()
    {
        AllBuildings.Remove(this);
        trainingQueue.Clear();
        isTraining        = false;

        // REFUND the population this building had reserved for units that never
        // got produced.
        int refund = pendingPopulation;
        pendingPopulation = 0;
        if (refund > 0)
        {
            NetworkedPlayer owner = NetworkedPlayer.Get(ownerPlayerId);
            if (owner != null) owner.RemovePopulation(refund);
            else if (ownerPlayerId == PlayerColorManager.LocalPlayerIndex)
                ResourceManager.Instance?.RemovePopulation(refund);
            else
                EnemyAI.Instance?.RemovePopulation(refund);
        }
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

    private bool hasCustomRallyPoint = false;
    private Vector3 customRallyPointPos;
    public bool HasCustomRallyPoint => hasCustomRallyPoint;
    public Vector3 RallyPointPosition
    {
        get
        {
            if (hasCustomRallyPoint) return customRallyPointPos;
            if (spawnPoint != null) return spawnPoint.position + spawnPoint.forward * 2.5f;
            return transform.position + transform.forward * 4.5f;
        }
    }

    public void SetSpawnPoint(Vector3 worldPos)
    {
        customRallyPointPos = worldPos;
        hasCustomRallyPoint = true;
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
                if (ownerPlayerId == PlayerColorManager.LocalPlayerIndex)
                {
                    if (BonusMilitaryDamage > 0 || BonusMilitaryHP > 0)
                    {
                        if (unit is Infantry || unit is Cavalry)
                            ApplyUnitBuff(unit, BonusMilitaryDamage, BonusMilitaryHP);
                    }
                    if (TechWheelbarrowResearched && unit is Villager)
                    {
                        ApplyWheelbarrowToVillager(unit as Villager);
                    }
                }
                // Move spawned unit to rally point with random jitter so multiple trained units do not stack inside each other
                Vector2 jitter2D = Random.insideUnitCircle * 1.5f;
                Vector3 targetDest = RallyPointPosition + new Vector3(jitter2D.x, 0f, jitter2D.y);
                if (NavMesh.SamplePosition(targetDest, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
                    targetDest = navHit.position;

                unit.ClearWaypoints();
                unit.MoveTo(targetDest);
            }
            if (NetworkServer.active)
            {
                NetworkedPlayer ownerNp = NetworkedPlayer.Get(ownerPlayerId);
                if (ownerNp != null && ownerNp.connectionToClient != null)
                    NetworkServer.Spawn(go, ownerNp.connectionToClient);
                else if (netIdentity != null && netIdentity.connectionToClient != null)
                    NetworkServer.Spawn(go, netIdentity.connectionToClient);
                else
                    NetworkServer.Spawn(go);
            }
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
        if (spawned > 0)
        {
            AudioManager.Instance?.PlayUnitTrain();
        }

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
        currentBuildingHealth = syncHealth;

        if (NetworkServer.active && NetworkClient.active)
            RpcOnBuildingDamage(transform.position);
        else if (!NetworkServer.active)
        {
            EffectManager.Instance?.PlayBuildingHitEffect(transform.position);
            ScreenShake.Instance?.LightShake();
        }

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
        if (NetworkServer.active && NetworkClient.active)
            RpcOnBuildingDestroyed(transform.position);
        else if (!NetworkServer.active)
        {
            EffectManager.Instance?.PlayDeathEffect(transform.position);
            ScreenShake.Instance?.HeavyShake();
        }

        EnemyAI.Instance?.OnBuildingDestroyed(this);
        AllBuildings.Remove(this);
        if (GameOverManager.Instance != null)
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

    // ------------------------------------------------------------------------
    // AoE4 Building Type Identification & Action System
    // ------------------------------------------------------------------------

    public bool IsHomeSiteBuilding =>
        (!string.IsNullOrEmpty(gameObject.name) && gameObject.name.IndexOf("HomeSite", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || (!string.IsNullOrEmpty(buildingName) && buildingName.IndexOf("Home", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || this is HomeSite;

    public bool IsBarracksBuilding =>
        (!string.IsNullOrEmpty(gameObject.name) && gameObject.name.IndexOf("Barracks", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || (!string.IsNullOrEmpty(buildingName) && buildingName.IndexOf("Barracks", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || this is Barracks;

    public bool IsMarketBuilding =>
        (!string.IsNullOrEmpty(gameObject.name) && gameObject.name.IndexOf("Market", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || (!string.IsNullOrEmpty(buildingName) && buildingName.IndexOf("Market", System.StringComparison.OrdinalIgnoreCase) >= 0);

    public bool IsLumberMillBuilding =>
        (!string.IsNullOrEmpty(gameObject.name) && (gameObject.name.IndexOf("Lumber", System.StringComparison.OrdinalIgnoreCase) >= 0 || gameObject.name.IndexOf("Mill", System.StringComparison.OrdinalIgnoreCase) >= 0))
        || (!string.IsNullOrEmpty(buildingName) && (buildingName.IndexOf("Lumber", System.StringComparison.OrdinalIgnoreCase) >= 0 || buildingName.IndexOf("Mill", System.StringComparison.OrdinalIgnoreCase) >= 0));

    public bool IsFarmBuilding =>
        (!string.IsNullOrEmpty(gameObject.name) && gameObject.name.IndexOf("Farm", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || (!string.IsNullOrEmpty(buildingName) && buildingName.IndexOf("Farm", System.StringComparison.OrdinalIgnoreCase) >= 0);

    public bool IsTowerBuilding =>
        (!string.IsNullOrEmpty(gameObject.name) && gameObject.name.IndexOf("Tower", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || (!string.IsNullOrEmpty(buildingName) && buildingName.IndexOf("Tower", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || GetComponent<TowerAttack>() != null;

    public bool IsWallBuilding =>
        (!string.IsNullOrEmpty(gameObject.name) && gameObject.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
        || (!string.IsNullOrEmpty(buildingName) && buildingName.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0);

    public List<BuildingAction> GetAvailableActions()
    {
        var actions = new List<BuildingAction>();

        if (IsHomeSiteBuilding)
        {
            // 1. Train Villager
            actions.Add(new BuildingAction
            {
                id = "train_villager",
                title = "Train Villager",
                description = "Trains a worker to gather resources and construct buildings.",
                costFood = 50, costWood = 0, costGold = 0,
                isOneTimeTech = false,
                isAvailable = () => CanAfford(50, 0, 0),
                getDynamicLabel = () => "Train Villager\n(50 Food)",
                onExecute = () => TrainUnitAction(0)
            });

            // 2. Advance Age
            int fCost = CurrentAge == 1 ? 200 : (CurrentAge == 2 ? 400 : 800);
            int gCost = CurrentAge == 1 ? 100 : (CurrentAge == 2 ? 250 : 500);
            string nextAgeName = CurrentAge == 1 ? "Age II" : (CurrentAge == 2 ? "Age III" : "Age IV");
            actions.Add(new BuildingAction
            {
                id = "advance_age",
                title = $"Advance to {nextAgeName}",
                description = "Advances your civilization to the next Age, unlocking powerful upgrades and granting permanent stat buffs.",
                costFood = fCost, costWood = 0, costGold = gCost,
                isOneTimeTech = false,
                isAvailable = () => CurrentAge < 4 && CanAfford(CurrentAge == 1 ? 200 : (CurrentAge == 2 ? 400 : 800), 0, CurrentAge == 1 ? 100 : (CurrentAge == 2 ? 250 : 500)),
                getDynamicLabel = () =>
                {
                    if (CurrentAge >= 4) return "Imperial Age IV (Max Age)";
                    int f = CurrentAge == 1 ? 200 : (CurrentAge == 2 ? 400 : 800);
                    int g = CurrentAge == 1 ? 100 : (CurrentAge == 2 ? 250 : 500);
                    string nextName = CurrentAge == 1 ? "Age II Feudal" : (CurrentAge == 2 ? "Age III Castle" : "Age IV Imperial");
                    return $"Advance to {nextName}\n({f} Food, {g} Gold)";
                },
                onExecute = () => AdvanceAge()
            });

            // 3. Research Wheelbarrow
            actions.Add(new BuildingAction
            {
                id = "tech_wheelbarrow",
                title = "Wheelbarrow Tech",
                description = "Permanently boosts all friendly Villagers' move speed by +25% and carry capacity by +10.",
                costFood = 0, costWood = 75, costGold = 50,
                isOneTimeTech = true,
                isResearched = TechWheelbarrowResearched,
                isAvailable = () => !TechWheelbarrowResearched && CanAfford(0, 75, 50),
                getDynamicLabel = () => TechWheelbarrowResearched ? "Wheelbarrow Researched [Active]" : "Wheelbarrow Tech\n(75 Wood, 50 Gold)",
                onExecute = () => ResearchWheelbarrow()
            });

            // 4. Town Bell / Garrison Heal
            actions.Add(new BuildingAction
            {
                id = "town_bell",
                title = "Town Bell Heal",
                description = "Heals all friendly units within 20m for +50 HP and repairs Town Center by +200 HP.",
                costFood = 50, costWood = 0, costGold = 0,
                isOneTimeTech = false,
                isAvailable = () => townBellCooldownTimer <= 0f && CanAfford(50, 0, 0),
                getDynamicLabel = () => townBellCooldownTimer > 0f ? $"Town Bell ({townBellCooldownTimer:F0}s)" : "Town Bell Heal\n(50 Food)",
                onExecute = () => RingTownBell()
            });
        }
        else if (IsBarracksBuilding)
        {
            // 1. Train Man-at-Arms (Infantry)
            actions.Add(new BuildingAction
            {
                id = "train_infantry",
                title = "Train Man-at-Arms",
                description = "Armored frontline melee infantry.",
                costFood = 60, costWood = 0, costGold = 20,
                isOneTimeTech = false,
                isAvailable = () => CanAfford(60, 0, 20),
                getDynamicLabel = () => "Train Man-at-Arms\n(60 Food, 20 Gold)",
                onExecute = () => TrainUnitAction(0)
            });

            // 2. Train Royal Knight (Cavalry)
            actions.Add(new BuildingAction
            {
                id = "train_cavalry",
                title = "Train Royal Knight",
                description = "Fast, heavily armored shock cavalry with high damage.",
                costFood = 100, costWood = 0, costGold = 60,
                isOneTimeTech = false,
                isAvailable = () => CanAfford(100, 0, 60),
                getDynamicLabel = () => "Train Royal Knight\n(100 Food, 60 Gold)",
                onExecute = () => TrainUnitAction(1)
            });

            // 3. Damascus Steel Weapons
            actions.Add(new BuildingAction
            {
                id = "tech_damascus_steel",
                title = "Damascus Steel (+6 Atk)",
                description = "Permanently increases Attack Damage of all existing and future Infantry/Cavalry by +6.",
                costFood = 100, costWood = 0, costGold = 75,
                isOneTimeTech = true,
                isResearched = TechDamascusSteelResearched,
                isAvailable = () => !TechDamascusSteelResearched && CanAfford(100, 0, 75),
                getDynamicLabel = () => TechDamascusSteelResearched ? "Damascus Steel Researched (+6 Atk)" : "Damascus Steel (+6 Atk)\n(100 Food, 75 Gold)",
                onExecute = () => ResearchDamascusSteel()
            });

            // 4. Plate Mail Armor
            actions.Add(new BuildingAction
            {
                id = "tech_plate_mail",
                title = "Plate Mail (+45 HP)",
                description = "Permanently increases Max HP of all friendly Infantry/Cavalry by +45.",
                costFood = 0, costWood = 100, costGold = 75,
                isOneTimeTech = true,
                isResearched = TechPlateMailResearched,
                isAvailable = () => !TechPlateMailResearched && CanAfford(0, 100, 75),
                getDynamicLabel = () => TechPlateMailResearched ? "Plate Mail Researched (+45 HP)" : "Plate Mail (+45 HP)\n(100 Wood, 75 Gold)",
                onExecute = () => ResearchPlateMail()
            });
        }
        else if (IsMarketBuilding)
        {
            // Sell 100 Food -> 80 Gold
            actions.Add(new BuildingAction
            {
                id = "market_sell_food",
                title = "Sell 100 Food -> 80 Gold",
                description = "Instant market trade.",
                costFood = 100, costWood = 0, costGold = 0,
                isAvailable = () => CanAfford(100, 0, 0),
                getDynamicLabel = () => "Sell 100 Food -> 80 Gold",
                onExecute = () => TradeMarket(100, 0, 0, 0, 0, 80)
            });

            // Buy 100 Food <- 120 Gold
            actions.Add(new BuildingAction
            {
                id = "market_buy_food",
                title = "Buy 100 Food <- 120 Gold",
                description = "Instant market trade.",
                costFood = 0, costWood = 0, costGold = 120,
                isAvailable = () => CanAfford(0, 0, 120),
                getDynamicLabel = () => "Buy 100 Food <- 120 Gold",
                onExecute = () => TradeMarket(0, 0, 120, 100, 0, 0)
            });

            // Sell 100 Wood -> 80 Gold
            actions.Add(new BuildingAction
            {
                id = "market_sell_wood",
                title = "Sell 100 Wood -> 80 Gold",
                description = "Instant market trade.",
                costFood = 0, costWood = 100, costGold = 0,
                isAvailable = () => CanAfford(0, 100, 0),
                getDynamicLabel = () => "Sell 100 Wood -> 80 Gold",
                onExecute = () => TradeMarket(0, 100, 0, 0, 0, 80)
            });

            // Buy 100 Wood <- 120 Gold
            actions.Add(new BuildingAction
            {
                id = "market_buy_wood",
                title = "Buy 100 Wood <- 120 Gold",
                description = "Instant market trade.",
                costFood = 0, costWood = 0, costGold = 120,
                isAvailable = () => CanAfford(0, 0, 120),
                getDynamicLabel = () => "Buy 100 Wood <- 120 Gold",
                onExecute = () => TradeMarket(0, 0, 120, 0, 100, 0)
            });

            // Research Merchant Guilds
            actions.Add(new BuildingAction
            {
                id = "tech_merchant_guilds",
                title = "Merchant Guilds (+Gold)",
                description = "Activates passive +12 Gold every 5s from this Market.",
                costFood = 0, costWood = 100, costGold = 100,
                isOneTimeTech = true,
                isResearched = hasMerchantGuilds,
                isAvailable = () => !hasMerchantGuilds && CanAfford(0, 100, 100),
                getDynamicLabel = () => hasMerchantGuilds ? "Merchant Guilds Active (+12g/5s)" : "Merchant Guilds (+Gold)\n(100 Wood, 100 Gold)",
                onExecute = () => ResearchMerchantGuilds()
            });
        }
        else if (IsLumberMillBuilding)
        {
            // 1. Double-Broadaxe
            actions.Add(new BuildingAction
            {
                id = "tech_double_broadaxe",
                title = "Double-Broadaxe (+50%)",
                description = "Increases passive and villager wood yield by +50%.",
                costFood = 75, costWood = 0, costGold = 50,
                isOneTimeTech = true,
                isResearched = TechDoubleBroadaxeResearched,
                isAvailable = () => !TechDoubleBroadaxeResearched && CanAfford(75, 0, 50),
                getDynamicLabel = () => TechDoubleBroadaxeResearched ? "Double-Broadaxe Active (+50% Wood)" : "Double-Broadaxe (+50%)\n(75 Food, 50 Gold)",
                onExecute = () => ResearchDoubleBroadaxe()
            });

            // 2. Forestry
            actions.Add(new BuildingAction
            {
                id = "tech_forestry",
                title = "Forestry (+Speed/HP)",
                description = "Increases Lumber Mill production speed and adds +300 Max HP.",
                costFood = 0, costWood = 75, costGold = 50,
                isOneTimeTech = true,
                isResearched = TechForestryResearched,
                isAvailable = () => !TechForestryResearched && CanAfford(0, 75, 50),
                getDynamicLabel = () => TechForestryResearched ? "Forestry Researched (+300 HP)" : "Forestry (+Speed/HP)\n(75 Wood, 50 Gold)",
                onExecute = () => ResearchForestry()
            });

            // 3. Emergency Timber Delivery
            actions.Add(new BuildingAction
            {
                id = "emergency_timber",
                title = "Emergency Timber Delivery",
                description = "Trade 50 Food to immediately receive 100 Wood.",
                costFood = 50, costWood = 0, costGold = 0,
                isAvailable = () => CanAfford(50, 0, 0),
                getDynamicLabel = () => "Emergency Timber\n(50 Food -> 100 Wood)",
                onExecute = () => EmergencyTimber()
            });
        }
        else if (IsFarmBuilding)
        {
            // 1. Horticulture
            actions.Add(new BuildingAction
            {
                id = "tech_horticulture",
                title = "Horticulture (+50% Food)",
                description = "Increases Farm food output by +50% (+15 Food per cycle).",
                costFood = 0, costWood = 75, costGold = 50,
                isOneTimeTech = true,
                isResearched = TechHorticultureResearched,
                isAvailable = () => !TechHorticultureResearched && CanAfford(0, 75, 50),
                getDynamicLabel = () => TechHorticultureResearched ? "Horticulture Active (+15 Food/tick)" : "Horticulture (+50% Food)\n(75 Wood, 50 Gold)",
                onExecute = () => ResearchHorticulture()
            });

            // 2. Bountiful Harvest
            actions.Add(new BuildingAction
            {
                id = "bountiful_harvest",
                title = "Bountiful Harvest",
                description = "Trade 40 Gold to immediately harvest 120 Food.",
                costFood = 0, costWood = 0, costGold = 40,
                isAvailable = () => CanAfford(0, 0, 40),
                getDynamicLabel = () => "Bountiful Harvest\n(40 Gold -> 120 Food)",
                onExecute = () => BountifulHarvest()
            });
        }
        else if (IsTowerBuilding)
        {
            // 1. Arrow Slits
            actions.Add(new BuildingAction
            {
                id = "tech_arrow_slits",
                title = "Arrow Slits (+10 Dmg)",
                description = "Boosts this Tower's attack damage by +10 and range by +4m.",
                costFood = 0, costWood = 100, costGold = 75,
                isOneTimeTech = true,
                isResearched = hasArrowSlits,
                isAvailable = () => !hasArrowSlits && CanAfford(0, 100, 75),
                getDynamicLabel = () => hasArrowSlits ? "Arrow Slits Active (+10 Atk, +4m)" : "Arrow Slits (+10 Dmg)\n(100 Wood, 75 Gold)",
                onExecute = () => ResearchArrowSlits()
            });

            // 2. Boiling Oil & Stonework
            actions.Add(new BuildingAction
            {
                id = "tech_boiling_oil",
                title = "Boiling Oil (+400 HP)",
                description = "Increases Tower Max HP by +400, heals to full, and increases fire rate.",
                costFood = 0, costWood = 125, costGold = 50,
                isOneTimeTech = true,
                isResearched = hasBoilingOil,
                isAvailable = () => !hasBoilingOil && CanAfford(0, 125, 50),
                getDynamicLabel = () => hasBoilingOil ? "Boiling Oil Active (+400 HP, Faster)" : "Boiling Oil (+400 HP)\n(125 Wood, 50 Gold)",
                onExecute = () => ResearchBoilingOil()
            });
        }
        else if (IsWallBuilding)
        {
            // 1. Fortify Wall
            actions.Add(new BuildingAction
            {
                id = "fortify_ramparts",
                title = "Fortify Wall (+250 HP)",
                description = "Repairs Wall to 100% HP and adds +250 Max HP.",
                costFood = 0, costWood = 30, costGold = 0,
                isAvailable = () => CanAfford(0, 30, 0),
                getDynamicLabel = () => "Fortify Wall (+250 HP)\n(30 Wood -> +250 HP)",
                onExecute = () => FortifyWall()
            });

            // 2. Toggle Gate (Open/Close)
            actions.Add(new BuildingAction
            {
                id = "toggle_gatehouse",
                title = "Toggle Gate (Open/Close)",
                description = "Opens or closes gate passage for friendly unit movement.",
                costFood = 0, costWood = 0, costGold = 0,
                isAvailable = () => true,
                getDynamicLabel = () => isGateOpen ? "Gate: OPEN (Click to Close)" : "Gate: CLOSED (Click to Open)",
                onExecute = () => ToggleGate()
            });
        }

        return actions;
    }

    public void TrainUnitAction(int index)
    {
        if (NetworkedPlayer.LocalInstance != null && netId != 0)
        {
            NetworkedPlayer.LocalInstance.CmdTrainUnit(netId, index);
        }
        else
        {
            SpawnUnit(index);
        }
    }

    public void AdvanceAge()
    {
        if (CurrentAge >= 4) return;
        int foodCost = CurrentAge == 1 ? 200 : (CurrentAge == 2 ? 400 : 800);
        int goldCost = CurrentAge == 1 ? 100 : (CurrentAge == 2 ? 250 : 500);

        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(foodCost, 0, goldCost))
        {
            CurrentAge++;

            // Sync with AgeManager via reflection or static property if present
            var ageType = System.Type.GetType("AgeManager") ?? System.Type.GetType("AgeManager, Assembly-CSharp");
            if (ageType != null)
            {
                var prop = ageType.GetProperty("CurrentAge", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null && prop.CanWrite) prop.SetValue(null, CurrentAge);
                var field = ageType.GetField("CurrentAge", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (field != null) field.SetValue(null, CurrentAge);
                var instProp = ageType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var inst = instProp?.GetValue(null);
                if (inst != null)
                {
                    var setAgeM = ageType.GetMethod("SetAge", new System.Type[] { typeof(int) });
                    setAgeM?.Invoke(inst, new object[] { CurrentAge });
                }
            }

            // Buff all player buildings: +250 Max HP and heal +250
            foreach (var b in AllBuildings)
            {
                if (b != null && b.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                {
                    b.maxBuildingHealth += 250;
                    b.currentBuildingHealth = Mathf.Min(b.maxBuildingHealth, b.currentBuildingHealth + 250);
                }
            }

            // Buff all player units: +4 Attack and +25 Max HP
            Unit[] allUnits = Object.FindObjectsOfType<Unit>();
            foreach (var u in allUnits)
            {
                if (u != null && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                {
                    ApplyUnitBuff(u, 4, 25);
                }
            }

            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayBuildCompleteEffect(transform.position);
            ScreenShake.Instance?.LightShake();
            string roman = GetAgeRoman(CurrentAge);
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2.5f, $"ADVANCED TO AGE {roman}!", Color.cyan);
            BuildingInfoUI.Instance?.ShowNotification($"Advanced to Age {roman}!");
            ResourceBuildingUI.Instance?.ShowNotification($"Advanced to Age {roman}!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Not enough resources to advance Age!");
            ResourceBuildingUI.Instance?.ShowNotification("Not enough resources to advance Age!");
        }
    }

    public void ResearchWheelbarrow()
    {
        if (TechWheelbarrowResearched) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 75, 50))
        {
            TechWheelbarrowResearched = true;
            VillagerSpeedMultiplier = 1.25f;
            VillagerCarryBonus = 10;

            Villager[] allVillagers = Object.FindObjectsOfType<Villager>();
            foreach (var v in allVillagers)
            {
                if (v != null && v.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                    ApplyWheelbarrowToVillager(v);
            }

            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+25% Speed & +10 Carry Researched!", Color.green);
            BuildingInfoUI.Instance?.ShowNotification("Wheelbarrow Researched: Villagers move 25% faster!");
            ResourceBuildingUI.Instance?.ShowNotification("Wheelbarrow Researched: Villagers move 25% faster!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 75 Wood, 50 Gold!");
        }
    }

    public void RingTownBell()
    {
        if (townBellCooldownTimer > 0f) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(50, 0, 0))
        {
            townBellCooldownTimer = 20f;
            currentBuildingHealth = Mathf.Min(maxBuildingHealth, currentBuildingHealth + 200);

            Unit[] allUnits = Object.FindObjectsOfType<Unit>();
            foreach (var u in allUnits)
            {
                if (u != null && !u.IsDead && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                {
                    if (Vector3.Distance(transform.position, u.transform.position) <= 20f)
                    {
                        var hpField = typeof(Unit).GetField("currentHealth", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (hpField != null)
                        {
                            int cur = (int)hpField.GetValue(u);
                            hpField.SetValue(u, Mathf.Min(u.MaxHealth, cur + 50));
                        }
                        EffectManager.Instance?.PlayHitEffect(u.transform.position + Vector3.up * 0.5f);
                    }
                }
            }

            AudioManager.Instance?.PlayWaveHorn();
            EffectManager.Instance?.PlayBuildingHitEffect(transform.position);
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "TOWN BELL: +50 HP HEAL!", Color.green);
            BuildingInfoUI.Instance?.ShowNotification("Town Bell rung! Garrison healed.");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Town Bell requires 50 Food!");
        }
    }

    public void ResearchDamascusSteel()
    {
        if (TechDamascusSteelResearched) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(100, 0, 75))
        {
            TechDamascusSteelResearched = true;
            BonusMilitaryDamage += 6;

            Unit[] allUnits = Object.FindObjectsOfType<Unit>();
            foreach (var u in allUnits)
            {
                if (u != null && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex && (u is Infantry || u is Cavalry))
                    ApplyUnitBuff(u, 6, 0);
            }

            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+6 Military Attack Researched!", Color.red);
            BuildingInfoUI.Instance?.ShowNotification("Damascus Steel Weapons: +6 Attack to all military!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 100 Food, 75 Gold!");
        }
    }

    public void ResearchPlateMail()
    {
        if (TechPlateMailResearched) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 100, 75))
        {
            TechPlateMailResearched = true;
            BonusMilitaryHP += 45;

            Unit[] allUnits = Object.FindObjectsOfType<Unit>();
            foreach (var u in allUnits)
            {
                if (u != null && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex && (u is Infantry || u is Cavalry))
                    ApplyUnitBuff(u, 0, 45);
            }

            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+45 Military HP Researched!", Color.cyan);
            BuildingInfoUI.Instance?.ShowNotification("Plate Mail Armor: +45 HP to all military!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 100 Wood, 75 Gold!");
        }
    }

    public void TradeMarket(int spendFood, int spendWood, int spendGold, int gainFood, int gainWood, int gainGold)
    {
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(spendFood, spendWood, spendGold))
        {
            ResourceManager.Instance.AddResources(gainFood, gainWood, gainGold);
            AudioManager.Instance?.PlayResourceGather();
            string msg = "";
            if (gainFood > 0) msg = $"+{gainFood} Food";
            if (gainWood > 0) msg = $"+{gainWood} Wood";
            if (gainGold > 0) msg = $"+{gainGold} Gold";
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, msg, Color.yellow);
            BuildingInfoUI.Instance?.ShowNotification($"Market Trade: {msg}");
            ResourceBuildingUI.Instance?.ShowNotification($"Market Trade: {msg}");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Insufficient resources for trade!");
            ResourceBuildingUI.Instance?.ShowNotification("Insufficient resources for trade!");
        }
    }

    public void ResearchMerchantGuilds()
    {
        if (hasMerchantGuilds) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 100, 100))
        {
            hasMerchantGuilds = true;
            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+12 Gold / 5s Active!", Color.yellow);
            BuildingInfoUI.Instance?.ShowNotification("Merchant Guilds: Generating +12 Gold every 5s!");
            ResourceBuildingUI.Instance?.ShowNotification("Merchant Guilds: Generating +12 Gold every 5s!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 100 Wood, 100 Gold!");
            ResourceBuildingUI.Instance?.ShowNotification("Requires 100 Wood, 100 Gold!");
        }
    }

    public void ResearchDoubleBroadaxe()
    {
        if (TechDoubleBroadaxeResearched) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(75, 0, 50))
        {
            TechDoubleBroadaxeResearched = true;
            WoodYieldMultiplier = 1.5f;

            var rb = GetComponent<ResourceBuilding>();
            if (rb != null)
            {
                var fld = typeof(ResourceBuilding).GetField("amountPerTick", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fld != null)
                {
                    int cur = (int)fld.GetValue(rb);
                    fld.SetValue(rb, Mathf.RoundToInt(cur * 1.5f));
                }
            }

            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+50% Wood Yield Researched!", Color.green);
            BuildingInfoUI.Instance?.ShowNotification("Double-Broadaxe: Wood yields increased by +50%!");
            ResourceBuildingUI.Instance?.ShowNotification("Double-Broadaxe: Wood yields increased by +50%!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 75 Food, 50 Gold!");
            ResourceBuildingUI.Instance?.ShowNotification("Requires 75 Food, 50 Gold!");
        }
    }

    public void ResearchForestry()
    {
        if (TechForestryResearched) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 75, 50))
        {
            TechForestryResearched = true;
            maxBuildingHealth += 300;
            currentBuildingHealth = Mathf.Min(maxBuildingHealth, currentBuildingHealth + 300);

            var rb = GetComponent<ResourceBuilding>();
            if (rb != null)
            {
                var fld = typeof(ResourceBuilding).GetField("tickInterval", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fld != null)
                {
                    float cur = (float)fld.GetValue(rb);
                    fld.SetValue(rb, Mathf.Max(1.5f, cur * 0.65f));
                }
            }

            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+300 HP & Faster Wood Delivery!", Color.green);
            BuildingInfoUI.Instance?.ShowNotification("Forestry Researched: Faster timber production & +300 HP!");
            ResourceBuildingUI.Instance?.ShowNotification("Forestry Researched: Faster timber production & +300 HP!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 75 Wood, 50 Gold!");
            ResourceBuildingUI.Instance?.ShowNotification("Requires 75 Wood, 50 Gold!");
        }
    }

    public void EmergencyTimber()
    {
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(50, 0, 0))
        {
            ResourceManager.Instance.AddResources(0, 100, 0);
            AudioManager.Instance?.PlayResourceGather();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+100 Wood Delivered!", Color.green);
            BuildingInfoUI.Instance?.ShowNotification("Emergency Timber: -50 Food  +100 Wood");
            ResourceBuildingUI.Instance?.ShowNotification("Emergency Timber: -50 Food  +100 Wood");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 50 Food!");
            ResourceBuildingUI.Instance?.ShowNotification("Requires 50 Food!");
        }
    }

    public void ResearchHorticulture()
    {
        if (TechHorticultureResearched) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 75, 50))
        {
            TechHorticultureResearched = true;
            var rb = GetComponent<ResourceBuilding>();
            if (rb != null)
            {
                var fld = typeof(ResourceBuilding).GetField("amountPerTick", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fld != null)
                {
                    int cur = (int)fld.GetValue(rb);
                    fld.SetValue(rb, cur + 15);
                }
            }

            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+15 Food Per Cycle Researched!", Color.yellow);
            BuildingInfoUI.Instance?.ShowNotification("Horticulture: Farms produce +15 extra Food per cycle!");
            ResourceBuildingUI.Instance?.ShowNotification("Horticulture: Farms produce +15 extra Food per cycle!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 75 Wood, 50 Gold!");
            ResourceBuildingUI.Instance?.ShowNotification("Requires 75 Wood, 50 Gold!");
        }
    }

    public void BountifulHarvest()
    {
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 0, 40))
        {
            ResourceManager.Instance.AddResources(120, 0, 0);
            AudioManager.Instance?.PlayResourceGather();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+120 Food Harvested!", Color.yellow);
            BuildingInfoUI.Instance?.ShowNotification("Bountiful Harvest: -40 Gold  +120 Food");
            ResourceBuildingUI.Instance?.ShowNotification("Bountiful Harvest: -40 Gold  +120 Food");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 40 Gold!");
            ResourceBuildingUI.Instance?.ShowNotification("Requires 40 Gold!");
        }
    }

    public void ResearchArrowSlits()
    {
        if (hasArrowSlits) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 100, 75))
        {
            hasArrowSlits = true;
            TowerAttack ta = GetComponent<TowerAttack>();
            if (ta != null)
            {
                ta.attackDamage += 10;
                ta.attackRange += 4f;
            }
            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+10 Atk & +4m Range!", Color.red);
            BuildingInfoUI.Instance?.ShowNotification("Arrow Slits Researched: Watchtower damage & range boosted!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 100 Wood, 75 Gold!");
        }
    }

    public void ResearchBoilingOil()
    {
        if (hasBoilingOil) return;
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 125, 50))
        {
            hasBoilingOil = true;
            maxBuildingHealth += 400;
            currentBuildingHealth = maxBuildingHealth;
            TowerAttack ta = GetComponent<TowerAttack>();
            if (ta != null)
            {
                ta.attackCooldown = Mathf.Max(0.5f, ta.attackCooldown * 0.7f);
            }
            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+400 HP & Faster Attack Rate!", Color.yellow);
            BuildingInfoUI.Instance?.ShowNotification("Boiling Oil & Stonework: +400 HP and increased fire rate!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 125 Wood, 50 Gold!");
        }
    }

    public void FortifyWall()
    {
        if (ResourceManager.Instance != null && ResourceManager.Instance.TrySpend(0, 30, 0))
        {
            maxBuildingHealth += 250;
            currentBuildingHealth = maxBuildingHealth;
            AudioManager.Instance?.PlayBuildingComplete();
            EffectManager.Instance?.PlayBuildingHitEffect(transform.position);
            EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, "+250 Max HP Fortified!", Color.white);
            BuildingInfoUI.Instance?.ShowNotification("Wall Fortified: Fully repaired and +250 Max HP!");
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
            BuildingInfoUI.Instance?.ShowNotification("Requires 30 Wood!");
        }
    }

    public void ToggleGate()
    {
        isGateOpen = !isGateOpen;
        var obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle == null) obstacle = GetComponentInChildren<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.enabled = !isGateOpen;
        }

        var cols = GetComponentsInChildren<Collider>();
        foreach (var c in cols)
        {
            c.isTrigger = isGateOpen;
        }

        AudioManager.Instance?.PlayUIClick();
        EffectManager.Instance?.PlayFloatingText(transform.position + Vector3.up * 2f, isGateOpen ? "Gatehouse OPEN" : "Gatehouse CLOSED", isGateOpen ? Color.green : Color.red);
        BuildingInfoUI.Instance?.ShowNotification(isGateOpen ? "Gatehouse opened for friendly passage." : "Gatehouse closed and fortified.");
    }

    public static void ApplyUnitBuff(Unit u, int addAttack, int addMaxHp)
    {
        if (u == null) return;
        var t = typeof(Unit);
        if (addAttack > 0)
        {
            var atkField = t.GetField("attackDamage", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (atkField != null)
            {
                int curAtk = (int)atkField.GetValue(u);
                atkField.SetValue(u, curAtk + addAttack);
            }
        }
        if (addMaxHp > 0)
        {
            var hpField = t.GetField("maxHealth", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (hpField != null)
            {
                int curHp = (int)hpField.GetValue(u);
                hpField.SetValue(u, curHp + addMaxHp);
            }
            var curHpField = t.GetField("currentHealth", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (curHpField != null)
            {
                int cur = (int)curHpField.GetValue(u);
                curHpField.SetValue(u, cur + addMaxHp);
            }
        }
    }

    public static void ApplyWheelbarrowToVillager(Villager v)
    {
        if (v == null) return;
        var t = typeof(Unit);
        var spdField = t.GetField("baseSpeed", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (spdField != null)
        {
            float curSpd = (float)spdField.GetValue(v);
            spdField.SetValue(v, curSpd * 1.25f);
        }
        var nav = v.GetComponent<NavMeshAgent>();
        if (nav != null) nav.speed *= 1.25f;

        var vt = typeof(Villager);
        var amtField = vt.GetField("gatherAmount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (amtField != null)
        {
            int curAmt = (int)amtField.GetValue(v);
            amtField.SetValue(v, curAmt + 10);
        }
    }

    public static bool CanAfford(int food, int wood, int gold)
    {
        if (ResourceManager.Instance == null) return true;
        return ResourceManager.Instance.Food >= food
            && ResourceManager.Instance.Wood >= wood
            && ResourceManager.Instance.Gold >= gold;
    }

    public static string GetAgeRoman(int age)
    {
        switch (age)
        {
            case 1: return "I (Dark Age)";
            case 2: return "II (Feudal Age)";
            case 3: return "III (Castle Age)";
            case 4: return "IV (Imperial Age)";
            default: return age.ToString();
        }
    }
}
