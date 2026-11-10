using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Mirror;

public class ConstructionSite : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float buildTime      = 10f;
    [SerializeField] private GameObject completedPrefab;
    private string buildingName = "Building";

    [SyncVar] private float progress       = 0f;
    [SyncVar] private bool  isComplete     = false;
    [SyncVar] private int   pendingOwnerId = -1;
    [SyncVar] private int   refundFood, refundWood, refundGold;

    private List<Villager> builders = new List<Villager>();

    public void Initialize(GameObject prefab, float time,
        int costFood = 0, int costWood = 0, int costGold = 0)
    {
        completedPrefab = prefab;
        buildTime       = time;
        buildingName    = prefab != null ? prefab.name : "Building";
        refundFood      = costFood;
        refundWood      = costWood;
        refundGold      = costGold;
    }

    public void SetOwnerOnServer(int ownerId)
    {
        if (isClient && !isServer) return;
        pendingOwnerId = ownerId;
    }

    public void AssignBuilder(Villager v)
    {
        if (!builders.Contains(v))
            builders.Add(v);
        if (pendingOwnerId < 0 && v != null)
            pendingOwnerId = v.OwnerPlayerId;
    }

    private float GetBuilderDistance(Villager b)
    {
        if (b == null) return float.MaxValue;
        Collider col = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();
        if (col != null && !(col is MeshCollider mc && !mc.convex))
        {
            Vector3 closest = col.ClosestPoint(b.transform.position);
            return Vector2.Distance(new Vector2(b.transform.position.x, b.transform.position.z), new Vector2(closest.x, closest.z));
        }
        return Vector2.Distance(new Vector2(b.transform.position.x, b.transform.position.z), new Vector2(transform.position.x, transform.position.z));
    }

    private void Update()
    {
        if (isComplete) return;
        builders.RemoveAll(b => b == null);
        if (builders.Count == 0) return;

        int activeBuilders = 0;
        foreach (Villager b in builders)
        {
            float dist = GetBuilderDistance(b);
            if (dist <= 2.5f || Vector3.Distance(b.transform.position, transform.position) <= 4.5f) activeBuilders++;
        }

        if (activeBuilders == 0) return;

        if (!isClient || isServer)
        {
            progress += Time.deltaTime * activeBuilders;
            if (progress >= buildTime)
                Complete();
        }
    }

    private void Complete()
    {
        if (isClient && !isServer) return;
        isComplete = true;
        AudioManager.Instance?.PlayBuildingComplete();
        EffectManager.Instance?.PlayBuildCompleteEffect(transform.position);
        GameObject built = null;
        if (completedPrefab != null)
        {
            built = Instantiate(completedPrefab,
                transform.position, transform.rotation);
            int layer = LayerMask.NameToLayer("Building");
            SetLayerRecursive(built, layer);
            int ownerId = pendingOwnerId >= 0 ? pendingOwnerId : PlayerColorManager.LocalPlayerIndex;
            if (built.TryGetComponent(out Building b))
                b.SetOwner(ownerId);
            if (NetworkServer.active)
                NetworkServer.Spawn(built);
        }

        // Prevent builders or any units from getting trapped inside the new building's collider & NavMeshObstacle
        if (built != null)
        {
            DisplaceTrappedUnits(built);
        }

        bool isWall = (completedPrefab != null && completedPrefab.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                   || (!string.IsNullOrEmpty(buildingName) && buildingName.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0);

        List<Villager> currentBuilders = new List<Villager>(builders);
        builders.Clear();

        foreach (Villager b in currentBuilders)
        {
            if (b == null) continue;
            b.OnBuildingComplete();

            if (isWall)
            {
                ConstructionSite nextSite = FindNearestUnfinishedWallSite(transform.position, 6f, b.OwnerPlayerId);
                if (nextSite != null)
                {
                    nextSite.AssignBuilder(b);
                    b.BuildAt(nextSite);
                }
            }
        }

        if (NetworkServer.active)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    private void DisplaceTrappedUnits(GameObject built)
    {
        if (built == null) return;

        Collider buildCol = built.GetComponent<Collider>() ?? built.GetComponentInChildren<Collider>();
        Bounds bounds;
        if (buildCol != null)
        {
            bounds = buildCol.bounds;
        }
        else
        {
            bounds = new Bounds(built.transform.position, new Vector3(4f, 3f, 4f));
        }

        Bounds expanded = new Bounds(bounds.center, new Vector3(bounds.size.x + 1.2f, bounds.size.y + 4f, bounds.size.z + 1.2f));

        float checkRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) + 3.5f;
        Collider[] trappedCols = Physics.OverlapSphere(bounds.center, checkRadius);
        HashSet<Unit> unitsToCheck = new HashSet<Unit>();

        foreach (Collider c in trappedCols)
        {
            Unit u = c.GetComponentInParent<Unit>();
            if (u != null && !u.IsDead)
                unitsToCheck.Add(u);
        }

        foreach (Villager b in builders)
        {
            if (b != null && !b.IsDead)
                unitsToCheck.Add(b);
        }

        foreach (Unit u in unitsToCheck)
        {
            if (u == null) continue;
            Vector3 uPos = u.transform.position;

            bool insideXZ = Mathf.Abs(uPos.x - bounds.center.x) <= (bounds.extents.x + 0.6f) &&
                           Mathf.Abs(uPos.z - bounds.center.z) <= (bounds.extents.z + 0.6f);

            if (insideXZ || expanded.Contains(uPos))
            {
                Vector3 outward = uPos - bounds.center;
                outward.y = 0;
                if (outward.sqrMagnitude < 0.01f)
                    outward = built.transform.forward;
                outward.Normalize();

                float margin = Mathf.Max(bounds.extents.x, bounds.extents.z) + 1.2f;
                Vector3 safePos = bounds.center + outward * margin;
                safePos.y = built.transform.position.y;

                if (NavMesh.SamplePosition(safePos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    safePos = hit.position;

                NavMeshAgent ag = u.GetComponent<NavMeshAgent>();
                if (ag != null && ag.isOnNavMesh)
                {
                    ag.Warp(safePos);
                    ag.ResetPath();
                }
                else
                {
                    u.transform.position = safePos;
                }
            }
        }
    }

    private ConstructionSite FindNearestUnfinishedWallSite(Vector3 center, float maxDist, int ownerId)
    {
        ConstructionSite[] allSites = FindObjectsOfType<ConstructionSite>();
        ConstructionSite nearest = null;
        float minDistSq = maxDist * maxDist;

        foreach (ConstructionSite site in allSites)
        {
            if (site == null || site == this || site.isComplete) continue;
            if (ownerId >= 0 && site.pendingOwnerId >= 0 && site.pendingOwnerId != ownerId) continue;

            bool isSiteWall = (site.completedPrefab != null && site.completedPrefab.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                           || (!string.IsNullOrEmpty(site.buildingName) && site.buildingName.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isSiteWall) continue;

            float distSq = (site.transform.position - center).sqrMagnitude;
            if (distSq <= minDistSq)
            {
                minDistSq = distSq;
                nearest = site;
            }
        }
        return nearest;
    }

    private void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursive(t.gameObject, layer);
    }

    private void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        GameUI.Instance?.HideBuildingUI();
        UnitInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.ShowConstructionSite(this);
    }

    public string BuildingName => buildingName;

    public int MaxHealth
    {
        get
        {
            if (completedPrefab == null) return 100;
            Building b = completedPrefab.GetComponent<Building>();
            return b != null ? b.maxBuildingHealth : 100;
        }
    }

    public int CurrentHealth =>
        (int)(buildTime > 0 ? (progress / buildTime) * MaxHealth : 0);

    public bool HasActiveBuilders
    {
        get
        {
            foreach (Villager b in builders)
            {
                if (b == null) continue;
                float dist = GetBuilderDistance(b);
                if (dist <= 2.5f || Vector3.Distance(b.transform.position, transform.position) <= 4.5f)
                    return true;
            }
            return false;
        }
    }

    public void Demolish()
    {
        if (isClient && !isServer) return;
        if (isComplete) return;
        float ratio = 1f - (buildTime > 0 ? Mathf.Clamp01(progress / buildTime) : 0f);
        NetworkedPlayer owner = NetworkedPlayer.Get(pendingOwnerId);
        if (owner != null)
            owner.AddResources(
                Mathf.RoundToInt(refundFood * ratio),
                Mathf.RoundToInt(refundWood * ratio),
                Mathf.RoundToInt(refundGold * ratio));
        else
            ResourceManager.Instance?.AddResources(
                Mathf.RoundToInt(refundFood * ratio),
                Mathf.RoundToInt(refundWood * ratio),
                Mathf.RoundToInt(refundGold * ratio));
        foreach (Villager b in builders)
            if (b != null) b.OnBuildingComplete();
        builders.Clear();
        if (NetworkServer.active)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    public float Progress   => progress;
    public float BuildTime  => buildTime;
    public bool  IsComplete => isComplete;
}
