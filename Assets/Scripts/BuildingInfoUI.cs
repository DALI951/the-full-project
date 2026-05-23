using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows building info in the center panel.
/// Displays name, health bar, and spawn point button.
/// Attach to GameManager.
/// </summary>
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

    // Construction progress (for sites)
    [Header("Construction")]
    [SerializeField] private GameObject constructionGroup;
    [SerializeField] private TMP_Text   progressText;
    [SerializeField] private Image      progressBarFill;

    [Header("Construction Site Controls")]
    [SerializeField] private Button deleteConstructionButton;

    private Building         trackedBuilding;
    private ConstructionSite trackedSite;
    private bool             settingSpawnPoint = false;

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

        // Click ground to set spawn point
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
                    if (spawnPointStatusText != null)
                        spawnPointStatusText.text = "Spawn point set ✓";
                }
            }
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
        if (setSpawnPointButton) setSpawnPointButton.gameObject.SetActive(!isEnemy);
        if (spawnPointStatusText)
        {
            spawnPointStatusText.gameObject.SetActive(!isEnemy);
            if (!isEnemy) spawnPointStatusText.text = "Click to set spawn point";
        }
        if (spawnPointStatusText) spawnPointStatusText.gameObject.SetActive(!isEnemy);
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

    private void OnSetSpawnPointClicked()
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