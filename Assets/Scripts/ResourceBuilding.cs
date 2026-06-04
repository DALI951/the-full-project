using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Attach to Farm, Market, LumberMill.
/// Generates resources over time — requires villagers inside to operate.
/// More villagers = faster generation. Max 8 villagers.
/// </summary>
public class ResourceBuilding : MonoBehaviour
{
    [Header("Resource Generation")]
    [SerializeField] private ResourceType resourceType  = ResourceType.Food;
    [SerializeField] private int          amountPerTick = 5;
    [SerializeField] private float        tickInterval  = 5f;

    private float timer = 0f;

    // ── Worker system ────────────────────────────────────────────────────
    private readonly List<Villager> workers  = new List<Villager>();
    private const int               maxWorkers = 8;

    // ── Lifecycle ────────────────────────────────────────────────────────

    private void Start()
    {
        // Ensure a collider exists so OnMouseDown fires
        if (GetComponent<Collider>() == null && GetComponentInChildren<Collider>() == null)
        {
            BoxCollider bc = gameObject.AddComponent<BoxCollider>();
            bc.size   = new Vector3(3f, 3f, 3f);
            bc.center = new Vector3(0f, 1.5f, 0f);
        }
    }

    private void OnDestroy()
    {
        for (int i = workers.Count - 1; i >= 0; i--)
            if (workers[i] != null)
                workers[i].ExitResourceBuilding(transform.position + Vector3.right * 2f);
        workers.Clear();
    }

    private void Update()
    {
        // Clean up null refs (dead villagers)
        workers.RemoveAll(v => v == null);

        if (workers.Count == 0) return;

        // Each extra villager speeds up the timer proportionally
        timer += Time.deltaTime * workers.Count;
        if (timer >= tickInterval)
        {
            timer = 0f;
            int generated = amountPerTick * workers.Count;
            Building b = GetComponent<Building>();
            int pid = b != null ? b.OwnerPlayerId : -1;
            NetworkedPlayer owner = NetworkedPlayer.Get(pid);
            if (owner != null)
            {
                switch (resourceType)
                {
                    case ResourceType.Food: owner.AddResources(generated, 0, 0); break;
                    case ResourceType.Wood: owner.AddResources(0, generated, 0); break;
                    case ResourceType.Gold: owner.AddResources(0, 0, generated); break;
                }
            }
            else
            {
                switch (resourceType)
                {
                    case ResourceType.Food: ResourceManager.Instance?.AddResources(generated, 0, 0); break;
                    case ResourceType.Wood: ResourceManager.Instance?.AddResources(0, generated, 0); break;
                    case ResourceType.Gold: ResourceManager.Instance?.AddResources(0, 0, generated); break;
                }
            }
        }
    }

    // ── Click to open UI ─────────────────────────────────────────────────

    private void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        ResourceBuildingUI.Instance?.Show(this);
    }

    // ── Worker API ───────────────────────────────────────────────────────

    /// <summary>Returns false if building is full; shows notification.</summary>
    public bool TryAddVillager(Villager v)
    {
        if (workers.Count >= maxWorkers)
        {
            ResourceBuildingUI.Instance?.ShowNotification(
                $"{gameObject.name} is full! (max {maxWorkers} villagers)");
            return false;
        }
        if (workers.Contains(v)) return false;
        v.AssignToResourceBuilding(this);
        return true;
    }

    /// <summary>Called by Villager when it physically arrives at the building.</summary>
    public void OnVillagerArrived(Villager v)
    {
        if (!workers.Contains(v)) workers.Add(v);
        ResourceBuildingUI.Instance?.RefreshIfShowing(this);
    }

    /// <summary>Called by Villager when it leaves (MoveTo, Die, etc.).</summary>
    public void OnVillagerLeft(Villager v)
    {
        workers.Remove(v);
        ResourceBuildingUI.Instance?.RefreshIfShowing(this);
    }

    /// <summary>Ejects the most-recently-added worker villager.</summary>
    public void EjectOneVillager()
    {
        if (workers.Count == 0) return;
        Villager v = workers[workers.Count - 1];
        workers.RemoveAt(workers.Count - 1);
        if (v == null) return;
        Vector3 exitPos = transform.position + transform.forward * 2.5f;
        if (NavMesh.SamplePosition(exitPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            exitPos = hit.position;
        v.ExitResourceBuilding(exitPos);
        ResourceBuildingUI.Instance?.RefreshIfShowing(this);
    }

    /// <summary>Finds the nearest idle local villager and sends them in.</summary>
    public void GetClosestIdleVillager()
    {
        if (workers.Count >= maxWorkers)
        {
            ResourceBuildingUI.Instance?.ShowNotification(
                $"{gameObject.name} is full! (max {maxWorkers} villagers)");
            return;
        }
        if (UnitSelectionManager.Instance == null) return;

        Villager best     = null;
        float    bestDist = float.MaxValue;

        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (!(u is Villager v)) continue;
            if (v.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex) continue;
            if (!v.IsIdle) continue;
            float d = Vector3.Distance(transform.position, v.transform.position);
            if (d < bestDist) { bestDist = d; best = v; }
        }

        if (best != null)
            TryAddVillager(best);
        else
            ResourceBuildingUI.Instance?.ShowNotification("No idle villagers available!");
    }

    // ── Properties ───────────────────────────────────────────────────────
    public int          WorkerCount     => workers.Count;
    public int          MaxWorkers      => maxWorkers;
    public ResourceType GetResourceType => resourceType;
}