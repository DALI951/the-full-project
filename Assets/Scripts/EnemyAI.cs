using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using System.Linq;

public class EnemyAI : MonoBehaviour
{
    public static EnemyAI Instance { get; private set; }

    [Header("Enemy Identity")]
    [SerializeField] public int enemyPlayerId = 1;

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
        // Disable on pure multiplayer clients; run in host mode (NetworkServer.active) and offline mode (!NetworkClient.active)
        if (NetworkClient.active && !NetworkServer.active) { enabled = false; return; }

        // Only disable EnemyAI if in a multiplayer match with NO AI bot players
        if (NetworkServer.active && NetworkServer.connections.Count >= 2)
        {
            var allPlayers = FindObjectsOfType<NetworkedPlayer>();
            if (allPlayers.All(p => !IsPlayerAI(p))) { enabled = false; return; }
        }

        ResolveEnemyPlayerId();

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

        float totalNow = food + wood + gold;
        float totalStart = Mathf.Max(1f, startFood + startWood + startGold);
        float resourceRatio = totalNow / totalStart;

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

    public bool CanAfford(int costFood, int costWood, int costGold)
    {
        return food >= costFood && wood >= costWood && gold >= costGold;
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
        if (wood >= barracksCost)
            TryPlaceBuilding(barracksPrefab);
    }

    [Header("Building Prefabs")]
    [SerializeField] private GameObject barracksPrefab;
    [SerializeField] private int barracksCost = 150;
    [SerializeField] private int towerCost = 100;
    private const int BUILDING_SPACING = 8;

    public GameObject SpawnBuilding(GameObject prefab, Vector3 pos)
    {
        if (prefab == null) return null;
        pos = ClampToMap(pos);
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            pos = hit.position;
        else if (Physics.Raycast(new Vector3(pos.x, pos.y + 200f, pos.z), Vector3.down, out RaycastHit gHit, 400f))
            pos.y = gHit.point.y;

        GameObject go = Instantiate(prefab, pos, Quaternion.identity);
        AlignBottomToGround(go, pos.y);
        int layer = LayerMask.NameToLayer("Building");
        if (layer >= 0) SetLayerRecursive(go, layer);

        if (go.GetComponentInChildren<Collider>() == null)
        {
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(2f, 2f, 2f);
        }

        if (go.TryGetComponent(out Building building))
            building.SetOwner(enemyPlayerId);

        if (NetworkServer.active)
            NetworkServer.Spawn(go);

        return go;
    }

    private void TryPlaceBuilding(GameObject prefab)
    {
        if (prefab == null) return;
        Vector3 basePos = GetEnemyBasePosition();
        if (basePos == Vector3.zero) return;

        Vector3 pos = FindBuildSpot(basePos, 6f);
        if (pos != Vector3.zero)
        {
            int cost = GetBuildingCost(prefab);
            if (!TrySpend(cost, cost, cost / 2)) return;

            AssignBuilderToBuild(pos, prefab);
            builtPositions.Add(pos);
        }
    }

    private Vector3 FindBuildSpot(Vector3 basePos, float minSpacing = 6f)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 rand = Random.insideUnitCircle.normalized * Random.Range(minSpacing + 2f, 22f + attempt * 2f);
            Vector3 pos = basePos + new Vector3(rand.x, 0f, rand.y);
            pos = ClampToMap(pos);

            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                pos = hit.position;

            if (IsPositionClearForBuilding(pos, minSpacing))
                return pos;
        }
        return Vector3.zero;
    }

    private bool IsPositionClearForBuilding(Vector3 pos, float minSpacing = 5f)
    {
        foreach (Vector3 built in builtPositions)
        {
            if (Vector3.Distance(pos, built) < minSpacing) return false;
        }
        foreach (Building b in Building.AllBuildings)
        {
            if (b != null && Vector3.Distance(pos, b.transform.position) < minSpacing) return false;
        }
        foreach (ConstructionSite cs in FindObjectsOfType<ConstructionSite>())
        {
            if (cs != null && Vector3.Distance(pos, cs.transform.position) < minSpacing) return false;
        }
        Collider[] hits = Physics.OverlapSphere(pos, minSpacing * 0.5f);
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
        Villager builder = FindIdleVillager();
        if (builder == null) return;
        Vector3 clampedPos = ClampToMap(pos);
        if (UnityEngine.AI.NavMesh.SamplePosition(clampedPos, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
            clampedPos = hit.position;
        else if (Physics.Raycast(new Vector3(clampedPos.x, clampedPos.y + 200f, clampedPos.z), Vector3.down, out RaycastHit gHit, 400f))
            clampedPos.y = gHit.point.y;

        if (constructionSitePrefab != null)
        {
            GameObject siteGO = Object.Instantiate(constructionSitePrefab, clampedPos, Quaternion.identity);
            AlignBottomToGround(siteGO, clampedPos.y);
            ConstructionSite site = siteGO.GetComponent<ConstructionSite>();
            if (site == null) { Object.Destroy(siteGO); return; }
            int cost = GetBuildingCost(prefab);
            site.Initialize(prefab, 10f, cost, cost, cost / 2);
            site.SetOwnerOnServer(enemyPlayerId);
            if (NetworkServer.active)
                NetworkServer.Spawn(siteGO);
            builder.BuildAt(site);
        }
        else
        {
            SpawnBuilding(prefab, clampedPos);
        }
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

            if (b is HomeSite || b.IsHomeSiteBuilding)
            {
                if (villagerCount < 12 || (ecoPriority > 0 && currentPopulation < maxPopulation - 2))
                {
                    if (CanAfford(50, 0, 0) && CanAddPopulation(1))
                    {
                        b.SpawnUnit(0);
                        ecoPriority = Mathf.Max(0, ecoPriority - 1);
                    }
                }
            }
            else if (b is Barracks || b.IsBarracksBuilding)
            {
                int idx = 0;
                float cavRatio = militaryCount > 0 ? (float)CountEnemyCavalry() / militaryCount : 0f;

                if (timePhase > 5f && cavRatio < 0.3f && Random.value < 0.4f && b.SpawnablePrefabs.Count > 1)
                    idx = 1;

                int costFood = idx == 1 ? 100 : 60;
                int costGold = idx == 1 ? 60 : 20;
                if (CanAfford(costFood, 0, costGold) && CanAddPopulation(1))
                    b.SpawnUnit(idx);
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
        waveCount++;
        StartCoroutine(ExecuteAttackWave(attackers, primaryTarget));
    }

    private IEnumerator ExecuteAttackWave(List<Unit> attackers, Building primaryTarget)
    {
        if (primaryTarget == null)
        {
            Unit nearestEnemy = FindNearestPlayerUnit(AveragePos(attackers));
            if (nearestEnemy != null)
            {
                foreach (Unit u in attackers)
                {
                    if (u != null)
                    {
                        u.ClearWaypoints();
                        u.SetCombatTarget(nearestEnemy);
                    }
                }
            }
            yield break;
        }

        Vector3 targetPos = primaryTarget.transform.position;
        List<Unit> cavalry = attackers.Where(u => u is Cavalry).ToList();
        List<Unit> infantry = attackers.Where(u => u != null && !(u is Cavalry)).ToList();

        if (cavalry.Count >= 2 && primaryTarget != null)
        {
            Vector3 flankPos = targetPos + (primaryTarget.transform.right * Random.Range(8f, 15f));
            flankPos = ClampToMap(flankPos);
            foreach (Unit c in cavalry)
            {
                if (c == null) continue;
                c.ClearWaypoints();
                c.MoveTo(flankPos);
                if (primaryTarget != null) c.SetBuildingTarget(primaryTarget);
            }
            yield return new WaitForSeconds(Random.Range(1f, 3f));
        }

        foreach (Unit u in infantry)
        {
            if (u == null) continue;
            u.ClearWaypoints();
            if (primaryTarget != null)
                u.SetBuildingTarget(primaryTarget);
            else
                u.MoveTo(targetPos);
        }

        yield return new WaitForSeconds(2f);

        foreach (Unit u in attackers)
        {
            if (u != null && !u.HasActiveTarget)
            {
                if (primaryTarget != null)
                    u.SetBuildingTarget(primaryTarget);
                else
                {
                    Unit playerU = FindNearestPlayerUnit(u.transform.position);
                    if (playerU != null) u.SetCombatTarget(playerU);
                }
            }
        }
    }

    private Unit FindNearestPlayerUnit(Vector3 from)
    {
        if (UnitSelectionManager.Instance == null) return null;
        Unit nearest = null;
        float bestDist = float.MaxValue;

        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (u == null || u.IsDead || u.OwnerPlayerId == enemyPlayerId) continue;
            float d = Vector3.Distance(from, u.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = u;
            }
        }
        return nearest;
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

            if (b is HomeSite || b.IsHomeSiteBuilding) score *= 0.5f;
            else if (b is Barracks || b.IsBarracksBuilding) score *= 0.7f;

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

    public void SendVillagerToGather(Villager v)
    {
        if (v == null || v.IsGathering) return;
        ResourceType preferredType = GetPreferredResourceType();
        ResourceNode node = ResourceNode.FindNearest(v.transform.position, preferredType, null, 250f);
        if (node == null)
            node = ResourceNode.FindNearestAny(v.transform.position, 250f);
        if (node != null)
        {
            v.GatherFrom(node);
        }
    }

    private void CheckGatherers()
    {
        if (UnitSelectionManager.Instance == null) return;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (u == null || !(u is Villager v)) continue;
            if (u.OwnerPlayerId != enemyPlayerId) continue;
            if (v.IsGathering) continue;

            SendVillagerToGather(v);
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
        if (GameOverManager.Instance != null)
            GameOverManager.Instance.CheckGameOver(building != null ? building.OwnerPlayerId : -1);
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
            bool match = b is T;
            if (typeof(T) == typeof(HomeSite) && b.IsHomeSiteBuilding) match = true;
            if (typeof(T) == typeof(Barracks) && b.IsBarracksBuilding) match = true;
            if (match && b.QueueBatchCount < 3)
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

        ResolveEnemyPlayerId();
        foreach (Building b in Building.AllBuildings)
            if (b != null && b.OwnerPlayerId == enemyPlayerId) return b.transform.position;
        return Vector3.zero;
    }

    public void ResolveEnemyPlayerId()
    {
        bool hasEnemyBuilding = false;
        foreach (Building b in Building.AllBuildings)
        {
            if (b != null && b.OwnerPlayerId == enemyPlayerId)
            {
                hasEnemyBuilding = true;
                break;
            }
        }

        if (!hasEnemyBuilding)
        {
            NetworkedPlayer aiPlayer = FindObjectsOfType<NetworkedPlayer>().FirstOrDefault(p => IsPlayerAI(p));
            if (aiPlayer != null)
            {
                enemyPlayerId = aiPlayer.playerIndex;
            }
            else
            {
                int localId = PlayerColorManager.LocalPlayerIndex;
                foreach (Building b in Building.AllBuildings)
                {
                    if (b != null && b.OwnerPlayerId != 0 && b.OwnerPlayerId != localId)
                    {
                        enemyPlayerId = b.OwnerPlayerId;
                        break;
                    }
                }
            }
        }
    }

    private void AlignBottomToGround(GameObject go, float targetGroundY)
    {
        if (go == null) return;
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds totalBounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer || !renderers[i].enabled) continue;
                if (!hasBounds)
                {
                    totalBounds = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    totalBounds.Encapsulate(renderers[i].bounds);
                }
            }
            if (hasBounds)
            {
                float bottomY = totalBounds.min.y;
                float diff = targetGroundY - bottomY;
                if (Mathf.Abs(diff) > 0.01f)
                {
                    go.transform.position += new Vector3(0f, diff, 0f);
                }
            }
        }
    }

    private void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private Vector3 ClampToMap(Vector3 pos)
    {
        if (MapBoundary.Instance != null)
            return MapBoundary.Instance.Clamp(pos);
        return pos;
    }

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// A destroyed MonoBehaviour is NOT a real CLR null (Unity fake-null only affects
    /// == and !=). Any "Instance?.Foo()" call site elsewhere would therefore still
    /// dispatch into this dead object and throw MissingReferenceException the next
    /// time the scene reloads. Clearing the field makes it a true null.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private static bool IsPlayerAI(NetworkedPlayer p)
    {
        if (p == null) return false;
        var field = typeof(NetworkedPlayer).GetField("isAI");
        if (field != null && field.FieldType == typeof(bool))
            return (bool)field.GetValue(p);
        var prop = typeof(NetworkedPlayer).GetProperty("isAI");
        if (prop != null && prop.PropertyType == typeof(bool))
            return (bool)prop.GetValue(p);

        if (p.connectionToClient == null && p.netIdentity != null && !p.isLocalPlayer)
            return true;
        if (!string.IsNullOrEmpty(p.displayName) && (p.displayName.StartsWith("Bot") || p.displayName.StartsWith("AI")))
            return true;
        return false;
    }
}
