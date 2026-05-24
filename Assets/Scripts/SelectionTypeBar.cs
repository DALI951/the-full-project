using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shows icons at the top of the center panel when multiple types selected.
/// Clicking an icon shows info for that type.
/// Attach to GameManager.
/// </summary>
public class SelectionTypeBar : MonoBehaviour
{
    public static SelectionTypeBar Instance { get; private set; }

    [Header("Type Bar")]
    [SerializeField] private GameObject typeBarPanel;
    [SerializeField] private Transform  iconContainer;
    [SerializeField] private GameObject typeIconPrefab; // Button with Image + Text

    [System.Serializable]
    public class TypeIcon
    {
        public string   unitName;
        public Sprite   icon;
    }

    [SerializeField] private TypeIcon[] typeIcons; // assign in Inspector

    private List<GameObject> spawnedIcons = new List<GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        typeBarPanel?.SetActive(false);
    }

    public void ShowTypes(List<Unit> units)
    {
        foreach (var g in spawnedIcons) if (g) Destroy(g);
        spawnedIcons.Clear();

        // Count by type
        Dictionary<string, List<Unit>> groups = new Dictionary<string, List<Unit>>();
        foreach (Unit u in units)
        {
            if (u == null) continue;
            if (!groups.ContainsKey(u.UnitName))
                groups[u.UnitName] = new List<Unit>();
            groups[u.UnitName].Add(u);
        }

        if (groups.Count <= 1) { typeBarPanel?.SetActive(false); return; }

        foreach (var kvp in groups)
        {
            if (typeIconPrefab == null || iconContainer == null) continue;

            GameObject iconGO = Instantiate(typeIconPrefab, iconContainer);
            spawnedIcons.Add(iconGO);

            // Set count text
            TMP_Text txt = iconGO.GetComponentInChildren<TMP_Text>();
            if (txt) txt.text = $"{kvp.Key}\n×{kvp.Value.Count}";

            // Set icon image if found
            Image img = iconGO.transform.Find("Icon")?.GetComponent<Image>();
            if (img != null)
            {
                foreach (TypeIcon ti in typeIcons)
                    if (ti.unitName == kvp.Key && ti.icon != null)
                    { img.sprite = ti.icon; break; }
            }

            // Click → show info for this type
            string  capturedName  = kvp.Key;
            List<Unit> capturedList = kvp.Value;
            Button btn = iconGO.GetComponent<Button>();
            btn?.onClick.AddListener(() =>
            {
                if (capturedList.Count == 1)
                    UnitInfoUI.Instance?.ShowUnit(capturedList[0]);
                else
                    UnitInfoUI.Instance?.ShowMultiple(capturedList);

                bool hasVillager = capturedName == "Villager";
                if (hasVillager) BuildMenuUI.Instance?.Show();
                else             BuildMenuUI.Instance?.Hide();
            });
        }

        typeBarPanel?.SetActive(true);
    }

    public void Hide()
    {
        foreach (var g in spawnedIcons) if (g) Destroy(g);
        spawnedIcons.Clear();
        typeBarPanel?.SetActive(false);
    }
}