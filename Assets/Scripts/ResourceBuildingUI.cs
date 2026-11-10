using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// ResourceBuildingUI — shows when clicking a Farm / Market / LumberMill.
/// Displays worker count, Get Villager, eject buttons, and AoE4 trading & tech tree actions.
/// </summary>
public class ResourceBuildingUI : MonoBehaviour
{
    public static ResourceBuildingUI Instance { get; private set; }

    [Header("Info Panel (center)")]
    [SerializeField] private GameObject infoPanel;
    [SerializeField] private Button     closeButton;

    [Header("Buttons Panel (left)")]
    [SerializeField] private GameObject buttonsPanel;
    [Header("Info Labels")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text resourceTypeText;
    [SerializeField] private TMP_Text statusText;

    [Header("Worker Controls")]
    [SerializeField] private Button   getVillagerButton;
    [SerializeField] private TMP_Text getVillagerLabel;
    [SerializeField] private Button   ejectVillagerButton;
    [SerializeField] private TMP_Text ejectVillagerLabel;
    [SerializeField] private TMP_Text workerCountText;

    [Header("Notification (canvas-level element)")]
    [SerializeField] private GameObject notificationPanel;
    [SerializeField] private TMP_Text   notificationText;

    private ResourceBuilding trackedBuilding;
    private Coroutine        notifCoroutine;

    // ── Dynamic AoE4 Actions Area ──────────────────────────────────────────
    private GameObject actionsPanel;
    private Transform  actionsContainer;
    private TMP_Text   actionsHeader;
    private readonly List<GameObject> activeActionButtons = new List<GameObject>();
    private readonly List<Building.BuildingAction> currentActions = new List<Building.BuildingAction>();

    public static string SanitizeTmpText(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return raw.Replace("→", "->")
                  .Replace("←", "<-")
                  .Replace("—", "-")
                  .Replace("–", "-")
                  .Replace("✓", "[Done]")
                  .Replace("★", "*")
                  .Replace("•", "*");
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        closeButton?.onClick.AddListener(Hide);
        getVillagerButton?.onClick.AddListener(OnGetVillagerClicked);
        ejectVillagerButton?.onClick.AddListener(OnEjectVillagerClicked);

        infoPanel?.SetActive(false);
        buttonsPanel?.SetActive(false);
        notificationPanel?.SetActive(false);

        SelectionManager.RegisterBlockingPanel(infoPanel?.GetComponent<RectTransform>());
        SelectionManager.RegisterBlockingPanel(buttonsPanel?.GetComponent<RectTransform>());

        EnsureActionsContainer();
    }

    private void EnsureActionsContainer()
    {
        if (actionsPanel != null) return;
        if (infoPanel == null && buttonsPanel == null) return;

        // Position the upgrade/actions panel cleanly beside infoPanel in CenterZone
        Transform parentCanvas = null;
        if (infoPanel != null)
        {
            parentCanvas = infoPanel.transform.parent != null && infoPanel.transform.parent.parent != null
                ? infoPanel.transform.parent.parent
                : (infoPanel.transform.parent ?? infoPanel.transform);
        }
        else if (buttonsPanel != null)
        {
            parentCanvas = buttonsPanel.transform.parent;
        }
        else
        {
            parentCanvas = transform;
        }

        actionsPanel = new GameObject("ResourceBuildingActionsPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        actionsPanel.transform.SetParent(parentCanvas, false);

        RectTransform rt = actionsPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        RectTransform infoRt = infoPanel != null ? infoPanel.GetComponent<RectTransform>() : null;
        if (infoPanel != null && infoPanel.transform.parent != null && infoPanel.transform.parent is RectTransform parentRt && parentRt.gameObject.name != "CenterZone")
        {
            infoRt = parentRt;
        }

        float infoRight = infoRt != null ? (infoRt.anchoredPosition.x + (infoRt.sizeDelta.x * 0.5f)) : (-193.6f + 111f);
        float actionWidth = 230f;
        float panelY = infoRt != null ? infoRt.anchoredPosition.y : 0f;
        float panelH = infoRt != null && infoRt.sizeDelta.y > 0 ? infoRt.sizeDelta.y : 180f;

        rt.anchoredPosition = new Vector2(infoRight + (actionWidth * 0.5f) + 6f, panelY);
        rt.sizeDelta = new Vector2(actionWidth, panelH);

        Image bg = actionsPanel.GetComponent<Image>();
        bg.color = new Color(0.18f, 0.12f, 0.08f, 0.95f);

        // Action Header
        GameObject hdrObj = new GameObject("ResourceActionHeader", typeof(RectTransform), typeof(TextMeshProUGUI));
        hdrObj.transform.SetParent(actionsPanel.transform, false);
        RectTransform hdrRt = hdrObj.GetComponent<RectTransform>();
        hdrRt.anchorMin = new Vector2(0f, 1f);
        hdrRt.anchorMax = new Vector2(1f, 1f);
        hdrRt.pivot = new Vector2(0.5f, 1f);
        hdrRt.anchoredPosition = new Vector2(0f, -4f);
        hdrRt.sizeDelta = new Vector2(210f, 22f);

        actionsHeader = hdrObj.GetComponent<TextMeshProUGUI>();
        actionsHeader.text = "TECH & TRADING";
        actionsHeader.fontSize = 11.5f;
        actionsHeader.fontStyle = FontStyles.Bold;
        actionsHeader.alignment = TextAlignmentOptions.Center;
        actionsHeader.color = new Color(1f, 0.85f, 0.4f, 1f);
        if (nameText != null && nameText.font != null) actionsHeader.font = nameText.font;

        // Scroll Area for Action Buttons
        GameObject scrollObj = new GameObject("ActionScrollArea", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
        scrollObj.transform.SetParent(actionsPanel.transform, false);
        RectTransform scrollRt = scrollObj.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.offsetMin = new Vector2(6f, 6f);
        scrollRt.offsetMax = new Vector2(-6f, -28f);

        Image maskImg = scrollObj.GetComponent<Image>();
        maskImg.color = new Color(0f, 0f, 0f, 0.01f);
        Mask mask = scrollObj.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        ScrollRect sr = scrollObj.GetComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 25f;

        // Content
        GameObject contentObj = new GameObject("ActionContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObj.transform.SetParent(scrollObj.transform, false);
        RectTransform contentRt = contentObj.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        VerticalLayoutGroup vlg = contentObj.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.spacing = 5f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        ContentSizeFitter csf = contentObj.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        sr.content = contentRt;
        actionsContainer = contentObj.transform;

        actionsPanel.SetActive(false);
        SelectionManager.RegisterBlockingPanel(rt);
    }

    private void Update()
    {
        if (trackedBuilding != null && infoPanel != null && infoPanel.activeSelf)
        {
            Refresh();
        }
    }

    // ── Public API ───────────────────────────────────────────────────────

    public void Show(ResourceBuilding building)
    {
        if (building == null) return;
        trackedBuilding = building;

        UnitInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();

        if (Mirror.NetworkClient.active && NetworkedPlayer.LocalInstance != null)
        {
            NetworkedPlayer.LocalInstance.CmdRequestResourceBuildingWorkers(building.transform.position);
        }

        EnsureActionsContainer();

        Building bld = building.GetComponent<Building>();
        if (bld != null)
        {
            RebuildActionButtons(bld);
        }
        else
        {
            ClearActionButtons();
        }

        Refresh();
        infoPanel?.SetActive(true);
        buttonsPanel?.SetActive(true);
        actionsPanel?.SetActive(true);
    }

    public void RefreshIfShowing(ResourceBuilding building)
    {
        if (trackedBuilding == building && infoPanel != null && infoPanel.activeSelf)
            Refresh();
    }

    public void Hide()
    {
        trackedBuilding = null;
        ClearActionButtons();
        actionsPanel?.SetActive(false);
        infoPanel?.SetActive(false);
        buttonsPanel?.SetActive(false);
    }

    /// <summary>Shows a timed toast notification (3 s).</summary>
    public void ShowNotification(string message)
    {
        if (notificationPanel == null || notificationText == null) return;
        notificationText.text = SanitizeTmpText(message);
        notificationPanel.SetActive(true);
        if (notifCoroutine != null) StopCoroutine(notifCoroutine);
        notifCoroutine = StartCoroutine(HideNotifAfter(3f));
    }

    // ── Private ──────────────────────────────────────────────────────────

    public static string SanitizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string clean = raw.Replace("(Clone)", "").Trim();
        return clean;
    }

    private void Refresh()
    {
        if (trackedBuilding == null) return;

        int count = trackedBuilding.WorkerCount;
        int max   = trackedBuilding.MaxWorkers;

        Building bld = trackedBuilding.GetComponent<Building>();
        string rawName = (bld != null && !string.IsNullOrEmpty(bld.BuildingName)) ? bld.BuildingName : trackedBuilding.gameObject.name;
        if (nameText)         nameText.text         = SanitizeTmpText(SanitizeName(rawName));
        if (resourceTypeText) resourceTypeText.text  = $"Generates: {trackedBuilding.GetResourceType}";
        if (workerCountText)  workerCountText.text   = $"Workers: {count} / {max}";

        if (statusText)
        {
            if (count == 0)   statusText.text = "Idle - assign villagers to start production";
            else if (count == max) statusText.text = "Full - producing at max speed";
            else              statusText.text = $"Producing ({count} villager{(count > 1 ? "s" : "")})";
        }

        // Get Villager button
        if (getVillagerButton) getVillagerButton.interactable = count < max;
        if (getVillagerLabel)  getVillagerLabel.text = "Get Villager";

        // Eject button — label shows current worker count
        if (ejectVillagerButton) ejectVillagerButton.interactable = count > 0;
        if (ejectVillagerLabel)
            ejectVillagerLabel.text = count > 0
                ? $"{count} Working\n(click to eject)"
                : "0 Working";

        RefreshActionButtons();
    }

    private void RebuildActionButtons(Building building)
    {
        ClearActionButtons();
        if (building == null || actionsContainer == null) return;

        List<Building.BuildingAction> actions = building.GetAvailableActions();
        if (actions == null || actions.Count == 0)
        {
            actionsPanel?.SetActive(false);
            return;
        }

        if (actionsHeader != null)
        {
            if (building.IsMarketBuilding) actionsHeader.text = "MARKET TRADING & GUILDS";
            else if (building.IsLumberMillBuilding) actionsHeader.text = "LUMBER MILL UPGRADES";
            else if (building.IsFarmBuilding) actionsHeader.text = "FARM TECH & HARVEST";
            else actionsHeader.text = SanitizeTmpText($"{SanitizeName(building.BuildingName).ToUpper()} ACTIONS");
        }

        foreach (var act in actions)
        {
            currentActions.Add(act);
            CreateActionButton(actionsContainer, act);
        }

        actionsPanel?.SetActive(true);
    }

    private void CreateActionButton(Transform parent, Building.BuildingAction act)
    {
        GameObject btnObj = null;

        if (getVillagerButton != null)
        {
            btnObj = Instantiate(getVillagerButton.gameObject, parent);
            btnObj.SetActive(true);
            var le = btnObj.GetComponent<LayoutElement>();
            if (le == null) le = btnObj.AddComponent<LayoutElement>();
            le.minHeight = 44f;
            le.preferredHeight = 48f;
            le.flexibleWidth = 1f;
        }
        else
        {
            btnObj = new GameObject("ResourceActionButton_" + act.id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnObj.transform.SetParent(parent, false);

            Image img = btnObj.GetComponent<Image>();
            img.color = new Color(0.28f, 0.20f, 0.12f, 0.95f);

            Button b = btnObj.GetComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = new Color(0.32f, 0.22f, 0.14f, 1f);
            cb.highlightedColor = new Color(0.50f, 0.36f, 0.22f, 1f);
            cb.pressedColor = new Color(0.20f, 0.14f, 0.08f, 1f);
            cb.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
            b.colors = cb;

            LayoutElement le = btnObj.GetComponent<LayoutElement>();
            le.minHeight = 44f;
            le.preferredHeight = 48f;
            le.flexibleWidth = 1f;

            GameObject txtObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtObj.transform.SetParent(btnObj.transform, false);
            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmp = txtObj.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 11f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (nameText != null && nameText.font != null) tmp.font = nameText.font;
        }

        activeActionButtons.Add(btnObj);

        Button btn = btnObj.GetComponent<Button>();
        TMP_Text txt = btnObj.GetComponentInChildren<TMP_Text>();
        if (txt != null)
        {
            if (nameText != null && nameText.font != null && txt.font == null)
                txt.font = nameText.font;

            txt.enableAutoSizing = true;
            txt.fontSizeMin = 9f;
            txt.fontSizeMax = 12f;
            txt.alignment = TextAlignmentOptions.Center;
            txt.enableWordWrapping = true;
            txt.overflowMode = TextOverflowModes.Ellipsis;
            txt.margin = new Vector4(4f, 2f, 4f, 2f);

            string rawText = act.getDynamicLabel != null ? act.getDynamicLabel() : act.title;
            txt.text = SanitizeTmpText(rawText);
        }

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            act.onExecute?.Invoke();
            Refresh();
        });
    }

    private void RefreshActionButtons()
    {
        for (int i = 0; i < currentActions.Count && i < activeActionButtons.Count; i++)
        {
            var act = currentActions[i];
            var btnObj = activeActionButtons[i];
            if (btnObj == null || act == null) continue;

            Button btn = btnObj.GetComponent<Button>();
            TMP_Text txt = btnObj.GetComponentInChildren<TMP_Text>();

            if (txt != null && act.getDynamicLabel != null)
                txt.text = SanitizeTmpText(act.getDynamicLabel());

            bool canAfford = Building.CanAfford(act.costFood, act.costWood, act.costGold);
            bool available = act.isAvailable == null || act.isAvailable();
            if (btn != null)
                btn.interactable = canAfford && available;
        }
    }

    private void ClearActionButtons()
    {
        foreach (var go in activeActionButtons)
        {
            if (go != null) Destroy(go);
        }
        activeActionButtons.Clear();
        currentActions.Clear();
    }

    private void OnGetVillagerClicked()
    {
        if (trackedBuilding == null) return;
        if (Mirror.NetworkClient.active && NetworkedPlayer.LocalInstance != null)
        {
            NetworkedPlayer.LocalInstance.CmdAddWorkerToResourceBuilding(trackedBuilding.transform.position);
        }
        else
        {
            trackedBuilding.GetClosestIdleVillager();
        }
    }

    private void OnEjectVillagerClicked()
    {
        if (trackedBuilding == null) return;
        if (Mirror.NetworkClient.active && NetworkedPlayer.LocalInstance != null)
        {
            NetworkedPlayer.LocalInstance.CmdEjectWorkerFromResourceBuilding(trackedBuilding.transform.position);
        }
        else
        {
            trackedBuilding.EjectOneVillager();
        }
    }

    private IEnumerator HideNotifAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        notificationPanel?.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ClearActionButtons();
        if (actionsPanel != null) Destroy(actionsPanel);
    }
}
