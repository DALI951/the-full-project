using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// WorldHealthBar — lightweight, zero-prefab-dependency overhead health bar.
/// Billboards to Camera.main, dynamically adjusts color and fill ratio,
/// and hides when at full health unless selected.
/// </summary>
public class WorldHealthBar : MonoBehaviour
{
    [SerializeField] private float heightOffset = 2.0f;
    [SerializeField] private float barWidth     = 1.0f;
    [SerializeField] private float barHeight    = 0.12f;

    private GameObject    canvasGO;
    private RectTransform canvasRect;
    private RectTransform fillTransform;
    private Image         bgImage;
    private Image         fillImage;

    private Unit             cachedUnit;
    private Building         cachedBuilding;
    private ConstructionSite cachedSite;

    public float HeightOffset
    {
        get => heightOffset;
        set { heightOffset = value; if (canvasGO != null) canvasGO.transform.localPosition = new Vector3(0f, heightOffset, 0f); }
    }

    public float BarWidth
    {
        get => barWidth;
        set { barWidth = value; if (canvasRect != null) canvasRect.sizeDelta = new Vector2(barWidth, barHeight); }
    }

    private void Awake()
    {
        cachedUnit     = GetComponent<Unit>();
        cachedBuilding = GetComponent<Building>();
        cachedSite     = GetComponent<ConstructionSite>();

        // Auto-detect height from collider if not custom configured
        if (heightOffset <= 2.0f)
        {
            Collider col = GetComponentInChildren<Collider>();
            if (col != null)
            {
                float boundsHeight = col.bounds.size.y;
                if (boundsHeight > heightOffset)
                    heightOffset = boundsHeight + 0.4f;
            }
        }

        CreateHealthBarUI();
    }

    public void Configure(float offset, float width)
    {
        heightOffset = offset;
        barWidth     = width;
        if (canvasRect != null)
        {
            canvasRect.sizeDelta = new Vector2(barWidth, barHeight);
            canvasGO.transform.localPosition = new Vector3(0f, heightOffset, 0f);
        }
    }

    private void CreateHealthBarUI()
    {
        if (canvasGO != null) return;

        canvasGO = new GameObject("OverheadHealthBar");
        canvasGO.transform.SetParent(transform, false);
        canvasGO.transform.localPosition = new Vector3(0f, heightOffset, 0f);

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.WorldSpace;
        canvas.sortingOrder = 50;

        canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(barWidth, barHeight);

        // Dark background / border
        GameObject bgGO = new GameObject("Background");
        bgGO.transform.SetParent(canvasGO.transform, false);
        RectTransform bgRect = bgGO.AddComponent<RectTransform>();
        bgRect.anchorMin        = Vector2.zero;
        bgRect.anchorMax        = Vector2.one;
        bgRect.sizeDelta        = Vector2.zero;
        bgRect.anchoredPosition = Vector2.zero;

        bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0.04f, 0.05f, 0.08f, 0.88f);

        // Health fill
        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(bgGO.transform, false);
        fillTransform = fillGO.AddComponent<RectTransform>();
        fillTransform.anchorMin = new Vector2(0f, 0f);
        fillTransform.anchorMax = new Vector2(0f, 1f);
        fillTransform.pivot     = new Vector2(0f, 0.5f);

        float padding = Mathf.Min(0.02f, barHeight * 0.15f);
        fillTransform.anchoredPosition = new Vector2(padding, 0f);
        fillTransform.sizeDelta = new Vector2(barWidth - padding * 2f, barHeight - padding * 2f);

        fillImage = fillGO.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.95f, 0.25f, 1f);

        canvasGO.SetActive(false);
    }

    private void LateUpdate()
    {
        if (canvasGO == null) return;

        // Hide if dying or dead
        if (cachedUnit != null && cachedUnit.IsDead)
        {
            if (canvasGO.activeSelf) canvasGO.SetActive(false);
            return;
        }

        float currentHp = 0f;
        float maxHp = 1f;
        bool isSelected = false;

        if (cachedUnit != null)
        {
            currentHp  = cachedUnit.CurrentHealth;
            maxHp      = cachedUnit.MaxHealth;
            isSelected = cachedUnit.IsSelected;
        }
        else if (cachedBuilding != null)
        {
            currentHp  = cachedBuilding.CurrentBuildingHealth;
            maxHp      = cachedBuilding.MaxBuildingHealth;
            isSelected = SelectionManager.Instance != null && SelectionManager.Instance.SelectedBuilding == cachedBuilding;
        }
        else if (cachedSite != null)
        {
            currentHp  = cachedSite.CurrentHealth;
            maxHp      = cachedSite.MaxHealth;
            isSelected = SelectionManager.Instance != null && SelectionManager.Instance.SelectedBuilding == null;
        }
        else
        {
            cachedUnit     = GetComponent<Unit>();
            cachedBuilding = GetComponent<Building>();
            cachedSite     = GetComponent<ConstructionSite>();
            return;
        }

        if (maxHp <= 0f) maxHp = 1f;
        float ratio = Mathf.Clamp01(currentHp / maxHp);

        // Visibility rule: Hidden when at full health (>= 0.999f) AND not selected;
        // automatically visible whenever damaged (< 0.999f) or currently selected!
        bool isFullHealth = ratio >= 0.999f;
        bool shouldShow   = !isFullHealth || isSelected;

        if (canvasGO.activeSelf != shouldShow)
            canvasGO.SetActive(shouldShow);

        if (!shouldShow) return;

        // Keep billboard facing Camera.main
        Camera cam = Camera.main;
        if (cam != null)
        {
            canvasGO.transform.rotation = cam.transform.rotation;
        }

        // Keep position cleanly tracked
        canvasGO.transform.position = transform.position + Vector3.up * heightOffset;

        // Update fill bar size and gradient color
        float padding = Mathf.Min(0.02f, barHeight * 0.15f);
        float usableWidth = Mathf.Max(0f, barWidth - padding * 2f);
        float fillWidth = usableWidth * ratio;

        fillTransform.sizeDelta = new Vector2(fillWidth, Mathf.Max(0.01f, barHeight - padding * 2f));
        fillImage.color = GetHealthColor(ratio);
    }

    /// <summary>
    /// Smoothly interpolates health color:
    /// > 60%: Gold/Yellow to Bright Green
    /// 30%–60%: Crimson Red to Gold/Yellow
    /// < 30%: Crimson Red
    /// </summary>
    public static Color GetHealthColor(float ratio)
    {
        if (ratio > 0.6f)
        {
            float t = (ratio - 0.6f) / 0.4f;
            return Color.Lerp(new Color(1f, 0.85f, 0.15f, 1f), new Color(0.2f, 0.95f, 0.25f, 1f), t);
        }
        else if (ratio >= 0.3f)
        {
            float t = (ratio - 0.3f) / 0.3f;
            return Color.Lerp(new Color(0.95f, 0.18f, 0.15f, 1f), new Color(1f, 0.85f, 0.15f, 1f), t);
        }
        else
        {
            return new Color(0.95f, 0.18f, 0.15f, 1f);
        }
    }

    /// <summary>
    /// Ensures a WorldHealthBar component exists on the target GameObject.
    /// </summary>
    public static WorldHealthBar EnsureOn(GameObject go, float heightOffset = 2.0f, float width = 1.0f)
    {
        if (go == null) return null;
        WorldHealthBar bar = go.GetComponent<WorldHealthBar>();
        if (bar == null)
        {
            bar = go.AddComponent<WorldHealthBar>();
            bar.Configure(heightOffset, width);
        }
        return bar;
    }

    // ── Auto-attachment manager for existing and spawned units/buildings ──
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void BootstrapAutoAttacher()
    {
        GameObject mgr = new GameObject("[WorldHealthBarManager]");
        Object.DontDestroyOnLoad(mgr);
        mgr.AddComponent<WorldHealthBarAutoAttacher>();
    }
}

/// <summary>
/// Background helper component that automatically ensures newly spawned
/// Units, Buildings, and ConstructionSites receive overhead WorldHealthBars.
/// </summary>
public class WorldHealthBarAutoAttacher : MonoBehaviour
{
    private float nextScanTime = 0f;

    private void Update()
    {
        if (Time.time < nextScanTime) return;
        nextScanTime = Time.time + 0.5f;

        Unit[] units = Object.FindObjectsOfType<Unit>();
        for (int i = 0; i < units.Length; i++)
        {
            if (units[i] == null) continue;
            WorldHealthBar.EnsureOn(units[i].gameObject, 2.2f, 1.1f);
        }

        Building[] buildings = Object.FindObjectsOfType<Building>();
        for (int i = 0; i < buildings.Length; i++)
        {
            if (buildings[i] == null) continue;
            WorldHealthBar.EnsureOn(buildings[i].gameObject, 4.0f, 2.5f);
        }

        ConstructionSite[] sites = Object.FindObjectsOfType<ConstructionSite>();
        for (int i = 0; i < sites.Length; i++)
        {
            if (sites[i] == null) continue;
            WorldHealthBar.EnsureOn(sites[i].gameObject, 2.5f, 1.8f);
        }
    }
}
