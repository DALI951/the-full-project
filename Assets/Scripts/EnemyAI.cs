using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using System.Linq;

public class EnemyAI : MonoBehaviour
{
    public static EnemyAI Instance { get; private set; }

    [Header("Enemy Identity")]
    [SerializeField] public int enemyPlayerId = 8;

    [Header("Starting Resources")]
    [SerializeField] private int startFood = 400;
    [SerializeField] private int startWood = 300;
    [SerializeField] private int startGold = 200;

    [Header("Population")]
    [SerializeField] private int maxPopulation = 20;
    private int currentPopulation = 0;

    [Header("AI Timers (seconds)")]
    [SerializeField] private float economyInterval = 8f;
    [SerializeField] private float militaryInterval = 12f;
    [SerializeField] private float attackInterval = 60f;
    [SerializeField] private float defenseInterval = 15f;
    [SerializeField] private float gatherInterval = 8f;
    [SerializeField] private float scoutInterval = 30f;

    [Header("Attack Thresholds")]
    [SerializeField] private int minAttackForce = 4;
    [SerializeField] private int maxAttackForce = 15;

    [Header("Building Blueprint")]
    [SerializeField] private GameObject towerPrefab;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject farmPrefab;
    [SerializeField] private GameObject lumberMillPrefab;
    [SerializeField] private GameObject marketPrefab;
    [SerializeField] private GameObject constructionSitePrefab;

    [Header("Adaptive Difficulty")]
    private int gamesPlayed = 0;
    private int difficultyLevel = 0;

    [Header("AI State")]
    public AIStrategy currentStrategy = AIStrategy.Economy;
    private float economyTimer, militaryTimer, attackTimer, defenseTimer, gatherTimer, scoutTimer;
    private Vector3 lastScoutPosition;
    private int waveCount = 0;
    private bool hasScouted = false;
    private bool isUnderAttack = false;
    private float underAttackTimer = 0f;
    private HashSet<Vector3> builtPositions = new HashSet<Vector3>();

    private int food, wood, gold;
    private int ecoPriority = 0;
    private float gameTime = 0f;

    public enum AIStrategy { Economy, MilitaryBuildup, Attacking, Defending, Desperate }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        if (!NetworkServer.active) { enabled = false; return; }
        if (NetworkServer.connections.Count >= 2) { enabled = false; return; }

        gamesPlayed = PlayerPrefs.GetInt("EnemyGamesPlayed", 0);
        difficultyLevel = Mathf.Min(gamesPlayed, 10);
        attackInterval = Mathf.Max(30f, attackInterval - difficultyLevel * 3f);
        economyInterval = Mathf.Max(5f, economyInterval - difficultyLevel * 0.3f);
        militaryInterval = Mathf.Max(8f, militaryInterval - difficultyLevel * 0.4f);

        startFood += difficultyLevel * 40;
        startWood += difficultyLevel * 25;
        startGold += difficultyLevel * 15;
        maxPopulation += difficultyLevel * 5;

        food = startFood; wood = startWood; gold = startGold;
        Invoke(nameof(InitialGatherOrders), 2f);
        Invoke(nameof(InitialScout), 30f);
    }

    private void Update()
    {
        gameTime += Time.deltaTime;

        economyTimer += Time.deltaTime;
        militaryTimer += Time.deltaTime;
        attackTimer += Time.deltaTime;
        defenseTimer += Time.deltaTime;
        gatherTimer += Time.deltaTime;
        scoutTimer += Time.deltaTime;

        EvaluateStrategy();

        if (economyTimer >= economyInterval) { economyTimer = 0f; RunEconomy(); }
        if (militaryTimer >= militaryInterval) { militaryTimer = 0f; RunMilitaryProduction(); }
        if (attackTimer >= attackInterval) { attackTimer = 0f; SendAttackWave(); }
        if (defenseTimer >= defenseInterval) { defenseTimer = 0f; RunDefense(); }
        if (gatherTimer >= gatherInterval) { gatherTimer = 0f; CheckGatherers(); }
        if (scoutTimer >= scoutInterval) { scoutTimer = 0f; RunScouting(); }

        if (isUnderAttack)
        {
            underAttackTimer -= Time.deltaTime;
            if (underAttackTimer <= 0f) isUnderAttack = false;
        }
    }

    private void EvaluateStrategy()
    {
        int militaryCount = CountEnemyMilitary();
        int villagerCount = CountEnemyVillagers();
        float resourceRatio = (food + wood + gold) / Mathf.Max(1, startFood + startWood + startGold);

        if (currentPopulation >= maxPopulation * 0.9f)
            currentStrategy = AIStrategy.Desperate;
        else if (isUnderAttack && militaryCount < 3)
            currentStrategy = AIStrategy.Defending;
        else if (attackTimer >= attackInterval * 0.8f && militaryCount >= minAttackForce)
            currentStrategy = AIStrategy.Attacking;
        else if (villagerCount < 6 || resourceRatio < 0.3f)
            currentStrategy = AIStrategy.Economy;
        else
            currentStrategy = AIStrategy.MilitaryBuildup;
    }

    public void ReportUnderAttack()
    {
        isUnderAttack = true;
        underAttackTimer = 20f;
    }

    public void AddEnemyResources(int f, int w, int g)
    {
        food = Mathf.Max(0, food + f);
        wood = Mathf.Max(0, wood + w);
        gold = Mathf.Max(0, gold + g);
    }

    public bool TrySpend(int costFood, int costWood, int costGold)
    {
        if (food < costFood || wood < costWood || gold < costGold) return false;
        food -= costFood; wood -= costWood; gold -= costGold;
        return true;
    }

    public bool CanAddPopulation(int amount = 1) => currentPopulation + amount <= maxPopulation;
    public void AddPopulation(int amount = 1) => currentPopulation = Mathf.Clamp(currentPopulation + amount, 0, maxPopulation);
    public void RemovePopulation(int amount = 1) => currentPopulation = Mathf.Max(0, currentPopulation - amount);
    public int CurrentPopulation => currentPopulation;
    public int MaxPopulation => maxPopulation;

    // ════════════════════════════════════════════════════════════════════
    // ECONOMY
    // ════════════════════════════════════════════════════════════════════

    private void RunEconomy()
    {
        int vCount = CountEnemyVillagers();
        float resourceBalance = (float)(wood + gold) / Mathf.Max(1, food);

        if (vCount < 6 || resourceBalance < 1.5f)
        {
            TrainFromBuilding<HomeSite>(0);
        }

        if (wood > 100 && gold > 50 && vCount >= 4)
        {
            TryBuildEconomyBuilding();
        }

        if (food > 150 && wood > 100 && CountEnemyBuildings<Barracks>() == 0)
        {
            TryBuildBarracks();
        }

        AssignIdleVillagersToResourceBuildings();
    }

    private void TryBuildEconomyBuilding()
    {
        if (ResourceNode.FindNearest(transform.position, ResourceType.Food, null, 30f) != null
            && CountEnemyBuildingsOfPrefab(farmPrefab) < 2)
        {
            TryPlaceBuilding(farmPrefab);
        }
        else if (ResourceNode.FindNearest(transform.position, ResourceType.Wood, null, 30f) != null
                 && CountEnemyBuildingsOfPrefab(lumberMillPrefab) < 1)
        {
            TryPlaceBuilding(lumberMillPrefab);
        }
        else if (CountEnemyBuildingsOfPrefab(marketPrefab) < 1)
        {
            TryPlaceBuilding(marketPrefab);
        }
    }

    private void TryBuildBarracks()
    {
        if (barracksCost <= wood + 50)
            TryPlaceBuilding(barracksPrefab);
    }

    [Header("Building Prefabs")]
    [SerializeField] private GameObject barracksPrefab;
    [SerializeField] private int barracksCost = 150;
    [SerializeField] private int towerCost = 100;
    private const int BUILDING_SPACING = 8;

    private void TryPlaceBuilding(GameObject prefab)
    {
        if (prefab == null) return;
        Vector3 basePos = GetEnemyBasePosition();
        if (basePos == Vector3.zero) return;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            Vector2 rand = Random.insideUnitCircle * (10f + attempt * 3f);
            Vector3 pos = basePos + new Vector3(rand.x, 0f, rand.y);
            pos = ClampToMap(pos);

            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                pos = hit.position;

            if (IsPositionClearForBuilding(pos) && !builtPositions.Contains(pos))
            {
                int cost = GetBuildingCost(prefab);
                if (!TrySpend(cost, cost, cost / 2)) continue;

                AssignBuilderToBuild(pos, prefab);
                builtPositions.Add(pos);
                break;
            }
        }
    }

    private bool IsPositionClearForBuilding(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, 3f);
        foreach (Collider c in hits)
        {
            if (c.GetComponentInParent<Building>() != null) return false;
            if (c.GetComponentInParent<ConstructionSite>() != null) return false;
            if (c.GetComponentInParent<ResourceNode>() != null) return false;
        }
        return true;
    }

    private int GetBuildingCost(GameObject prefab)
    {
        if (prefab == farmPrefab || prefab == lumberMillPrefab || prefab == marketPrefab) return 75;
        if (prefab == towerPrefab) return towerCost;
        if (prefab == wallPrefab) return 50;
        return 100;
    }

    private void AssignBuilderToBuild(Vector3 pos, GameObject prefab)
    {
        if (constructionSitePrefab == null) return;
        Villager builder = FindIdleVillager();
        if (builder == null) return;
        Vector3 clampedPos = ClampToMap(pos);
        GameObject siteGO = Object.Instantiate(constructionSitePrefab, clampedPos, Quaternion.identity);
        ConstructionSite site = siteGO.GetComponent<ConstructionSite>();
        if (site == null) { Object.Destroy(siteGO); return; }
        int cost = GetBuildingCost(prefab);
        site.Initialize(prefab, 10f, cost, cost, cost / 2);
        site.SetOwnerOnServer(enemyPlayerId);
        NetworkServer.Spawn(siteGO);
        builder.BuildAt(site);
    }

    // ════════════════════════════════════════════════════════════════════
    // MILITARY PRODUCTION
    // ════════════════════════════════════════════════════════════════════

    private void RunMilitaryProduction()
    {
        int militaryCount = CountEnemyMilitary();
        int villagerCount = CountEnemyVillagers();
        float timePhase = gameTime / 60f;

        if (villagerCount < 4 && militaryCount > 3)
        {
            TrainFromBuilding<HomeSite>(0);
            return;
        }

        foreach (Building b in Building.AllBuildings)
        {
            if (b == null || b.OwnerPlayerId != enemyPlayerId) continue;
            if (b.QueueBatchCount >= 3) continue;

            if (b is HomeSite)
            {
                if (villagerCount < 12 || (ecoPriority > 0 && currentPopulation < maxPopulation - 2))
                {
                    if (TrySpend(50, 0, 0))
                    {
                        b.SpawnUnit(0);
                        ecoPriority = Mathf.Max(0, ecoPriority - 1);
                    }
                }
            }
            else if (b is Barracks)
            {
                int idx = 0;
                float cavRatio = militaryCount > 0 ? (float)CountEnemyCavalry() / militaryCount : 0f;

                if (timePhase > 5f && cavRatio < 0.3f && Random.value < 0.4f && b.SpawnablePrefabs.Count > 1)
                    idx = 1;

                Unit unitPrefab = b.SpawnablePrefabs[idx]?.GetComponent<Unit>();
                if (unitPrefab != null)
                {
                    int costFood = unitPrefab is Cavalry ? 80 : 60;
                    int costGold = unitPrefab is Cavalry ? 30 : 20;
                    if (TrySpend(costFood, 0, costGold) && CanAddPopulation(1))
                        b.SpawnUnit(idx);
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // ATTACK WAVES
    // ════════════════════════════════════════════════════════════════════

    private void SendAttackWave()
    {
        List<Unit> available = GetAvailableMilitaryUnits();
        if (available.Count < minAttackForce) return;

        int attackForce = Mathf.Min(available.Count, maxAttackForce + waveCount * 2);
        List<Unit> attackers = available.Take(attackForce).ToList();

        Building primaryTarget = FindBestTarget(AveragePos(attackers));
        if (primaryTarget == null) return;

        waveCount++;
        StartCoroutine(ExecuteAttackWave(attackers, primaryTarget));
    }

    private IEnumerator ExecuteAttackWave(List<Unit> attackers, Building primaryTarget)
    {
        List<Unit> infantry = attackers.Where(u => u is Infantry).ToList();
        List<Unit> cavalry = attackers.Where(u => u is Cavalry).ToList();

        if (cavalry.Count >= 2)
        {
            Vector3 flankPos = primaryTarget.transform.position + (primaryTarget.transform.right * Random.Range(8f, 15f));
            flankPos = ClampToMap(flankPos);
            foreach (Unit c in cavalry)
            {
                c.ClearWaypoints();
                c.MoveTo(flankPos);
                c.SetBuildingTarget(primaryTarget);
            }
            yield return new WaitForSeconds(Random.Range(1f, 3f));
        }

        Vector3 approachPos = primaryTarget.transform.position;
        foreach (Unit u in infantry)
        {
            u.ClearWaypoints();
            u.SetBuildingTarget(primaryTarget);
        }

        yield return new WaitForSeconds(2f);

        foreach (Unit u in attackers)
        {
            if (u != null && !u.HasActiveTarget)
                u.SetBuildingTarget(primaryTarget);
        }
    }

    private Building FindBestTarget(Vector3 from)
    {
        Building best = null;
        float bestScore = float.MaxValue;
        int localId = PlayerColorManager.LocalPlayerIndex;

        foreach (Building b in Building.AllBuildings)
        {
            if (b == null || b.OwnerPlayerId != localId) continue;

            float dist = Vector3.Distance(from, b.transform.position);
            float score = dist;

            if (b is HomeSite) score *= 0.5f;
            else if (b is Barracks) score *= 0.7f;

            if (b.CurrentBuildingHealth < b.MaxBuildingHealth * 0.3f)
                score *= 0.4f;

            if (score < bestScore) { bestScore = score; best = b; }
        }
        return best;
    }

    // ════════════════════════════════════════════════════════════════════
    // DEFENSE
    // ════════════════════════════════════════════════════════════════════

    private void RunDefense()
    {
        if (towerPrefab == null) return;
        Vector3 basePos = GetEnemyBasePosition();
        if (basePos == Vector3.zero) return;

        int towerCount = CountEnemyBuildingsOfPrefab(towerPrefab);
        int maxTowers = 2 + difficultyLevel;

        if (towerCount < maxTowers && wood > towerCost * 2 && gold > towerCost)
        {
            TryPlaceBuilding(towerPrefab);
        }

        if (isUnderAttack)
        {
            RecallDefenders();
        }
    }

    private void RecallDefenders()
    {
        Vector3 basePos = GetEnemyBasePosition();
        List<Unit> military = GetAvailableMilitaryUnits();

        foreach (Unit u in military)
        {
            float distToBase = Vector3.Distance(u.transform.position, basePos);
            if (distToBase > 15f)
            {
                u.ClearWaypoints();
                u.MoveTo(basePos + Random.insideUnitSphere * 5f);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // SCOUTING
    // ════════════════════════════════════════════════════════════════════

    private void InitialScout() => RunScouting();

    private void RunScouting()
    {
        int localId = PlayerColorManager.LocalPlayerIndex;
        Building playerBase = null;
        foreach (Building b in Building.AllBuildings)
        {
            if (b != null && b.OwnerPlayerId == localId && b is HomeSite)
            { playerBase = b; break; }
        }

        if (playerBase == null) return;

        if (!hasScouted)
        {
            List<Unit> scouts = GetAvailableMilitaryUnits();
            if (scouts.Count > 0)
            {
                scouts[0].MoveTo(playerBase.transform.position);
                hasScouted = true;
            }
        }

        lastScoutPosition = playerBase.transform.position;
    }

    // ════════════════════════════════════════════════════════════════════
    // GATHERING
    // ════════════════════════════════════════════════════════════════════

    private void InitialGatherOrders() => CheckGatherers();

    private void CheckGatherers()
    {
        if (UnitSelectionManager.Instance == null) return;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (u == null || !(u is Villager v)) continue;
            if (u.OwnerPlayerId != enemyPlayerId) continue;
            if (v.IsGathering) continue;

            ResourceType preferredType = GetPreferredResourceType();
            ResourceNode node = ResourceNode.FindNearest(u.transform.position, preferredType, null, 250f);
            if (node == null)
                node = ResourceNode.FindNearestAny(u.transform.position, 250f);
            if (node != null) v.GatherFrom(node);
        }
    }

    private ResourceType GetPreferredResourceType()
    {
        int militaryCount = CountEnemyMilitary();
        if (militaryCount > 5) return ResourceType.Gold;
        if (CountEnemyVillagers() < 4) return ResourceType.Food;
        if (wood < 80) return ResourceType.Wood;
        return ResourceType.Gold;
    }

    private void AssignIdleVillagersToResourceBuildings()
    {
        foreach (ResourceBuilding rb in FindObjectsOfType<ResourceBuilding>())
        {
            if (rb == null) continue;
            Building b = rb.GetComponent<Building>();
            if (b != null && b.OwnerPlayerId != enemyPlayerId) continue;
            if (rb.WorkerCount >= rb.MaxWorkers) continue;

            Villager idle = FindIdleVillager();
            if (idle != null)
                rb.TryAddVillager(idle);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // PATROL
    // ════════════════════════════════════════════════════════════════════

    private void IssuePatrolOrders(List<Unit> units)
    {
        Vector3 basePos = GetEnemyBasePosition();
        foreach (Unit u in units)
        {
            if (u == null || u.HasActiveTarget) continue;
            Vector2 rand = Random.insideUnitCircle * 12f;
            Vector3 patrol = basePos + new Vector3(rand.x, 0f, rand.y);
            patrol = ClampToMap(patrol);
            u.SetFirstWaypoint(patrol);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // WIN / LOSE
    // ════════════════════════════════════════════════════════════════════

    public void OnBuildingDestroyed(Building building)
    {
        StartCoroutine(CheckWinLose());
    }

    private IEnumerator CheckWinLose()
    {
        yield return null;

        bool enemyCanSurvive = false;
        bool playerCanSurvive = false;
        int localId = PlayerColorManager.LocalPlayerIndex;

        foreach (Building b in Building.AllBuildings)
        {
            if (b == null) continue;
            if (b.OwnerPlayerId == enemyPlayerId) enemyCanSurvive = true;
            if (b.OwnerPlayerId == localId) playerCanSurvive = true;
        }

        if (!enemyCanSurvive || !playerCanSurvive)
        {
            if (UnitSelectionManager.Instance != null)
            {
                foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
                {
                    if (u == null) continue;
                    if (u is Villager || u.UnitType == "Builder")
                    {
                        if (u.OwnerPlayerId == enemyPlayerId) enemyCanSurvive = true;
                        if (u.OwnerPlayerId == localId) playerCanSurvive = true;
                    }
                }
            }
        }

        if (!enemyCanSurvive)
        {
            PlayerPrefs.SetInt("EnemyGamesPlayed", gamesPlayed + 1);
            PlayerPrefs.Save();
            WinLoseUI.Instance?.ShowWin();
        }
        if (!playerCanSurvive)
        {
            PlayerPrefs.SetInt("EnemyGamesPlayed", gamesPlayed + 1);
            PlayerPrefs.Save();
            WinLoseUI.Instance?.ShowLose();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // HELPERS
    // ════════════════════════════════════════════════════════════════════

    private int CountEnemyMilitary()
    {
        int count = 0;
        if (UnitSelectionManager.Instance == null) return 0;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
            if (u != null && !(u is Villager) && u.OwnerPlayerId == enemyPlayerId) count++;
        return count;
    }

    private int CountEnemyVillagers()
    {
        int count = 0;
        if (UnitSelectionManager.Instance == null) return 0;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
            if (u != null && u is Villager && u.OwnerPlayerId == enemyPlayerId) count++;
        return count;
    }

    private int CountEnemyCavalry()
    {
        int count = 0;
        if (UnitSelectionManager.Instance == null) return 0;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
            if (u != null && u is Cavalry && u.OwnerPlayerId == enemyPlayerId) count++;
        return count;
    }

    private int CountEnemyBuildings<T>() where T : Building
    {
        int count = 0;
        foreach (Building b in Building.AllBuildings)
            if (b != null && b is T && b.OwnerPlayerId == enemyPlayerId) count++;
        return count;
    }

    private int CountEnemyBuildingsOfPrefab(GameObject prefab)
    {
        if (prefab == null) return 0;
        int count = 0;
        foreach (Building b in Building.AllBuildings)
        {
            if (b == null || b.OwnerPlayerId != enemyPlayerId) continue;
            if (b.gameObject.name.StartsWith(prefab.name.Replace("(Clone)", "").Trim()))
                count++;
        }
        return count;
    }

    private void TrainFromBuilding<T>(int unitIndex) where T : Building
    {
        foreach (Building b in Building.AllBuildings)
        {
            if (b == null || b.OwnerPlayerId != enemyPlayerId) continue;
            if (b is T && b.QueueBatchCount < 3)
            {
                b.SpawnUnit(unitIndex);
                return;
            }
        }
    }

    private List<Unit> GetAvailableMilitaryUnits()
    {
        List<Unit> result = new List<Unit>();
        if (UnitSelectionManager.Instance == null) return result;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (u == null || u is Villager) continue;
            if (u.OwnerPlayerId != enemyPlayerId) continue;
            result.Add(u);
        }
        return result;
    }

    private Villager FindIdleVillager()
    {
        if (UnitSelectionManager.Instance == null) return null;
        Villager best = null;
        float bestDist = float.MaxValue;
        Vector3 basePos = GetEnemyBasePosition();

        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (!(u is Villager v)) continue;
            if (u.OwnerPlayerId != enemyPlayerId) continue;
            if (v.IsIdle)
            {
                float d = Vector3.Distance(u.transform.position, basePos);
                if (d < bestDist) { bestDist = d; best = v; }
            }
        }
        return best;
    }

    private Building FindNearestEnemyBuilding(Vector3 from)
    {
        Building nearest = null;
        float best = float.MaxValue;
        int localId = PlayerColorManager.LocalPlayerIndex;

        foreach (Building b in Building.AllBuildings)
        {
            if (b == null || b.OwnerPlayerId != localId) continue;
            float d = Vector3.Distance(from, b.transform.position);
            if (d < best) { best = d; nearest = b; }
        }
        return nearest;
    }

    private Vector3 AveragePos(List<Unit> units)
    {
        Vector3 sum = Vector3.zero; int cnt = 0;
        foreach (Unit u in units) { if (u != null) { sum += u.transform.position; cnt++; } }
        return cnt > 0 ? sum / cnt : Vector3.zero;
    }

    private Vector3 GetEnemyBasePosition()
    {
        foreach (Building b in Building.AllBuildings)
            if (b != null && b.OwnerPlayerId == enemyPlayerId) return b.transform.position;
        return Vector3.zero;
    }

    private Vector3 ClampToMap(Vector3 pos)
    {
        if (MapBoundary.Instance != null)
            return MapBoundary.Instance.Clamp(pos);
        return pos;
    }
}