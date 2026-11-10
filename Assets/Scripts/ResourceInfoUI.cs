using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// ResourceInfoUI — resource deposit inspection panel with AoE4 Royal Slate-Navy & Gold styling,
/// providing rich lore and harvest metrics for trees, mines, farms, and wildlife.
/// </summary>
public class ResourceInfoUI : MonoBehaviour
{
    public static ResourceInfoUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Button     closeButton;

    [Header("Text Fields")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text resourceText;
    [SerializeField] private TMP_Text amountText;
    [SerializeField] private TMP_Text gatherersText;
    [SerializeField] private TMP_Text healthText;

    [Header("Health Bar (animals only)")]
    [SerializeField] private GameObject healthBarGroup;
    [SerializeField] private Image      healthBarFill;

    private ResourceNode trackedNode;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (panel != null) panel.SetActive(false);
        SelectionManager.RegisterBlockingPanel(panel?.GetComponent<RectTransform>());
    }

    private void Update()
    {
        // If the tracked node was destroyed (depleted), close the panel
        if (trackedNode == null && panel != null && panel.activeSelf)
            Hide();
        else if (trackedNode != null)
            Refresh();
    }

    // ─── AoE4 Resource Information ───────────────────────────────────────

    public static (string title, string givesDesc) GetAoE4ResourceDetails(ResourceNode node)
    {
        if (node == null) return ("", "");

        if (node.RequiresKill)
        {
            return (
                "Wild Game (Deer Herd)",
                "<color=#E5C158>Food Source</color> - Harvestable wildlife. Villagers hunt and field-dress for rich sustenance."
            );
        }

        switch (node.ResourceType)
        {
            case ResourceType.Wood:
                return (
                    "Forest Timber",
                    "<color=#E5C158>Wood Source</color> - Dense timber grove. Harvested for buildings, palisades, and military gear."
                );
            case ResourceType.Gold:
                return (
                    "Deep Gold Vein",
                    "<color=#E5C158>Gold Deposit</color> - Precious metal vein. Essential for military recruitment and Age advancement."
                );
            case ResourceType.Food:
                return (
                    "Agricultural Farm",
                    "<color=#E5C158>Renewable Food Plot</color> - Cultivated soil providing steady long-term nutrition."
                );
            default:
                return (
                    SanitizeName(node.gameObject.name),
                    $"<color=#E5C158>Resource Deposit</color> - Gives {node.ResourceType}."
                );
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────

    public void Show(ResourceNode node)
    {
        UnitInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.Hide();
        GameUI.Instance?.HideBuildingUI();
        if (node == null) return;
        trackedNode = node;
        UnitInfoUI.Instance?.Hide();
        Refresh();
        if (panel != null) panel.SetActive(true);
    }

    public void HideIfShowing(ResourceNode node)
    {
        if (trackedNode == node) Hide();
    }

    public void Hide()
    {
        trackedNode = null;
        if (panel != null) panel.SetActive(false);
    }

    // ─── Private ─────────────────────────────────────────────────────────

    public static string SanitizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string clean = raw.Replace("(Clone)", "").Trim();
        if (clean == "Animal") clean = "Deer";
        return clean;
    }

    private void Refresh()
    {
        if (trackedNode == null) return;

        var (title, givesDesc) = GetAoE4ResourceDetails(trackedNode);

        if (nameText)      nameText.text      = title;
        if (resourceText)  resourceText.text  = givesDesc;
        if (amountText)    amountText.text    = $"<color=#E5C158>Reserve:</color> {trackedNode.RemainingAmount} / {trackedNode.TotalAmount}";
        if (gatherersText) gatherersText.text = $"<color=#E5C158>Harvesters:</color> {trackedNode.GathererCount} / {trackedNode.MaxGatherers}";

        bool isAnimal = trackedNode.RequiresKill;
        if (healthBarGroup != null) healthBarGroup.SetActive(isAnimal);

        if (isAnimal)
        {
            if (trackedNode.IsKilled)
            {
                if (healthText)    healthText.text         = "<color=#88FF88>Carcase ready for harvest</color>";
                if (healthBarFill) healthBarFill.fillAmount = 0f;
            }
            else
            {
                float ratio = trackedNode.MaxHealth > 0
                    ? (float)trackedNode.CurrentHealth / trackedNode.MaxHealth : 0f;
                if (healthText)    healthText.text         = $"<color=#FFAA88>Wildlife HP:</color> {trackedNode.CurrentHealth} / {trackedNode.MaxHealth}";
                if (healthBarFill) healthBarFill.fillAmount = Mathf.Clamp01(ratio);
            }
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
