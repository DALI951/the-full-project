using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>ResourceInfoUI v11 — X button, null guards, live refresh.</summary>
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

    private void Refresh()
    {
        if (trackedNode == null) return;

        if (nameText)      nameText.text      = trackedNode.gameObject.name;
        if (resourceText)  resourceText.text  = $"Gives: {trackedNode.ResourceType}";
        if (amountText)    amountText.text     = $"{trackedNode.RemainingAmount} / {trackedNode.TotalAmount}";
        if (gatherersText) gatherersText.text  = $"Gatherers: {trackedNode.GathererCount} / {trackedNode.MaxGatherers}";

        bool isAnimal = trackedNode.RequiresKill;
        if (healthBarGroup != null) healthBarGroup.SetActive(isAnimal);

        if (isAnimal)
        {
            if (trackedNode.IsKilled)
            {
                if (healthText)    healthText.text         = "Dead ✓ — gatherable";
                if (healthBarFill) healthBarFill.fillAmount = 0f;
            }
            else
            {
                float ratio = trackedNode.MaxHealth > 0
                    ? (float)trackedNode.CurrentHealth / trackedNode.MaxHealth : 0f;
                if (healthText)    healthText.text         = $"HP: {trackedNode.CurrentHealth} / {trackedNode.MaxHealth}";
                if (healthBarFill) healthBarFill.fillAmount = Mathf.Clamp01(ratio);
            }
        }
    }
}
