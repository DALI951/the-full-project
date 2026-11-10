using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class BuildingInfoUI : MonoBehaviour
{
    public static BuildingInfoUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Button     closeButton;

    [Header("Info Fields")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image    healthBarFill;
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private TMP_Text functionText;

    [Header("Spawn Point")]
    [SerializeField] private Button   setSpawnPointButton;
    [SerializeField] private TMP_Text spawnPointStatusText;

    [Header("Construction")]
    [SerializeField] private GameObject constructionGroup;
    [SerializeField] private TMP_Text   progressText;
    [SerializeField] private Image      progressBarFill;

    [Header("Construction Site Controls")]
    [SerializeField] private Button deleteConstructionButton;

    private Building         trackedBuilding;
    private ConstructionSite trackedSite;
    private bool             settingSpawnPoint = false;
    private bool             wasSpawnPointSet = false;
    private bool             hoveringSpawnButton = false;
    private float            statusClearTimer = 0f;

    // ── Dynamic AoE4 Actions Panel ──────────────────────────────────────────
    private GameObject actionsPanel;
    private Transform  actionsContainer;
    private TMP_Text   actionsHeader;
    private readonly List<GameObject> activeActionButtons = new List<GameObject>();
    private readonly List<Building.BuildingAction> currentActions = new List<Building.BuildingAction>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        closeButton?.onClick.AddListener(Hide);
        setSpawnPointButton?.onClick.AddListener(OnSetSpawnPointClicked);
        panel?.SetActive(false);
        SelectionManager.RegisterBlockingPanel(panel?.GetComponent<RectTransform>());
        deleteConstructionButton?.onClick.AddListener(OnDeleteConstructionClicked);

        EnsureActionsPanel();
    }

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

    private void EnsureActionsPanel()
    {
        if (actionsPanel != null) return;
        if (panel == null) return;

        Transform parentCanvas = panel.transform.parent;
        actionsPanel = new GameObject("BuildingActionsPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        actionsPanel.transform.SetParent(parentCanvas, false);

        RectTransform rt = actionsPanel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        
        RectTransform panelRt = panel.GetComponent<RectTransform>();
        float panelLeft = panelRt != null ? panelRt.anchoredPosition.x - (panelRt.sizeDelta.x * 0.5f) : (34.8f - 117.5f);
        float actionWidth = 230f;
        float panelY = panelRt != null ? panelRt.anchoredPosition.y : 0f;
        float panelH = panelRt != null && panelRt.sizeDelta.y > 0 ? panelRt.sizeDelta.y : 180f;

        rt.anchoredPosition = new Vector2(panelLeft - (actionWidth * 0.5f) - 6f, panelY);
        rt.sizeDelta = new Vector2(actionWidth, panelH);

        Image bg = actionsPanel.GetComponent<Image>();
        bg.color = new Color(0.18f, 0.12f, 0.08f, 0.95f);

        // Header
        GameObject hdrObj = new GameObject("ActionHeader", typeof(RectTransform), typeof(TextMeshProUGUI));
        hdrObj.transform.SetParent(actionsPanel.transform, false);
        RectTransform hdrRt = hdrObj.GetComponent<RectTransform>();
        hdrRt.anchorMin = new Vector2(0f, 1f);
        hdrRt.anchorMax = new Vector2(1f, 1f);
        hdrRt.pivot = new Vector2(0.5f, 1f);
        hdrRt.anchoredPosition = new Vector2(0f, -4f);
        hdrRt.sizeDelta = new Vector2(210f, 22f);

        actionsHeader = hdrObj.GetComponent<TextMeshProUGUI>();
        actionsHeader.text = "BUILDING ACTIONS";
        actionsHeader.fontSize = 11.5f;
        actionsHeader.fontStyle = FontStyles.Bold;
        actionsHeader.alignment = TextAlignmentOptions.Center;
        actionsHeader.color = new Color(1f, 0.85f, 0.4f, 1f);
        if (nameText != null && nameText.font != null) actionsHeader.font = nameText.font;

        // Scroll Area
        GameObject scrollObj = new GameObject("ScrollArea", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(Mask));
        scrollObj.transform.SetParent(actionsPanel.transform, false);
        RectTransform scrollRt = scrollObj.GetComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0f, 0f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.offsetMin = new Vector2(6f, 6f);
        scrollRt.offsetMax = new Vector2(-6f, -28f);

        Image scrollMaskImg = scrollObj.GetComponent<Image>();
        scrollMaskImg.color = new Color(0f, 0f, 0f, 0.01f);
        Mask mask = scrollObj.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        ScrollRect scrollRect = scrollObj.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 25f;

        // Content
        GameObject contentObj = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
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

        scrollRect.content = contentRt;
        actionsContainer = contentObj.transform;

        actionsPanel.SetActive(false);
        SelectionManager.RegisterBlockingPanel(rt);
    }

    private void Update()
    {
        if (trackedSite     != null) RefreshSite();
        if (trackedBuilding != null)
        {
            RefreshBuildingHealth();
            RefreshTowerStats();
            RefreshActionButtons();
        }

        if (settingSpawnPoint && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
        {
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray();
                if (Camera.main != null && Physics.Raycast(ray, out RaycastHit hit))
                {
                    if (trackedBuilding != null)
                    {
                        trackedBuilding.SetSpawnPoint(hit.point);
                        if (NetworkedPlayer.LocalInstance != null)
                        {
                            var ni = trackedBuilding.GetComponent<Mirror.NetworkIdentity>();
                            if (ni != null)
                                NetworkedPlayer.LocalInstance.CmdSetRallyPoint(ni.netId, hit.point);
                        }
                    }
                    MoveFlag.Instance?.ShowSpawnFlag(hit.point);
                    settingSpawnPoint = false;
                    wasSpawnPointSet = true;
                    statusClearTimer = 1.5f;
                    if (spawnPointStatusText != null)
                        spawnPointStatusText.text = "Spawn point set";
                }
            }
        }
        else if (trackedBuilding != null && trackedBuilding.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex && Input.GetMouseButtonDown(1))
        {
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                if (SelectionManager.Instance == null || SelectionManager.Instance.SelectedUnits.Count == 0)
                {
                    Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray();
                    if (Camera.main != null && Physics.Raycast(ray, out RaycastHit hit))
                    {
                        trackedBuilding.SetSpawnPoint(hit.point);
                        if (NetworkedPlayer.LocalInstance != null)
                        {
                            var ni = trackedBuilding.GetComponent<Mirror.NetworkIdentity>();
                            if (ni != null)
                                NetworkedPlayer.LocalInstance.CmdSetRallyPoint(ni.netId, hit.point);
                        }
                        MoveFlag.Instance?.ShowSpawnFlag(hit.point);
                        wasSpawnPointSet = true;
                        statusClearTimer = 1.5f;
                        if (spawnPointStatusText != null)
                            spawnPointStatusText.text = "Spawn point set";
                    }
                }
            }
        }

        if (panel == null || !panel.activeSelf || setSpawnPointButton == null || spawnPointStatusText == null)
            return;

        if (statusClearTimer > 0f)
        {
            statusClearTimer -= Time.deltaTime;
            if (statusClearTimer <= 0f && !hoveringSpawnButton)
            {
                spawnPointStatusText.text = "";
                wasSpawnPointSet = false;
            }
            return;
        }

        bool over = EventSystem.current != null
            && RectTransformUtility.RectangleContainsScreenPoint(
                setSpawnPointButton.GetComponent<RectTransform>(),
                Input.mousePosition,
                null);

        if (over && !hoveringSpawnButton)
        {
            hoveringSpawnButton = true;
            if (settingSpawnPoint)
                spawnPointStatusText.text = "Click on the map to set spawn point...";
            else if (wasSpawnPointSet)
                spawnPointStatusText.text = "Spawn point set";
            else
                spawnPointStatusText.text = "Click to set spawn point";
        }
        else if (!over && hoveringSpawnButton)
        {
            hoveringSpawnButton = false;
            if (!wasSpawnPointSet || statusClearTimer <= 0f)
                spawnPointStatusText.text = "";
        }
    }

    public void Show(Building b) => ShowBuilding(b);

    public static string SanitizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string clean = raw.Replace("(Clone)", "").Trim();
        return clean;
    }

    public void ShowBuilding(Building building)
    {
        trackedBuilding = building;
        trackedSite     = null;

        UnitInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();
        ResourceBuildingUI.Instance?.Hide();

        bool isEnemy = building.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex;
        if (nameText) nameText.text = SanitizeName(building.BuildingName);
        if (building.HasCustomRallyPoint && !isEnemy)
        {
            MoveFlag.Instance?.ShowSpawnFlag(building.RallyPointPosition);
        }
        if (playerNameText)
        {
            NetworkedPlayer owner = NetworkedPlayer.Get(building.OwnerPlayerId);
            playerNameText.text = owner != null ? owner.displayName : $"Player {building.OwnerPlayerId + 1}";
        }
        
        RefreshTowerStats();

        if (constructionGroup) constructionGroup.SetActive(false);
        hoveringSpawnButton = false;
        wasSpawnPointSet = false;
        statusClearTimer = 0f;
        if (setSpawnPointButton) setSpawnPointButton.gameObject.SetActive(!isEnemy);
        if (spawnPointStatusText)
        {
            spawnPointStatusText.gameObject.SetActive(!isEnemy);
            if (!isEnemy) spawnPointStatusText.text = "";
        }
        if (constructionGroup)    constructionGroup.SetActive(false);
        if (deleteConstructionButton) deleteConstructionButton.gameObject.SetActive(false);

        EnsureActionsPanel();
        if (actionsPanel != null && panel != null)
        {
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            RectTransform actRt = actionsPanel.GetComponent<RectTransform>();
            if (panelRt != null && actRt != null)
            {
                float panelLeft = panelRt.anchoredPosition.x - (panelRt.sizeDelta.x * 0.5f);
                float actionWidth = 230f;
                actRt.anchoredPosition = new Vector2(panelLeft - (actionWidth * 0.5f) - 6f, panelRt.anchoredPosition.y);
                actRt.sizeDelta = new Vector2(actionWidth, panelRt.sizeDelta.y > 0 ? panelRt.sizeDelta.y : 180f);
            }
        }

        if (isEnemy)
        {
            actionsPanel?.SetActive(false);
            ClearActionButtons();
        }
        else
        {
            RebuildActionButtons(building);
            actionsPanel?.SetActive(true);
        }

        panel?.SetActive(true);
    }

    private void RefreshTowerStats()
    {
        if (trackedBuilding == null) return;
        TowerAttack ta = trackedBuilding.GetComponent<TowerAttack>();
        if (ta != null && functionText != null)
        {
            functionText.text = SanitizeTmpText($"Watchtower Defense\nDamage: {ta.attackDamage}  |  Range: {ta.attackRange:F1}m  |  Rate: {(1f / Mathf.Max(0.1f, ta.attackCooldown)):F1}/s");
        }
        else if (functionText != null)
        {
            functionText.text = SanitizeTmpText(trackedBuilding.BuildingDescription);
        }
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
            actionsHeader.text = SanitizeTmpText($"{SanitizeName(building.BuildingName).ToUpper()} ACTIONS");
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

        // Try cloning from setSpawnPointButton if available for consistent styling
        if (setSpawnPointButton != null)
        {
            btnObj = Instantiate(setSpawnPointButton.gameObject, parent);
            btnObj.SetActive(true);
            var le = btnObj.GetComponent<LayoutElement>();
            if (le == null) le = btnObj.AddComponent<LayoutElement>();
            le.minHeight = 44f;
            le.preferredHeight = 48f;
            le.flexibleWidth = 1f;
        }
        else
        {
            btnObj = new GameObject("ActionButton_" + act.id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
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
            RefreshActionButtons();
            RefreshTowerStats();
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

    public void ShowConstructionSite(ConstructionSite site)
    {
        trackedSite     = site;
        trackedBuilding = null;

        UnitInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();
        GameUI.Instance?.HideBuildingUI();

        if (nameText)            nameText.text       = "Under Construction...";
        if (playerNameText)      playerNameText.text  = "";
        if (functionText)        functionText.text    = "";
        if (healthText)          healthText.text      = "";
        if (healthBarFill)       healthBarFill.fillAmount = 0f;

        if (setSpawnPointButton) setSpawnPointButton.gameObject.SetActive(false);
        if (spawnPointStatusText) spawnPointStatusText.gameObject.SetActive(false);
        if (constructionGroup)   constructionGroup.SetActive(true);

        if (deleteConstructionButton)
        {
            int localPid = NetworkedPlayer.LocalInstance != null ? NetworkedPlayer.LocalInstance.playerIndex : 0;
            int siteOwner = GetSiteOwnerId(site);
            deleteConstructionButton.gameObject.SetActive(siteOwner == localPid);
        }

        actionsPanel?.SetActive(false);
        ClearActionButtons();

        panel?.SetActive(true);
    }

    private static int GetSiteOwnerId(ConstructionSite site)
    {
        if (site == null) return -1;
        var prop = site.GetType().GetProperty("ownerPlayerId") ?? site.GetType().GetProperty("OwnerPlayerId");
        if (prop != null) return (int)prop.GetValue(site);
        var field = site.GetType().GetField("ownerPlayerId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                 ?? site.GetType().GetField("pendingOwnerId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null) return (int)field.GetValue(site);
        return -1;
    }

    public void Hide()
    {
        trackedBuilding  = null;
        trackedSite      = null;
        settingSpawnPoint = false;
        hoveringSpawnButton = false;
        actionsPanel?.SetActive(false);
        ClearActionButtons();
        panel?.SetActive(false);
        MoveFlag.Instance?.ClearRallyFlag();
    }

    public void ShowNotification(string message)
    {
        ResourceBuildingUI.Instance?.ShowNotification(message);
    }

    private void RefreshSite()
    {
        if (trackedSite == null) { Hide(); return; }
        float ratio = trackedSite.BuildTime > 0
            ? trackedSite.Progress / trackedSite.BuildTime : 0f;
        if (progressBarFill) progressBarFill.fillAmount = Mathf.Clamp01(ratio);
        if (progressText)
            progressText.text = trackedSite.HasActiveBuilders
                ? "Building..."
                : "Waiting for builders...";
        if (healthText)
            healthText.text = $"HP: {trackedSite.CurrentHealth} / {trackedSite.MaxHealth}";
        if (healthBarFill)
            healthBarFill.fillAmount = Mathf.Clamp01(ratio);
    }

    public void OnSetSpawnPointClicked()
    {
        settingSpawnPoint = true;
        if (spawnPointStatusText)
            spawnPointStatusText.text = "Click on the map to set spawn point...";
    }

    public void HideIfOpen()
    {
        if (panel != null && panel.activeSelf) Hide();
    }
    
    private void RefreshBuildingHealth()
    {
        if (trackedBuilding == null) return;
        float ratio = trackedBuilding.MaxBuildingHealth > 0
            ? (float)trackedBuilding.CurrentBuildingHealth / trackedBuilding.MaxBuildingHealth : 0f;
        if (healthText)    healthText.text         = $"HP: {trackedBuilding.CurrentBuildingHealth} / {trackedBuilding.MaxBuildingHealth}";
        if (healthBarFill) healthBarFill.fillAmount = Mathf.Clamp01(ratio);
    }

    public void OnDeleteClicked() => OnDeleteConstructionClicked();

    private void OnDeleteConstructionClicked()
    {
        if (trackedSite == null) return;
        if (NetworkedPlayer.LocalInstance != null && trackedSite.GetComponent<Mirror.NetworkIdentity>() != null)
        {
            NetworkedPlayer.LocalInstance.CmdCancelBuildingSite(trackedSite.GetComponent<Mirror.NetworkIdentity>().netId);
        }
        else
        {
            trackedSite.Demolish();
        }
        trackedSite = null;
        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ClearActionButtons();
        if (actionsPanel != null) Destroy(actionsPanel);
    }
}
