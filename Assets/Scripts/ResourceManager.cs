using UnityEngine;
using Mirror;

/// <summary>
/// ResourceManager — tracks Food, Wood, and Gold.
/// A singleton so any script can read/write resources via ResourceManager.Instance.
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
        food = Mathf.Max(0, food + addFood);
        wood = Mathf.Max(0, wood + addWood);
        gold = Mathf.Max(0, gold + addGold);

        ResourceUI.Instance?.Refresh(food, wood, gold);
    }

    /// <summary>Returns true and deducts cost if the player can afford it.</summary>
    public bool TrySpend(int costFood, int costWood, int costGold)
    {
        if (food < costFood || wood < costWood || gold < costGold)
        {
            Debug.Log("[ResourceManager] Not enough resources.");
            return false;
        }

        AddResources(-costFood, -costWood, -costGold);
        return true;
    }
    public bool CanAddPopulation(int amount = 1)
        => currentPopulation + amount <= maxPopulation;

    public void AddPopulation(int amount = 1)
    {
        currentPopulation = Mathf.Clamp(currentPopulation + amount, 0, maxPopulation);
        ResourceUI.Instance?.RefreshPopulation(currentPopulation, maxPopulation);
    }

    public void RemovePopulation(int amount = 1)
    {
        currentPopulation = Mathf.Max(0, currentPopulation - amount);
        ResourceUI.Instance?.RefreshPopulation(currentPopulation, maxPopulation);
    }
    // ─── Properties ──────────────────────────────────────────────────────
    public int Food => food;
    public int Wood => wood;
    public int Gold => gold;
    public int CurrentPopulation => currentPopulation;
    public int MaxPopulation     => maxPopulation;
}
