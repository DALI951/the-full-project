using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// EnemyAI — controls the enemy faction.
/// Attach to any persistent GameObject (e.g. GameManager).
/// Requires a second BuildingSpawner in the scene with playerIndex = enemyPlayerId.
/// </summary>
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
    [SerializeField] private float trainCheckInterval  = 20f;  // try to queue a unit every N s
    [SerializeField] private float attackInterval      = 60f;  // send attack wave every N s
    [SerializeField] private float patrolInterval      = 15f;  // give patrol orders every N s
    [SerializeField] private float gatherCheckInterval = 10f;  // redirect idle villagers every N s

    [Header("Attack Threshold")]
    [SerializeField] private int minAttackForce = 4;

    [Header("Adaptive Difficulty (auto-scales per game)")]
    private int gamesPlayed = 0;

    private int   food, wood, gold;
    private float trainTimer, attackTimer, patrolTimer, gatherTimer;

    // ── Lifecycle ────────────────────────────────────────────────────────

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
        int diff = Mathf.Min(gamesPlayed, 10);
        attackInterval     = Mathf.Max(30f, attackInterval     - diff * 3f);
        trainCheckInterval = Mathf.Max( 8f, trainCheckInterval - diff * 1f);
        startFood += diff * 40;
        startWood += diff * 25;
        startGold += diff * 15;

        food = startFood; wood = startWood; gold = startGold;
        Invoke(nameof(InitialGatherOrders), 3f);
    }

    private void Update()
    {
        trainTimer  += Time.deltaTime;
        attackTimer += Time.deltaTime;
        patrolTimer += Time.deltaTime;
        gatherTimer += Time.deltaTime;

        if (trainTimer  >= trainCheckInterval)  { trainTimer  = 0f; TryTrainUnits(); }
        if (attackTimer >= attackInterval)       { attackTimer = 0f; SendAttackWave(); }
        if (patrolTimer >= patrolInterval)       { patrolTimer = 0f; IssuePatrolOrders(); }
        if (gatherTimer >= gatherCheckInterval)  { gatherTimer = 0f; CheckGatherers(); }
    }

    // ── Resource API (called by Building and Villager) ────────────────────

    /// <summary>Add gathered/refunded resources to the enemy pool.</summary>
    public void AddEnemyResources(int f, int w, int g)
    {
        food = Mathf.Max(0, food + f);
        wood = Mathf.Max(0, wood + w);
        gold = Mathf.Max(0, gold + g);
    }

    /// <summary>Returns true and deducts if the enemy can afford the cost.</summary>
    public bool TrySpend(int costFood, int costWood, int costGold)
    {
        if (food < costFood || wood < costWood || gold < costGold) return false;
        food -= costFood; wood -= costWood; gold -= costGold;
        return true;
    }

    public bool CanAddPopulation(int amount = 1) => currentPopulation + amount <= maxPopulation;
    public void AddPopulation(int amount = 1)    => currentPopulation = Mathf.Clamp(currentPopulation + amount, 0, maxPopulation);
    public void RemovePopulation(int amount = 1) => currentPopulation = Mathf.Max(0, currentPopulation - amount);
    public int  CurrentPopulation => currentPopulation;
    public int  MaxPopulation     => maxPopulation;

    // ── Training ─────────────────────────────────────────────────────────

    private void TryTrainUnits()
    {
        foreach (Building b in Building.AllBuildings)
        {
            if (b == null || b.OwnerPlayerId != enemyPlayerId) continue;
            if (b.QueueBatchCount >= 3) continue;

            if (b is HomeSite)
                b.SpawnUnit(0);                          // Villager
            else if (b is Barracks)
            {
                int idx = (b.SpawnablePrefabs.Count > 1 &&
                        CountEnemyMilitary() % 3 == 0) ? 1 : 0;   // every 3rd unit = cavalry
                b.SpawnUnit(idx);
            }
            else
                b.SpawnUnit(0);
        }
    }

    private int CountEnemyMilitary()
    {
        int count = 0;
        if (UnitSelectionManager.Instance == null) return 0;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
            if (u != null && !(u is Villager) && u.OwnerPlayerId == enemyPlayerId) count++;
        return count;
    }

    // ── Gathering ─────────────────────────────────────────────────────────

    private void InitialGatherOrders() => CheckGatherers();

    private void CheckGatherers()
    {
        if (UnitSelectionManager.Instance == null) return;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (u == null || !(u is Villager v)) continue;
            if (u.OwnerPlayerId != enemyPlayerId) continue;
            if (v.IsGathering) continue;   // already working
            ResourceNode node = ResourceNode.FindNearestAny(u.transform.position, 250f);
            if (node != null) v.GatherFrom(node);
        }
    }

    // ── Attack Wave ───────────────────────────────────────────────────────

    private void SendAttackWave()
    {
        if (UnitSelectionManager.Instance == null) return;

        List<Unit> attackers = new List<Unit>();
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (u == null || u is Villager) continue;
            if (u.OwnerPlayerId != enemyPlayerId) continue;
            attackers.Add(u);
        }
        if (attackers.Count == 0) return;
        if (attackers.Count < minAttackForce) return;   // wait for enough army

        Building target = FindNearestPlayerBuilding(AveragePos(attackers));
        if (target == null) return;

        foreach (Unit u in attackers)
            u.SetBuildingTarget(target);
    }

    // ── Patrol ────────────────────────────────────────────────────────────

    private void IssuePatrolOrders()
    {
        if (UnitSelectionManager.Instance == null) return;
        Vector3 basePos = GetEnemyBasePosition();

        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (u == null || u is Villager) continue;
            if (u.OwnerPlayerId != enemyPlayerId) continue;
            if (u.HasActiveTarget) continue;   // already has orders

            Vector2 rand   = Random.insideUnitCircle * 12f;
            Vector3 patrol = basePos + new Vector3(rand.x, 0f, rand.y);
            if (MapBoundary.Instance != null) patrol = MapBoundary.Instance.Clamp(patrol);
            u.SetFirstWaypoint(patrol);
        }
    }

    // ── Win / Lose ────────────────────────────────────────────────────────

    /// <summary>Called by Building.OnBuildingDestroyed() before the GO is removed.</summary>
    public void OnBuildingDestroyed(Building building)
    {
        StartCoroutine(CheckWinLose());
    }

    private IEnumerator CheckWinLose()
    {
        yield return null;

        bool enemyCanSurvive  = false;
        bool playerCanSurvive = false;
        int  localId          = PlayerColorManager.LocalPlayerIndex;

        foreach (Building b in Building.AllBuildings)
        {
            if (b == null) continue;
            if (b.OwnerPlayerId == enemyPlayerId) enemyCanSurvive  = true;
            if (b.OwnerPlayerId == localId)        playerCanSurvive = true;
        }

        // Villagers / Builders can still rebuild — don't trigger yet
        if (!enemyCanSurvive || !playerCanSurvive)
        {
            if (UnitSelectionManager.Instance != null)
            {
                foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
                {
                    if (u == null) continue;
                    if (u is Villager || u.UnitType == "Builder")
                    {
                        if (u.OwnerPlayerId == enemyPlayerId) enemyCanSurvive  = true;
                        if (u.OwnerPlayerId == localId)        playerCanSurvive = true;
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

    // ── Helpers ───────────────────────────────────────────────────────────

    private Building FindNearestPlayerBuilding(Vector3 from)
    {
        Building nearest = null;
        float    best    = float.MaxValue;
        int      localId = PlayerColorManager.LocalPlayerIndex;

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
}