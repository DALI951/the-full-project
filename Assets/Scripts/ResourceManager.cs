using UnityEngine;
using Mirror;

/// <summary>
/// ResourceManager — tracks Food, Wood, and Gold.
/// A singleton so any script can read/write resources via ResourceManager.Instance.
/// Synchronizes bidirectionally with NetworkedPlayer.LocalInstance in multiplayer and host mode.
/// </summary>
public class ResourceManager : MonoBehaviour
{
    // ─── Singleton ───────────────────────────────────────────────────────
    public static ResourceManager Instance { get; private set; }

    // ─── Inspector Fields ────────────────────────────────────────────────
    [Header("Starting Resources")]
    [SerializeField] private int startingFood  = 200;
    [SerializeField] private int startingWood  = 150;
    [SerializeField] private int startingGold  = 100;
    [Header("Population")]
    [SerializeField] private int maxPopulation = 20;
    private int currentPopulation = 0;

    // ─── Runtime State ───────────────────────────────────────────────────
    private int food;
    private int wood;
    private int gold;

    // ─── Unity Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        food = startingFood;
        wood = startingWood;
        gold = startingGold;
    }

    private void Start()
    {
        NetworkedPlayer np = NetworkedPlayer.LocalInstance;
        if (np != null)
        {
            food = np.Food;
            wood = np.Wood;
            gold = np.Gold;
            currentPopulation = np.CurrentPopulation;
            maxPopulation = np.MaxPopulation;
        }
        ResourceUI.Instance?.Refresh(food, wood, gold);
        ResourceUI.Instance?.RefreshPopulation(currentPopulation, maxPopulation);
    }

    private void Update()
    {
        NetworkedPlayer np = NetworkedPlayer.LocalInstance;
        if (np == null) return;
        if (food != np.Food || wood != np.Wood || gold != np.Gold)
        {
            food = np.Food;
            wood = np.Wood;
            gold = np.Gold;
            ResourceUI.Instance?.Refresh(food, wood, gold);
        }
        if (currentPopulation != np.CurrentPopulation || maxPopulation != np.MaxPopulation)
        {
            currentPopulation = np.CurrentPopulation;
            maxPopulation = np.MaxPopulation;
            ResourceUI.Instance?.RefreshPopulation(currentPopulation, maxPopulation);
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────

    /// <summary>Add amounts to each resource (use negative to subtract).</summary>
    public void AddResources(int addFood, int addWood, int addGold)
    {
        NetworkedPlayer np = NetworkedPlayer.LocalInstance;
        if (np != null && NetworkServer.active)
        {
            np.AddResources(addFood, addWood, addGold);
            food = np.Food;
            wood = np.Wood;
            gold = np.Gold;
        }
        else
        {
            food = Mathf.Max(0, food + addFood);
            wood = Mathf.Max(0, wood + addWood);
            gold = Mathf.Max(0, gold + addGold);
        }

        ResourceUI.Instance?.Refresh(food, wood, gold);
    }

    /// <summary>Returns true and deducts cost if the player can afford it.</summary>
    public bool TrySpend(int costFood, int costWood, int costGold)
    {
        NetworkedPlayer np = NetworkedPlayer.LocalInstance;
        if (np != null && NetworkServer.active)
        {
            if (!np.TrySpend(costFood, costWood, costGold))
            {
                Debug.Log("[ResourceManager] Not enough resources (networked).");
                return false;
            }
            food = np.Food;
            wood = np.Wood;
            gold = np.Gold;
            ResourceUI.Instance?.Refresh(food, wood, gold);
            return true;
        }

        if (food < costFood || wood < costWood || gold < costGold)
        {
            Debug.Log("[ResourceManager] Not enough resources.");
            return false;
        }

        food = Mathf.Max(0, food - costFood);
        wood = Mathf.Max(0, wood - costWood);
        gold = Mathf.Max(0, gold - costGold);

        ResourceUI.Instance?.Refresh(food, wood, gold);
        return true;
    }

    public bool CanAddPopulation(int amount = 1)
    {
        NetworkedPlayer np = NetworkedPlayer.LocalInstance;
        if (np != null) return np.CanAddPopulation(amount);
        return currentPopulation + amount <= maxPopulation;
    }

    public void AddPopulation(int amount = 1)
    {
        NetworkedPlayer np = NetworkedPlayer.LocalInstance;
        if (np != null && NetworkServer.active)
        {
            np.AddPopulation(amount);
            currentPopulation = np.CurrentPopulation;
            maxPopulation = np.MaxPopulation;
        }
        else
        {
            currentPopulation = Mathf.Clamp(currentPopulation + amount, 0, maxPopulation);
        }
        ResourceUI.Instance?.RefreshPopulation(currentPopulation, maxPopulation);
    }

    public void RemovePopulation(int amount = 1)
    {
        NetworkedPlayer np = NetworkedPlayer.LocalInstance;
        if (np != null && NetworkServer.active)
        {
            np.RemovePopulation(amount);
            currentPopulation = np.CurrentPopulation;
            maxPopulation = np.MaxPopulation;
        }
        else
        {
            currentPopulation = Mathf.Max(0, currentPopulation - amount);
        }
        ResourceUI.Instance?.RefreshPopulation(currentPopulation, maxPopulation);
    }

    // ─── Properties ──────────────────────────────────────────────────────
    public int Food => (NetworkedPlayer.LocalInstance != null) ? NetworkedPlayer.LocalInstance.Food : food;
    public int Wood => (NetworkedPlayer.LocalInstance != null) ? NetworkedPlayer.LocalInstance.Wood : wood;
    public int Gold => (NetworkedPlayer.LocalInstance != null) ? NetworkedPlayer.LocalInstance.Gold : gold;
    public int CurrentPopulation => (NetworkedPlayer.LocalInstance != null) ? NetworkedPlayer.LocalInstance.CurrentPopulation : currentPopulation;
    public int MaxPopulation     => (NetworkedPlayer.LocalInstance != null) ? NetworkedPlayer.LocalInstance.MaxPopulation : maxPopulation;

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
}
