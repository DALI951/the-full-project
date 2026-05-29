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

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        closeButton?.onClick.AddListener(Hide);
        setSpawnPointButton?.onClick.AddListener(OnSetSpawnPointClicked);
        panel?.SetActive(false);
        SelectionManager.RegisterBlockingPanel(panel?.GetComponent<RectTransform>());
        deleteConstructionButton?.onClick.AddListener(OnDeleteConstructionClicked);
    }

    private void Update()
    {
        if (trackedSite     != null) RefreshSite();
        if (trackedBuilding != null) RefreshBuildingHealth();

        if (settingSpawnPoint && Input.GetMouseButtonDown(0))
        {
            if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    trackedBuilding?.SetSpawnPoint(hit.point);
                    MoveFlag.Instance?.ShowSpawnFlag(hit.point);
                    settingSpawnPoint = false;
                    wasSpawnPointSet = true;
                    statusClearTimer = 1.5f;
                    if (spawnPointStatusText != null)
                        spawnPointStatusText.text = "Spawn point set \u2713";
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

        bool over = UnityEngine.EventSystems.EventSystem.current != null
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
                spawnPointStatusText.text = "Spawn point set \u2713";
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

    public void ShowBuilding(Building building)
    {
        trackedBuilding = building;
        trackedSite     = null;

        UnitInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();
        ResourceBuildingUI.Instance?.Hide();

        bool isEnemy = building.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex;
        if (nameText) nameText.text = building.BuildingName;
        if (playerNameText)
        {
            NetworkedPlayer owner = NetworkedPlayer.Get(building.OwnerPlayerId);
            playerNameText.text = owner != null ? owner.displayName : $"Player {building.OwnerPlayerId + 1}";
        }
        if (functionText)
            functionText.text = building.BuildingDescription;
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

        panel?.SetActive(true);
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
        if (deleteConstructionButton) deleteConstructionButton.gameObject.SetActive(false);

        panel?.SetActive(true);
    }

    public void Hide()
    {
        trackedBuilding  = null;
        trackedSite      = null;
        settingSpawnPoint = false;
        hoveringSpawnButton = false;
        panel?.SetActive(false);
        MoveFlag.Instance?.ClearRallyFlag();
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

    private void OnDeleteConstructionClicked()
    {
        if (trackedSite == null) return;
        trackedSite.Demolish();
        trackedSite = null;
        Hide();
    }
}
