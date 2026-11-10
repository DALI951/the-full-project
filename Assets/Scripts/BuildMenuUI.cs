using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows build menu when a Villager is selected.
/// Upgraded with AoE4-style hotkeys ([Q], [W], [E], [R], [T], [Y], [U]),
/// hotkey badge displays, and stone wall placement hints.
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

    // Hotkey definitions for build actions
    private static readonly KeyCode[] BUILD_HOTKEYS = new KeyCode[]
    {
        KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y, KeyCode.U, KeyCode.I
    };

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

    private struct ButtonVisualState
    {
        public Image buttonImage;
        public Color originalImageColor;
        public TMP_Text[] texts;
        public Color[] originalTextColors;
    }
    private ButtonVisualState[] buttonVisuals;

    public static BuildEntry[] AllEntries => Instance != null ? Instance.buildings : null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        closeButton?.onClick.AddListener(Hide);
        buildPanel?.SetActive(false);
        tooltipPanel?.SetActive(false);
        SelectionManager.RegisterBlockingPanel(buildPanel?.GetComponent<RectTransform>());
    }

    private void Start()
    {
        // Cache visual states and listeners for build buttons
        if (buildButtons != null && buildButtons.Length > 0)
        {
            buttonVisuals = new ButtonVisualState[buildButtons.Length];

            for (int i = 0; i < buildButtons.Length && i < buildings.Length; i++)
            {
                if (buildButtons[i] == null) continue;
                int idx = i;

                buttonVisuals[i].buttonImage = buildButtons[i].GetComponent<Image>();
                buttonVisuals[i].originalImageColor = buttonVisuals[i].buttonImage != null
                    ? buttonVisuals[i].buttonImage.color
                    : Color.white;

                buttonVisuals[i].texts = buildButtons[i].GetComponentsInChildren<TMP_Text>(true);

                // Add AoE4 hotkey badge to button label if available
                if (i < BUILD_HOTKEYS.Length && buttonVisuals[i].texts != null && buttonVisuals[i].texts.Length > 0)
                {
                    TMP_Text label = buttonVisuals[i].texts[0];
                    if (label != null)
                    {
                        string keyStr = $"[{BUILD_HOTKEYS[i]}]";
                        if (!label.text.Contains(keyStr))
                        {
                            label.text = $"<color=#D4AF37><b>{keyStr}</b></color> {label.text}";
                        }
                    }
                }

                if (buttonVisuals[i].texts != null && buttonVisuals[i].texts.Length > 0)
                {
                    buttonVisuals[i].originalTextColors = new Color[buttonVisuals[i].texts.Length];
                    for (int t = 0; t < buttonVisuals[i].texts.Length; t++)
                    {
                        if (buttonVisuals[i].texts[t] != null)
                            buttonVisuals[i].originalTextColors[t] = buttonVisuals[i].texts[t].color;
                    }
                }

                // Click listener
                buildButtons[i].onClick.AddListener(() => OnBuildButtonClicked(idx));

                // Hover tooltips
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
    }

    private void Update()
    {
        if (buildPanel != null && buildPanel.activeSelf)
        {
            UpdateAffordabilityUI();
            HandleBuildHotkeys();
        }
    }

    private void HandleBuildHotkeys()
    {
        // Guard against typing in input fields
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null)
        {
            return;
        }

        if (buildButtons == null || buildings == null) return;

        for (int i = 0; i < BUILD_HOTKEYS.Length && i < buildButtons.Length && i < buildings.Length; i++)
        {
            if (buildButtons[i] == null || !buildButtons[i].gameObject.activeSelf) continue;

            if (Input.GetKeyDown(BUILD_HOTKEYS[i]))
            {
                OnBuildButtonClicked(i);
                break;
            }
        }
    }

    public void Show()
    {
        buildPanel?.SetActive(true);
        UpdateAffordabilityUI();
    }

    public void Hide()
    {
        buildPanel?.SetActive(false);
        HideBuildTooltip();
    }

    /// <summary>Checks whether local player can afford this building without spending resources.</summary>
    public bool CanAfford(BuildEntry entry)
    {
        if (entry == null) return false;

        if (Mirror.NetworkClient.active && NetworkedPlayer.LocalInstance != null)
        {
            return NetworkedPlayer.LocalInstance.Food >= entry.costFood &&
                   NetworkedPlayer.LocalInstance.Wood >= entry.costWood &&
                   NetworkedPlayer.LocalInstance.Gold >= entry.costGold;
        }

        if (ResourceManager.Instance != null)
        {
            return ResourceManager.Instance.Food >= entry.costFood &&
                   ResourceManager.Instance.Wood >= entry.costWood &&
                   ResourceManager.Instance.Gold >= entry.costGold;
        }

        return false;
    }

    public void OnBuildButtonClicked(int index)
    {
        if (index < 0 || index >= buildings.Length) return;
        BuildEntry entry = buildings[index];

        // Only check affordability - DO NOT spend resources here!
        if (!CanAfford(entry))
        {
            Debug.Log($"[BuildMenu] Not enough resources for {entry.buildingName}.");
            AudioManager.Instance?.PlayUIError();
            return;
        }

        if (entry.buildingName != null && entry.buildingName.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Debug.Log("[BuildMenu] Wall selected: Click & Drag on ground to build a connected Stone Wall line.");
        }

        BuildingPlacer.Instance?.StartPlacing(
            entry.buildingPrefab,
            entry.constructionSitePrefab,
            entry.buildTime,
            entry.ghostMaterial,
            entry.costFood, entry.costWood, entry.costGold);

        Hide();
    }

    /// <summary>Updates button visuals to reflect whether each building can currently be afforded.</summary>
    private void UpdateAffordabilityUI()
    {
        if (buttonVisuals == null || buildings == null) return;

        for (int i = 0; i < buttonVisuals.Length && i < buildings.Length; i++)
        {
            if (buildButtons[i] == null) continue;

            bool affordable = CanAfford(buildings[i]);

            // Dim or restore button background image
            if (buttonVisuals[i].buttonImage != null)
            {
                Color orig = buttonVisuals[i].originalImageColor;
                buttonVisuals[i].buttonImage.color = affordable
                    ? orig
                    : new Color(orig.r * 0.5f, orig.g * 0.5f, orig.b * 0.5f, orig.a * 0.65f);
            }

            // Tint cost / label text red if unaffordable
            if (buttonVisuals[i].texts != null)
            {
                for (int t = 0; t < buttonVisuals[i].texts.Length; t++)
                {
                    TMP_Text txt = buttonVisuals[i].texts[t];
                    if (txt == null) continue;
                    Color origText = (buttonVisuals[i].originalTextColors != null && t < buttonVisuals[i].originalTextColors.Length)
                        ? buttonVisuals[i].originalTextColors[t]
                        : Color.white;

                    txt.color = affordable ? origText : new Color(1f, 0.35f, 0.35f, 0.85f);
                }
            }
        }
    }

    // ── Build button tooltip ─────────────────────────────────────────────

    private void ShowBuildTooltip(int index)
    {
        if (index < 0 || index >= buildings.Length || tooltipPanel == null) return;
        BuildEntry e = buildings[index];

        string hotkeyPrefix = (index < BUILD_HOTKEYS.Length) ? $"[{BUILD_HOTKEYS[index]}] " : "";
        if (tooltipNameText) tooltipNameText.text = $"<color=#D4AF37>{hotkeyPrefix}</color>{e.buildingName}";

        var parts = new System.Collections.Generic.List<string>();
        if (e.costFood > 0) parts.Add($"Food: {e.costFood}");
        if (e.costWood > 0) parts.Add($"Wood: {e.costWood}");
        if (e.costGold > 0) parts.Add($"Gold: {e.costGold}");

        if (tooltipCostText)
        {
            tooltipCostText.text = parts.Count > 0 ? string.Join("  ", parts) : "Free";
            tooltipCostText.color = CanAfford(e) ? Color.white : new Color(1f, 0.35f, 0.35f);
        }

        string desc = e.description ?? "";
        if (e.buildingName != null && e.buildingName.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            desc += "\n<color=#D4AF37><b>Hint:</b> Click & Drag on ground to build a connected Stone Wall line</color>";
        }

        if (tooltipDescText) tooltipDescText.text = desc;

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
        UpdateAffordabilityUI();
    }

    public void RestoreAllButtons()
    {
        for (int i = 0; i < buildButtons.Length; i++)
            if (buildButtons[i] != null)
                buildButtons[i].gameObject.SetActive(true);
        UpdateAffordabilityUI();
    }

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
