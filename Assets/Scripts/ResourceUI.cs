using UnityEngine;
using TMPro;

/// <summary>
/// ResourceUI — displays Food, Wood, and Gold counters at the top of the screen.
/// Refreshed automatically whenever ResourceManager changes a value.
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

    // ─── Unity Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ─── Public API ──────────────────────────────────────────────────────

    /// <summary>Called by ResourceManager every time resources change.</summary>
    public void Refresh(int food, int wood, int gold)
    {
        if (foodText) foodText.text = $"Food: {food}";
        if (woodText) woodText.text = $"Wood: {wood}";
        if (goldText) goldText.text = $"Gold: {gold}";
    }
    public void RefreshPopulation(int current, int max)
    {
        if (populationText) populationText.text = $"Pop: {current}/{max}";
    }
}
