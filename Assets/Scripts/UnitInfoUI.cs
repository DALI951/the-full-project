using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// UnitInfoUI — unit inspection panel with AoE4 Royal Slate-Navy & Burnished Gold styling,
/// providing rich unit class designations and tactical combat role summaries.
/// </summary>
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

    // ─── AoE4 Unit Class Information ─────────────────────────────────────

    public static (string classTitle, string description) GetAoE4UnitDetails(Unit unit)
    {
        if (unit == null) return ("", "");

        if (unit is Builder || (unit.UnitType != null && unit.UnitType.Contains("Builder")) || (unit.UnitName != null && unit.UnitName.Contains("Builder")))
        {
            return (
                "<color=#E5C158><b>MASTER BUILDER & ENGINEER</b></color>",
                "Worker & Builder — Gathers Food, Wood, Gold & constructs fortifications. Dedicated construction specialist with accelerated building speed."
            );
        }
        if (unit is Villager || (unit.UnitType != null && unit.UnitType.Contains("Villager")) || (unit.UnitName != null && unit.UnitName.Contains("Villager")))
        {
            return (
                "<color=#E5C158><b>WORKER & VILLAGER</b></color>",
                "Worker & Builder — Gathers Food, Wood, Gold & constructs fortifications. The economic backbone of the empire."
            );
        }
        if (unit is Infantry || (unit.UnitType != null && unit.UnitType.Contains("Infantry")) || (unit.UnitName != null && unit.UnitName.Contains("Infantry")))
        {
            return (
                "<color=#E5C158><b>HEAVY MELEE INFANTRY</b></color>",
                "Heavy Melee Infantry — Strong vs Spearmen & Buildings. Armed with broadsword and reinforced shield for frontline combat."
            );
        }
        if (unit is Cavalry || (unit.UnitType != null && unit.UnitType.Contains("Cavalry")) || (unit.UnitName != null && unit.UnitName.Contains("Cavalry")))
        {
            return (
                "<color=#E5C158><b>ROYAL LANCER CAVALRY</b></color>",
                "Royal Lancer Cavalry — Fast flanking shock unit. High mobility and devastating charge damage against unfortified enemies."
            );
        }

        string rawType = string.IsNullOrEmpty(unit.UnitType) ? unit.UnitName : unit.UnitType;
        string title = $"<color=#E5C158><b>{SanitizeName(rawType).ToUpper()}</b></color>";
        string desc = !string.IsNullOrEmpty(unit.UnitDescription)
            ? unit.UnitDescription
            : "Imperial Realm Unit — Trained at specialized kingdom military structures.";
        return (title, desc);
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

        var (classTitle, aoe4Desc) = GetAoE4UnitDetails(unit);

        if (nameText)        nameText.text        = SanitizeName(unit.UnitName);
        if (playerNameText)  playerNameText.text  = "";
        if (typeText)        typeText.text        = classTitle;
        if (descriptionText) descriptionText.text = aoe4Desc;

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

        var (classTitle, aoe4Desc) = GetAoE4UnitDetails(unit);

        if (nameText)        nameText.text        = SanitizeName(unit.UnitName);
        if (playerNameText)  playerNameText.text  = $"<color=#FF7777>Hostile Realm (Player {unit.OwnerPlayerId + 1})</color>";
        if (typeText)        typeText.text        = classTitle;
        if (descriptionText) descriptionText.text = aoe4Desc;

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
            string clean = SanitizeName(u.UnitName);
            if (!counts.ContainsKey(clean)) counts[clean] = 0;
            counts[clean]++;
        }

        if (totalCountText != null)
            totalCountText.text = $"<color=#E5C158><b>{total} Units Selected</b></color>";

        foreach (var kvp in counts)
        {
            if (typeLinePrefab == null || typeListContainer == null) break;
            GameObject line = Instantiate(typeLinePrefab, typeListContainer);
            TMP_Text txt    = line.GetComponent<TMP_Text>();
            if (txt != null) txt.text = $"<color=#E0C068>{kvp.Key}</color>  ×  <b>{kvp.Value}</b>";
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
        if (healthText)    healthText.text         = $"<color=#E5C158>HP</color>  {trackedUnit.CurrentHealth} / {trackedUnit.MaxHealth}";
        if (healthBarFill) healthBarFill.fillAmount = Mathf.Clamp01(ratio);
    }

    public static string SanitizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        string clean = raw.Replace("(Clone)", "").Trim();
        if (clean == "Animal") clean = "Deer";
        return clean;
    }

    private void ClearTypeLines()
    {
        foreach (var go in typeLines) if (go) Destroy(go);
        typeLines.Clear();
    }

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
