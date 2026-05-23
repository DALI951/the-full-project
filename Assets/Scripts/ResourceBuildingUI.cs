using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// ResourceBuildingUI — shows when clicking a Farm / Market / LumberMill.
/// Displays worker count, Get Villager, and eject buttons.
/// Attach to GameManager. Wire all references in Inspector.
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
    [SerializeField] private TMP_Text getVillagerLabel;    // optional override label
    [SerializeField] private Button   ejectVillagerButton;
    [SerializeField] private TMP_Text ejectVillagerLabel;  // shows "3 Working  (eject)"
    [SerializeField] private TMP_Text workerCountText;     // "Workers: 3 / 8"

    [Header("Notification (canvas-level element)")]
    [SerializeField] private GameObject notificationPanel;
    [SerializeField] private TMP_Text   notificationText;

    private ResourceBuilding trackedBuilding;
    private Coroutine        notifCoroutine;

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
    }

    private void Update()
    {
        if (trackedBuilding != null && infoPanel != null && infoPanel.activeSelf)
            Refresh();
    }

    // ── Public API ───────────────────────────────────────────────────────

    public void Show(ResourceBuilding building)
    {
        if (building == null) return;
        trackedBuilding = building;

        UnitInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();

        Refresh();
        infoPanel?.SetActive(true);
        buttonsPanel?.SetActive(true);
    }

    public void RefreshIfShowing(ResourceBuilding building)
    {
        if (trackedBuilding == building && infoPanel != null && infoPanel.activeSelf)
            Refresh();
    }

    public void Hide()
    {
        trackedBuilding = null;
        infoPanel?.SetActive(false);
        buttonsPanel?.SetActive(false);
    }

    /// <summary>Shows a timed toast notification (3 s).</summary>
    public void ShowNotification(string message)
    {
        if (notificationPanel == null || notificationText == null) return;
        notificationText.text = message;
        notificationPanel.SetActive(true);
        if (notifCoroutine != null) StopCoroutine(notifCoroutine);
        notifCoroutine = StartCoroutine(HideNotifAfter(3f));
    }

    // ── Private ──────────────────────────────────────────────────────────

    private void Refresh()
    {
        if (trackedBuilding == null) return;

        int count = trackedBuilding.WorkerCount;
        int max   = trackedBuilding.MaxWorkers;

        if (nameText)         nameText.text         = trackedBuilding.gameObject.name;
        if (resourceTypeText) resourceTypeText.text  = $"Generates: {trackedBuilding.GetResourceType}";
        if (workerCountText)  workerCountText.text   = $"Workers: {count} / {max}";

        if (statusText)
        {
            if (count == 0)   statusText.text = "Idle — assign villagers to start production";
            else if (count == max) statusText.text = $"Full — producing at max speed";
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
    }

    private void OnGetVillagerClicked()  => trackedBuilding?.GetClosestIdleVillager();
    private void OnEjectVillagerClicked() => trackedBuilding?.EjectOneVillager();

    private IEnumerator HideNotifAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        notificationPanel?.SetActive(false);
    }
}