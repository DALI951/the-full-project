using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows build menu when a Villager is selected.
/// Attach to GameManager. Wire in Inspector.
/// </summary>
public class BuildMenuUI : MonoBehaviour
{
    public static BuildMenuUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject buildPanel;
    [SerializeField] private Button     closeButton;

    [Header("Building Entries")]
    [SerializeField] private BuildEntry[] buildings;

    [Header("Tooltip (assign a panel in the Canvas)")]
    [SerializeField] private GameObject tooltipPanel;
    [SerializeField] private TMP_Text   tooltipNameText;
    [SerializeField] private TMP_Text   tooltipCostText;
    [SerializeField] private TMP_Text   tooltipDescText;

    [Header("Build Buttons (same order as Buildings array)")]
    [SerializeField] private Button[] buildButtons;

    [System.Serializable]
    public class BuildEntry
    {
        public string      buildingName;
        public GameObject  buildingPrefab;
        public GameObject  constructionSitePrefab;
        public Material    ghostMaterial;
        public float       buildTime    = 10f;
        public int         costFood     = 0;
        public int         costWood     = 50;
        public int         costGold     = 0;
        public Sprite      icon;
        [TextArea(1, 3)]
        public string description = "";
    }

    public static BuildEntry[] AllEntries => Instance != null ? Instance.buildings : null;

    private void Start()
    {
        // Attach hover tooltips to build buttons
        for (int i = 0; i < buildButtons.Length && i < buildings.Length; i++)
        {
            if (buildButtons[i] == null) continue;
            int idx     = i;
            var trigger = buildButtons[i].gameObject
                            .AddComponent<UnityEngine.EventSystems.EventTrigger>();

            var enter = new UnityEngine.EventSystems.EventTrigger.Entry
                { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener((_) => ShowBuildTooltip(idx));
            trigger.triggers.Add(enter);

            var exit = new UnityEngine.EventSystems.EventTrigger.Entry
                { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener((_) => HideBuildTooltip());
            trigger.triggers.Add(exit);
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        closeButton?.onClick.AddListener(Hide);
        buildPanel?.SetActive(false);
        tooltipPanel?.SetActive(false);
        SelectionManager.RegisterBlockingPanel(buildPanel?.GetComponent<RectTransform>());
    }

    public void Show()
    {
        buildPanel?.SetActive(true);
    }

    public void Hide()
    {
        buildPanel?.SetActive(false);
        HideBuildTooltip();   
        BuildingPlacer.Instance?.CancelPlacement();
    }

    public void OnBuildButtonClicked(int index)
    {
        if (index < 0 || index >= buildings.Length) return;
        BuildEntry entry = buildings[index];

        if (Mirror.NetworkClient.active && NetworkedPlayer.LocalInstance != null)
        {
            NetworkedPlayer.LocalInstance.CmdStartPlacing(
                entry.buildingPrefab != null ? entry.buildingPrefab.name : "",
                entry.constructionSitePrefab,
                entry.buildTime,
                entry.costFood, entry.costWood, entry.costGold);
        }
        else
        {
            if (ResourceManager.Instance == null ||
                !ResourceManager.Instance.TrySpend(
                    entry.costFood, entry.costWood, entry.costGold))
            {
                Debug.Log("[BuildMenu] Not enough resources.");
                return;
            }
            BuildingPlacer.Instance?.StartPlacing(
                entry.buildingPrefab,
                entry.constructionSitePrefab,
                entry.buildTime,
                entry.ghostMaterial,
                entry.costFood, entry.costWood, entry.costGold);
        }

        Hide();
    }

    // ── Build button tooltip ─────────────────────────────────────────────

    private void ShowBuildTooltip(int index)
    {
        if (index < 0 || index >= buildings.Length || tooltipPanel == null) return;
        BuildEntry e = buildings[index];

        if (tooltipNameText) tooltipNameText.text = e.buildingName;

        var parts = new System.Collections.Generic.List<string>();
        if (e.costFood > 0) parts.Add($"Food: {e.costFood}");
        if (e.costWood > 0) parts.Add($"Wood: {e.costWood}");
        if (e.costGold > 0) parts.Add($"Gold: {e.costGold}");
        if (tooltipCostText)
            tooltipCostText.text = parts.Count > 0 ? string.Join("  ", parts) : "Free";

        if (tooltipDescText) tooltipDescText.text = e.description;

        tooltipPanel.SetActive(true);
    }

    private void HideBuildTooltip()
    {
        tooltipPanel?.SetActive(false);
    }

    /// <summary>Show only the first building entry (HomeSite) for Builder units.</summary>
    public void ShowForBuilder()
    {
        buildPanel?.SetActive(true);
        for (int i = 0; i < buildButtons.Length; i++)
            if (buildButtons[i] != null)
                buildButtons[i].gameObject.SetActive(i == 0); // only HomeSite (index 0)
    }

    public void RestoreAllButtons()
    {
        for (int i = 0; i < buildButtons.Length; i++)
            if (buildButtons[i] != null)
                buildButtons[i].gameObject.SetActive(true);
    }
}