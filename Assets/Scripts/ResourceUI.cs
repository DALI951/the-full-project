using UnityEngine;
using TMPro;

/// <summary>
/// ResourceUI — displays Food, Wood, Gold, and Population counters with AoE4 Royal styling
/// and responsive color flash feedback when resource totals increase or decrease.
/// </summary>
public class ResourceUI : MonoBehaviour
{
    // ─── Singleton ───────────────────────────────────────────────────────
    public static ResourceUI Instance { get; private set; }

    // ─── Inspector References ────────────────────────────────────────────
    [Header("Resource Labels")]
    [SerializeField] private TMP_Text foodText;
    [SerializeField] private TMP_Text woodText;
    [SerializeField] private TMP_Text goldText;
    [SerializeField] private TMP_Text populationText;

    // Previous values for flash detection
    private int lastFood = -1;
    private int lastWood = -1;
    private int lastGold = -1;
    private int lastPopCurrent = -1;
    private int lastPopMax = -1;

    // Flash timers & colors
    private float foodFlashTimer = 0f;
    private Color foodFlashColor = Color.white;
    private float woodFlashTimer = 0f;
    private Color woodFlashColor = Color.white;
    private float goldFlashTimer = 0f;
    private Color goldFlashColor = Color.white;
    private float popFlashTimer = 0f;
    private Color popFlashColor = Color.white;

    private const float FLASH_DURATION = 0.65f;
    private static readonly Color COLOR_INCREASE = new Color(0.35f, 1.0f, 0.45f, 1f);
    private static readonly Color COLOR_DECREASE = new Color(1.0f, 0.40f, 0.40f, 1f);
    private static readonly Color COLOR_DEFAULT  = new Color(0.96f, 0.94f, 0.88f, 1f);

    // ─── Unity Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        // Animate color flash decays smoothly back to default
        if (foodFlashTimer > 0f)
        {
            foodFlashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(foodFlashTimer / FLASH_DURATION);
            if (foodText != null) foodText.color = Color.Lerp(COLOR_DEFAULT, foodFlashColor, t);
        }

        if (woodFlashTimer > 0f)
        {
            woodFlashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(woodFlashTimer / FLASH_DURATION);
            if (woodText != null) woodText.color = Color.Lerp(COLOR_DEFAULT, woodFlashColor, t);
        }

        if (goldFlashTimer > 0f)
        {
            goldFlashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(goldFlashTimer / FLASH_DURATION);
            if (goldText != null) goldText.color = Color.Lerp(COLOR_DEFAULT, goldFlashColor, t);
        }

        if (popFlashTimer > 0f)
        {
            popFlashTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(popFlashTimer / FLASH_DURATION);
            if (populationText != null) populationText.color = Color.Lerp(COLOR_DEFAULT, popFlashColor, t);
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────

    /// <summary>Called by ResourceManager every time resources change.</summary>
    public void Refresh(int food, int wood, int gold)
    {
        // Check for changes and trigger color flash
        if (lastFood >= 0 && food != lastFood)
        {
            foodFlashColor = food > lastFood ? COLOR_INCREASE : COLOR_DECREASE;
            foodFlashTimer = FLASH_DURATION;
        }
        if (lastWood >= 0 && wood != lastWood)
        {
            woodFlashColor = wood > lastWood ? COLOR_INCREASE : COLOR_DECREASE;
            woodFlashTimer = FLASH_DURATION;
        }
        if (lastGold >= 0 && gold != lastGold)
        {
            goldFlashColor = gold > lastGold ? COLOR_INCREASE : COLOR_DECREASE;
            goldFlashTimer = FLASH_DURATION;
        }

        lastFood = food;
        lastWood = wood;
        lastGold = gold;

        if (foodText) foodText.text = $"<color=#E5C158><b>FOOD:</b></color> {food}";
        if (woodText) woodText.text = $"<color=#E5C158><b>WOOD:</b></color> {wood}";
        if (goldText) goldText.text = $"<color=#E5C158><b>GOLD:</b></color> {gold}";
    }

    public void RefreshPopulation(int current, int max)
    {
        if (lastPopCurrent >= 0 && current != lastPopCurrent)
        {
            popFlashColor = current > lastPopCurrent ? COLOR_INCREASE : COLOR_DECREASE;
            popFlashTimer = FLASH_DURATION;
        }

        lastPopCurrent = current;
        lastPopMax = max;

        if (populationText)
        {
            if (current >= max && max > 0)
                populationText.text = $"<color=#E5C158><b>POP:</b></color> <color=#FF6666>{current}/{max}</color>";
            else
                populationText.text = $"<color=#E5C158><b>POP:</b></color> {current}/{max}";
        }
    }

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
