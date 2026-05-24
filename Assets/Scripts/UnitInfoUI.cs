using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>UnitInfoUI v11 — X button wired, null guards throughout.</summary>
public class UnitInfoUI : MonoBehaviour
{
    public static UnitInfoUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Button     closeButton;

    [Header("Single Unit")]
    [SerializeField] private GameObject singleGroup;
    [SerializeField] private TMP_Text   nameText;
    [SerializeField] private TMP_Text   typeText;
    [SerializeField] private TMP_Text   descriptionText;
    [SerializeField] private TMP_Text   healthText;
    [SerializeField] private Image      healthBarFill;
    [SerializeField] private TMP_Text   playerNameText;

    [Header("Multi Unit")]
    [SerializeField] private GameObject multiGroup;
    [SerializeField] private TMP_Text   totalCountText;
    [SerializeField] private Transform  typeListContainer;
    [SerializeField] private GameObject typeLinePrefab;
    [SerializeField] private UnityEngine.UI.RawImage portraitDisplay;

    private Unit             trackedUnit;
    private List<GameObject> typeLines = new List<GameObject>();

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
        if (trackedUnit != null) RefreshHealth();
    }

    // ─── Public API ──────────────────────────────────────────────────────

    public void ShowUnit(Unit unit)
    {
        ResourceInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.Hide();
        ResourceBuildingUI.Instance?.Hide();
        if (unit == null) return;
        trackedUnit = unit;
        if (portraitDisplay != null) portraitDisplay.gameObject.SetActive(true);

        singleGroup?.SetActive(true);
        multiGroup?.SetActive(false);
        ClearTypeLines();

        if (nameText)        nameText.text        = unit.UnitName;
        if (playerNameText) playerNameText.text = "";
        if (typeText)        typeText.text         = unit.UnitType;
        if (descriptionText) descriptionText.text  = unit.UnitDescription;

        RefreshHealth();
        if (panel != null) panel.SetActive(true);
        if (unit.UnitType == "Builder")
            BuildMenuUI.Instance?.ShowForBuilder();
        else if (unit is Villager)
        {
            BuildMenuUI.Instance?.RestoreAllButtons();
            BuildMenuUI.Instance?.Show();
        }
        else
            BuildMenuUI.Instance?.Hide();

        panel.SetActive(true);
    }

    public void ShowEnemyUnit(Unit unit)
    {
        ResourceInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.Hide();
        ResourceBuildingUI.Instance?.Hide();
        if (unit == null) return;
        trackedUnit = unit;
        if (portraitDisplay != null) portraitDisplay.gameObject.SetActive(false);

        singleGroup?.SetActive(true);
        multiGroup?.SetActive(false);
        ClearTypeLines();

        if (nameText)        nameText.text        = unit.UnitName;
        if (playerNameText)  playerNameText.text  = $"Player {unit.OwnerPlayerId + 1}"; // placeholder
        if (typeText)        typeText.text         = unit.UnitType;
        if (descriptionText) descriptionText.text  = unit.UnitDescription;

        RefreshHealth();
        BuildMenuUI.Instance?.Hide();
        if (panel != null) panel.SetActive(true);
    }

    public void ShowMultiple(List<Unit> units)
    {
        ResourceBuildingUI.Instance?.Hide();
        trackedUnit = null;
        if (portraitDisplay != null) portraitDisplay.gameObject.SetActive(false);
        singleGroup?.SetActive(false);
        multiGroup?.SetActive(true);
        ClearTypeLines();

        int total = 0;
        Dictionary<string, int> counts = new Dictionary<string, int>();
        foreach (Unit u in units)
        {
            if (u == null) continue;
            total++;
            if (!counts.ContainsKey(u.UnitName)) counts[u.UnitName] = 0;
            counts[u.UnitName]++;
        }

        if (totalCountText != null)
            totalCountText.text = $"{total} units selected";

        foreach (var kvp in counts)
        {
            if (typeLinePrefab == null || typeListContainer == null) break;
            GameObject line = Instantiate(typeLinePrefab, typeListContainer);
            TMP_Text txt    = line.GetComponent<TMP_Text>();
            if (txt != null) txt.text = $"{kvp.Key}  ×  {kvp.Value}";
            typeLines.Add(line);
        }

        // Show type bar icons
        SelectionTypeBar.Instance?.ShowTypes(units);

        // Show build menu if any villagers
        bool hasVillager = false;
        foreach (Unit u in units)
            if (u is Villager) { hasVillager = true; break; }
        if (hasVillager) BuildMenuUI.Instance?.Show();
        else             BuildMenuUI.Instance?.Hide();

        panel.SetActive(true);
    }

    public void Hide()
    {
        trackedUnit = null;
        if (portraitDisplay != null) portraitDisplay.gameObject.SetActive(false);
        BuildMenuUI.Instance?.Hide();
        if (panel != null) panel.SetActive(false);
        ClearTypeLines();
    }

    // ─── Private ─────────────────────────────────────────────────────────

    private void RefreshHealth()
    {
        if (trackedUnit == null) return;
        float ratio = (float)trackedUnit.CurrentHealth / Mathf.Max(1, trackedUnit.MaxHealth);
        if (healthText)    healthText.text         = $"HP  {trackedUnit.CurrentHealth} / {trackedUnit.MaxHealth}";
        if (healthBarFill) healthBarFill.fillAmount = Mathf.Clamp01(ratio);
    }

    private void ClearTypeLines()
    {
        foreach (var go in typeLines) if (go) Destroy(go);
        typeLines.Clear();
    }
}
