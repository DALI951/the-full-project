using System.Collections.Generic;
using UnityEngine;

public class ResourceNode : MonoBehaviour
{
    private static readonly List<ResourceNode> RegisteredNodes = new List<ResourceNode>();
    public static IReadOnlyList<ResourceNode> AllNodes => RegisteredNodes;

    [Header("=== RESOURCE TYPE ===")]
    [SerializeField] private ResourceType resourceType = ResourceType.Wood;

    [Header("=== KILL REQUIRED? (Animals only) ===")]
    [SerializeField] public  bool requiresKill = false;
    [SerializeField] private int  maxHealth    = 50;

    [Header("=== AMOUNTS ===")]
    [SerializeField] private int totalAmount  = 200;
    [SerializeField] private int maxGatherers = 8;

    [Tooltip("Drag the visible mesh CHILD here. If empty, auto-finds first child Renderer.")]
    [SerializeField] public GameObject visualRoot;

    // ── Runtime ───────────────────────────────────────────────────────────
    private int            remainingAmount;
    private int            currentHealth;
    private bool           isDead    = false;
    private bool           depleted  = false;

    // CRITICAL FIX: Explicit slot reservation system
    private Villager[]     gathererSlots;  // fixed-size array for O(1) slot ops
    private List<Villager> overflowQueue;  // villagers waiting for a slot

    protected virtual void Awake()
    {
        remainingAmount = totalAmount;
        currentHealth   = requiresKill ? maxHealth : 1;
        gathererSlots   = new Villager[maxGatherers];
        overflowQueue   = new List<Villager>();

        if (GetComponent<Collider>() == null &&
            GetComponentInChildren<Collider>() == null)
        {
            CapsuleCollider cc = gameObject.AddComponent<CapsuleCollider>();
            cc.height = 1f;
            cc.radius = 0.25f;
            cc.center = new Vector3(0, 0.5f, 0);
        }

        if (visualRoot == null)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
                visualRoot = renderers[0].gameObject;
        }

        if (visualRoot == gameObject)
        {
            Debug.LogWarning($"[{name}] visualRoot is root GameObject. Consider child 'Visual'.");
        }

        if (!RegisteredNodes.Contains(this))
            RegisteredNodes.Add(this);

        if (visualRoot != null && !visualRoot.activeSelf)
            visualRoot.SetActive(true);

        MinimapSystem.Instance?.TrackResource(this);
        ResourceCullingManager.Instance?.Register(this);
    }

    protected virtual void OnDestroy()
    {
        RegisteredNodes.Remove(this);
        MinimapSystem.Instance?.Untrack(transform);
        ResourceCullingManager.Instance?.Unregister(this);
    }

    private void OnMouseDown()
    {
        if (SelectionManager.IsPointerOverVisiblePanel()) return;
        ResourceInfoUI.Instance?.Show(this);
    }

    // ── Slot System ─────────────────────────────────────────────────────

    /// <summary>Try to reserve a slot. Returns slot index (0-7) or -1 if full.</summary>
    public int ReserveSlot(Villager villager)
    {
        if (depleted) return -1;

        // Check if already has a slot
        for (int i = 0; i < maxGatherers; i++)
        {
            if (gathererSlots[i] == villager) return i;
        }

        // Find empty slot
        for (int i = 0; i < maxGatherers; i++)
        {
            if (gathererSlots[i] == null)
            {
                gathererSlots[i] = villager;
                return i;
            }
        }

        // Full — add to overflow
        if (!overflowQueue.Contains(villager))
            overflowQueue.Add(villager);

        return -1;
    }

    /// <summary>Release a slot when villager leaves or dies.</summary>
    public void ReleaseSlot(Villager villager)
    {
        for (int i = 0; i < maxGatherers; i++)
        {
            if (gathererSlots[i] == villager)
            {
                gathererSlots[i] = null;
                // Promote overflow villager if any
                PromoteFromOverflow();
                return;
            }
        }
        overflowQueue.Remove(villager);
    }

    private void PromoteFromOverflow()
    {
        for (int i = overflowQueue.Count - 1; i >= 0; i--)
        {
            Villager v = overflowQueue[i];
            if (v == null)
            {
                overflowQueue.RemoveAt(i);
                continue;
            }
            int slot = ReserveSlot(v);
            if (slot >= 0)
            {
                overflowQueue.RemoveAt(i);
                v.OnSlotPromoted(this, slot);
                break;
            }
        }
    }

    public bool HasAvailableSlot()
    {
        if (depleted) return false;
        for (int i = 0; i < maxGatherers; i++)
            if (gathererSlots[i] == null) return true;
        return false;
    }

    public int ActiveGathererCount()
    {
        int count = 0;
        for (int i = 0; i < maxGatherers; i++)
            if (gathererSlots[i] != null) count++;
        return count;
    }

    public int OverflowCount => overflowQueue.Count;

    // ── Gathering & Combat ────────────────────────────────────────────

    public int Gather(int amount)
    {
        if (depleted) return 0;
        if (requiresKill && !isDead) return 0;

        int got = Mathf.Min(amount, remainingAmount);
        remainingAmount -= got;

        if (remainingAmount <= 0 && !depleted)
            OnDepleted();

        return got;
    }

    public void TakeDamage(int amount)
    {
        if (!requiresKill || isDead || depleted) return;
        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            isDead = true;
            OnKilled();
        }
    }

    protected virtual void OnKilled()
    {
        Debug.Log($"[{name}] Killed — ready to gather.");
        if (this is AnimalNode a) a.StopMoving();
    }

    protected virtual void OnDepleted()
    {
        if (depleted) return;
        depleted = true;

        NetworkedPlayer.BroadcastResourceDepleted(transform.position);

        // Disable collider
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        // Hide visuals
        if (visualRoot != null)
        {
            foreach (var r in visualRoot.GetComponentsInChildren<Renderer>())
                r.enabled = false;
        }

        ResourceInfoUI.Instance?.HideIfShowing(this);

        // Release all gatherers and redirect
        List<Villager> toRedirect = new List<Villager>();
        for (int i = 0; i < maxGatherers; i++)
        {
            if (gathererSlots[i] != null)
            {
                toRedirect.Add(gathererSlots[i]);
                gathererSlots[i] = null;
            }
        }
        foreach (var v in overflowQueue)
        {
            if (v != null && !toRedirect.Contains(v))
                toRedirect.Add(v);
        }
        overflowQueue.Clear();

        foreach (Villager v in toRedirect)
        {
            if (v == null) continue;
            ResourceNode next = FindNearest(v.transform.position, resourceType, this, 50f);
            if (next != null) v.GatherFrom(next);
        }

        gameObject.SetActive(false);
        Destroy(gameObject, 0.3f);
    }

    // ── Finders ─────────────────────────────────────────────────────────

    public static ResourceNode FindNearest(Vector3 pos, ResourceType type, ResourceNode exclude = null, float maxDist = float.MaxValue)
    {
        ResourceNode best = null;
        float bestDist = float.MaxValue;

        foreach (ResourceNode n in RegisteredNodes)
        {
            if (n == null || n == exclude || n.depleted || n.isDead) continue;
            if (n.resourceType != type) continue;
            if (!n.HasAvailableSlot()) continue; // CRITICAL: only find resources with open slots
            float d = Vector3.Distance(pos, n.transform.position);
            if (d > maxDist) continue;
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }

    public static ResourceNode FindNearestAny(Vector3 pos, float maxDist, ResourceNode exclude = null)
    {
        ResourceNode best = null;
        float bestDist = float.MaxValue;

        foreach (ResourceNode n in RegisteredNodes)
        {
            if (n == null || n == exclude || n.depleted) continue;
            if (!n.HasAvailableSlot()) continue;
            float d = Vector3.Distance(pos, n.transform.position);
            if (d > maxDist) continue;
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }

    public static ResourceNode FindByPosition(Vector3 pos, float tolerance = 0.5f)
    {
        foreach (ResourceNode n in RegisteredNodes)
        {
            if (n == null) continue;
            if (Vector3.Distance(n.transform.position, pos) <= tolerance)
                return n;
        }
        return null;
    }

    public void DepleteClientSide()
    {
        if (depleted) return;
        depleted = true;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = false;
        if (visualRoot != null)
        {
            foreach (var r in visualRoot.GetComponentsInChildren<Renderer>())
                r.enabled = false;
        }
        ResourceInfoUI.Instance?.HideIfShowing(this);
    }

    // ── Properties ────────────────────────────────────────────────────
    public ResourceType ResourceType    => resourceType;
    public int          RemainingAmount => remainingAmount;
    public int          TotalAmount     => totalAmount;
    public int          MaxHealth       => maxHealth;
    public int          CurrentHealth   => currentHealth;
    public bool         RequiresKill    => requiresKill;
    public bool         IsKilled        => isDead || !requiresKill;
    public bool         IsEmpty         => depleted || remainingAmount <= 0;
    public int          GathererCount   => ActiveGathererCount();
    public int          MaxGatherers    => maxGatherers;
    public GameObject   VisualRoot      => visualRoot;
}