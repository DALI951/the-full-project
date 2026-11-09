using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// AoE4HUDThemer — automatically applies the unmistakable Age of Empires IV visual identity
/// across the GameScene Canvas:
/// - Deep Royal Slate-Navy panel backgrounds (0.06, 0.09, 0.15, 0.94)
/// - Warm burnished gold panel outlines (0.78, 0.64, 0.28, 0.85)
/// - Top-Center AoE4 Age & Kingdom Banner with:
///   1. Current Age banner ("I — DARK AGE", "II — FEUDAL AGE", etc., reading from AgeManager)
///   2. Quick "IDLE VILLAGER [.]" counter button (selects and centers camera on idle villagers)
///   3. Quick "ADVANCE AGE" button with resource validation, fanfare, and tooltips
/// </summary>
public class AoE4HUDThemer : MonoBehaviour
{
    public static AoE4HUDThemer Instance { get; private set; }

    // AoE4 Signature Color Palette
    public static readonly Color RoyalSlateNavy       = new Color(0.06f, 0.09f, 0.15f, 0.94f);
    public static readonly Color RoyalSlateNavyOpaque = new Color(0.06f, 0.09f, 0.15f, 0.98f);
    public static readonly Color BurnishedGold        = new Color(0.78f, 0.64f, 0.28f, 0.85f);
    public static readonly Color BurnishedGoldFull    = new Color(0.85f, 0.72f, 0.32f, 1.0f);
    public static readonly Color DeepNavyButton       = new Color(0.10f, 0.14f, 0.22f, 0.95f);
    public static readonly Color WarmGoldText         = new Color(0.95f, 0.82f, 0.38f, 1f);
    public static readonly Color LightParchment       = new Color(0.95f, 0.93f, 0.88f, 1f);

    // Historic AoE4 Advancement Costs: (Food, Gold) indexed by target Age (1..4)
    public static readonly (int food, int gold)[] AgeAdvancementCosts = new (int, int)[]
    {
        (0, 0),        // 0 unused
        (0, 0),        // Age 1: Dark Age (Starting)
        (500, 200),    // Age 2: Feudal Age
        (1000, 500),   // Age 3: Castle Age
        (2000, 1000)   // Age 4: Imperial Age
    };

    private Canvas targetCanvas;
    private GameObject topBannerObj;
    private TMP_Text ageDisplayText;
    private TMP_Text idleVillagerText;
    private Image idleVillagerImage;
    private Outline idleVillagerOutline;
    private TMP_Text advanceAgeText;
    private Image advanceAgeImage;
    private Outline advanceAgeOutline;
    private Button advanceAgeBtn;

    private int lastIdleVillagerIndex = -1;
    private float themeCheckTimer = 0f;
    private TMP_FontAsset sharedFontAsset;

    // ── Bootstrap ────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Bootstrap()
    {
        EnsureInstance();
    }

    public static void EnsureInstance()
    {
        if (Instance != null) return;

        GameObject host = GameObject.Find("[AoE4_HUD_Theme]");
        if (host == null)
        {
            host = new GameObject("[AoE4_HUD_Theme]");
            DontDestroyOnLoad(host);
        }

        if (host.GetComponent<AoE4HUDThemer>() == null)
            host.AddComponent<AoE4HUDThemer>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        topBannerObj = null;
        targetCanvas = null;
        ApplyTheme();
    }

    private void Start()
    {
        ApplyTheme();
    }

    private void Update()
    {
        // Re-check panel styling periodically in case panels opened or loaded dynamically
        themeCheckTimer += Time.deltaTime;
        if (themeCheckTimer >= 1.5f)
        {
            themeCheckTimer = 0f;
            ApplyPanelTheme();
        }

        UpdateTopBanner();
        HandleIdleVillagerHotkey();
    }

    // ── Age Advancement Helpers ──────────────────────────────────────────

    public static string GetCurrentAgeDisplay()
    {
        if (AgeManager.Instance != null)
        {
            int age = AgeManager.Instance.CurrentAge;
            return age switch
            {
                1 => "I — DARK AGE",
                2 => "II — FEUDAL AGE",
                3 => "III — CASTLE AGE",
                4 => "IV — IMPERIAL AGE",
                _ => AgeManager.Instance.GetAgeName().Replace("AGE ", "")
            };
        }
        return "I — DARK AGE";
    }

    public static string GetNextAgeShortName()
    {
        int age = AgeManager.Instance != null ? AgeManager.Instance.CurrentAge : 1;
        return age switch
        {
            1 => "Feudal Age (II)",
            2 => "Castle Age (III)",
            3 => "Imperial Age (IV)",
            _ => "Max Age"
        };
    }

    public static (int food, int gold) GetNextAgeCost()
    {
        int current = AgeManager.Instance != null ? AgeManager.Instance.CurrentAge : 1;
        int next = current + 1;
        if (next < AgeAdvancementCosts.Length)
            return AgeAdvancementCosts[next];
        return (0, 0);
    }

    public static bool CanAfford(int foodCost, int goldCost)
    {
        if (Mirror.NetworkClient.active && NetworkedPlayer.LocalInstance != null)
        {
            return NetworkedPlayer.LocalInstance.Food >= foodCost &&
                   NetworkedPlayer.LocalInstance.Gold >= goldCost;
        }

        if (ResourceManager.Instance != null)
        {
            return ResourceManager.Instance.Food >= foodCost &&
                   ResourceManager.Instance.Gold >= goldCost;
        }

        return false;
    }

    public static bool CanAdvanceAge()
    {
        int current = AgeManager.Instance != null ? AgeManager.Instance.CurrentAge : 1;
        if (current >= AgeManager.AGE_IMPERIAL) return false;
        var (f, g) = GetNextAgeCost();
        return CanAfford(f, g);
    }

    public static bool TryAdvanceAge()
    {
        int current = AgeManager.Instance != null ? AgeManager.Instance.CurrentAge : 1;
        if (current >= AgeManager.AGE_IMPERIAL)
        {
            ResourceBuildingUI.Instance?.ShowNotification("Kingdom has already achieved the Imperial Age!");
            return false;
        }

        var (foodCost, goldCost) = GetNextAgeCost();

        if (!CanAfford(foodCost, goldCost))
        {
            string warn = $"Need {foodCost} Food & {goldCost} Gold for {GetNextAgeShortName()}!";
            Debug.Log($"[AoE4HUDThemer] {warn}");
            AudioManager.Instance?.PlayUIError();
            ResourceBuildingUI.Instance?.ShowNotification(warn);
            return false;
        }

        // Spend resources
        bool spent = false;
        if (ResourceManager.Instance != null)
        {
            spent = ResourceManager.Instance.TrySpend(foodCost, 0, goldCost);
        }

        if (!spent)
        {
            AudioManager.Instance?.PlayUIError();
            return false;
        }

        // Advance age in AgeManager
        if (AgeManager.Instance != null)
        {
            AgeManager.Instance.AdvanceAge();
        }

        string victoryAnnouncement = $"ADVANCED TO {GetCurrentAgeDisplay()}!";
        ResourceBuildingUI.Instance?.ShowNotification($"<color=#E5C158><b>{victoryAnnouncement}</b></color>");

        return true;
    }

    public static string GetAdvanceAgeTooltip()
    {
        int current = AgeManager.Instance != null ? AgeManager.Instance.CurrentAge : 1;
        if (current >= AgeManager.AGE_IMPERIAL)
            return "<b>Imperial Age [Max]</b>\nThe pinnacle of medieval engineering and military supremacy.";

        var (f, g) = GetNextAgeCost();
        return $"<b>Advance to {GetNextAgeShortName()}</b>\nCost: <color=#E5C158>{f} Food, {g} Gold</color>\nUnlocks advanced combat units, fortified structures & economic expansion.";
    }

    // ── Top-Center Banner & Panel Styling ─────────────────────────────────

    public void ApplyTheme()
    {
        targetCanvas = FindTargetCanvas();
        if (targetCanvas == null) return;

        CacheSharedFont();
        ApplyPanelTheme();
        CreateOrUpdateTopBanner();
    }

    private Canvas FindTargetCanvas()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        foreach (Canvas c in canvases)
        {
            if (c.isRootCanvas && (c.name == "Canvas" || c.GetComponent<CanvasScaler>() != null))
                return c;
        }
        return canvases.Length > 0 ? canvases[0] : null;
    }

    private void CacheSharedFont()
    {
        if (sharedFontAsset != null) return;
        TMP_Text sample = FindObjectOfType<TMP_Text>(true);
        if (sample != null && sample.font != null)
            sharedFontAsset = sample.font;
    }

    /// <summary>
    /// Traverses the Canvas HUD panels and styles them into AoE4's Royal Slate-Navy & Burnished Gold theme.
    /// </summary>
    public void ApplyPanelTheme()
    {
        if (targetCanvas == null) targetCanvas = FindTargetCanvas();
        if (targetCanvas == null) return;

        string[] panelNames = new string[]
        {
            "BottomBar", "LeftZone", "CenterZone", "RightZone",
            "BuildingPanel", "BuildPanel", "UnitInfoPanel", "ResourceInfoPanel",
            "BuildingInfoUI", "BuildingInfoUI ", "ResourceBuildingUI", "ResourceBuildingUI ",
            "InfoPanel", "ButtonsPanel", "Resource", "NotificationPanel", "PausePanel",
            "BuildTooltip", "SpawnTooltip", "winlooseui"
        };

        var allTransforms = targetCanvas.GetComponentsInChildren<Transform>(true);
        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            string name = t.gameObject.name;

            bool isPanel = false;
            for (int i = 0; i < panelNames.Length; i++)
            {
                if (name.Equals(panelNames[i], System.StringComparison.OrdinalIgnoreCase))
                {
                    isPanel = true;
                    break;
                }
            }

            if (isPanel)
            {
                StylePanelObject(t.gameObject);
            }
        }
    }

    private void StylePanelObject(GameObject panelObj)
    {
        if (panelObj == null) return;

        // Skip the top banner itself as it's styled separately
        if (panelObj == topBannerObj || panelObj.name.Contains("AoE4_TopBanner")) return;

        // Apply Royal Slate-Navy background
        Image img = panelObj.GetComponent<Image>();
        if (img != null)
        {
            string lowerName = panelObj.name.ToLower();
            // Guard against fills, raw images, icons, and health bars
            if (!lowerName.Contains("fill") && !lowerName.Contains("icon") && !lowerName.Contains("portrait") && !lowerName.Contains("map"))
            {
                img.color = RoyalSlateNavy;
            }
        }

        // Apply Burnished Gold Outline
        Outline outline = panelObj.GetComponent<Outline>();
        if (outline == null && img != null)
        {
            outline = panelObj.AddComponent<Outline>();
        }

        if (outline != null)
        {
            outline.effectColor = BurnishedGold;
            outline.effectDistance = new Vector2(2f, -2f);
        }

        // Style child buttons
        Button[] buttons = panelObj.GetComponentsInChildren<Button>(true);
        foreach (Button btn in buttons)
        {
            if (btn == null) continue;
            Image btnImg = btn.GetComponent<Image>();
            if (btnImg != null && !btn.name.ToLower().Contains("close") && !btn.name.ToLower().Contains("exit"))
            {
                btnImg.color = DeepNavyButton;
                Outline btnOutline = btn.GetComponent<Outline>();
                if (btnOutline == null) btnOutline = btn.gameObject.AddComponent<Outline>();
                btnOutline.effectColor = new Color(BurnishedGold.r, BurnishedGold.g, BurnishedGold.b, 0.70f);
                btnOutline.effectDistance = new Vector2(1.2f, -1.2f);
            }

            TMP_Text btnTxt = btn.GetComponentInChildren<TMP_Text>(true);
            if (btnTxt != null)
            {
                btnTxt.color = LightParchment;
            }
        }
    }

    // ── Sleek Top-Center AoE4 Age & Kingdom Banner ────────────────────────

    private void CreateOrUpdateTopBanner()
    {
        if (targetCanvas == null) return;

        Transform existing = targetCanvas.transform.Find("AoE4_TopBanner");
        if (existing != null)
        {
            topBannerObj = existing.gameObject;
            return;
        }

        // 1. Root Banner Container
        topBannerObj = new GameObject("AoE4_TopBanner", typeof(RectTransform), typeof(Image), typeof(Outline));
        topBannerObj.transform.SetParent(targetCanvas.transform, false);

        RectTransform bannerRT = topBannerObj.GetComponent<RectTransform>();
        bannerRT.anchorMin = new Vector2(0.5f, 1f);
        bannerRT.anchorMax = new Vector2(0.5f, 1f);
        bannerRT.pivot     = new Vector2(0.5f, 1f);
        bannerRT.sizeDelta = new Vector2(650f, 46f);
        bannerRT.anchoredPosition = new Vector2(0f, -4f);

        Image bannerImg = topBannerObj.GetComponent<Image>();
        bannerImg.color = RoyalSlateNavyOpaque;

        Outline bannerOutline = topBannerObj.GetComponent<Outline>();
        bannerOutline.effectColor = BurnishedGoldFull;
        bannerOutline.effectDistance = new Vector2(2f, -2f);

        SelectionManager.RegisterBlockingPanel(bannerRT);

        // 2. Idle Villager Button (Left)
        GameObject idleBtnObj = new GameObject("Btn_IdleVillager", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
        idleBtnObj.transform.SetParent(topBannerObj.transform, false);

        RectTransform idleRT = idleBtnObj.GetComponent<RectTransform>();
        idleRT.anchorMin = new Vector2(0f, 0.5f);
        idleRT.anchorMax = new Vector2(0f, 0.5f);
        idleRT.pivot     = new Vector2(0f, 0.5f);
        idleRT.sizeDelta = new Vector2(195f, 36f);
        idleRT.anchoredPosition = new Vector2(8f, 0f);

        idleVillagerImage = idleBtnObj.GetComponent<Image>();
        idleVillagerImage.color = DeepNavyButton;

        idleVillagerOutline = idleBtnObj.GetComponent<Outline>();
        idleVillagerOutline.effectColor = BurnishedGold;
        idleVillagerOutline.effectDistance = new Vector2(1.5f, -1.5f);

        Button idleBtn = idleBtnObj.GetComponent<Button>();
        idleBtn.onClick.AddListener(SelectNextIdleVillager);

        GameObject idleTxtObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        idleTxtObj.transform.SetParent(idleBtnObj.transform, false);
        RectTransform idleTxtRT = idleTxtObj.GetComponent<RectTransform>();
        idleTxtRT.anchorMin = Vector2.zero;
        idleTxtRT.anchorMax = Vector2.one;
        idleTxtRT.sizeDelta = Vector2.zero;

        idleVillagerText = idleTxtObj.GetComponent<TextMeshProUGUI>();
        if (sharedFontAsset != null) idleVillagerText.font = sharedFontAsset;
        idleVillagerText.fontSize = 13.5f;
        idleVillagerText.alignment = TextAlignmentOptions.Center;
        idleVillagerText.fontStyle = FontStyles.Bold;
        idleVillagerText.text = "IDLE VILLAGER [.] (0)";

        // 3. Current Age Display (Center)
        GameObject ageDisplayObj = new GameObject("AgeDisplay", typeof(RectTransform), typeof(TextMeshProUGUI));
        ageDisplayObj.transform.SetParent(topBannerObj.transform, false);

        RectTransform ageRT = ageDisplayObj.GetComponent<RectTransform>();
        ageRT.anchorMin = new Vector2(0.5f, 0.5f);
        ageRT.anchorMax = new Vector2(0.5f, 0.5f);
        ageRT.pivot     = new Vector2(0.5f, 0.5f);
        ageRT.sizeDelta = new Vector2(230f, 38f);
        ageRT.anchoredPosition = Vector2.zero;

        ageDisplayText = ageDisplayObj.GetComponent<TextMeshProUGUI>();
        if (sharedFontAsset != null) ageDisplayText.font = sharedFontAsset;
        ageDisplayText.fontSize = 15.5f;
        ageDisplayText.alignment = TextAlignmentOptions.Center;
        ageDisplayText.fontStyle = FontStyles.Bold;
        ageDisplayText.text = "<color=#E5C158><b>I — DARK AGE</b></color>";

        // 4. Advance Age Button (Right)
        GameObject advBtnObj = new GameObject("Btn_AdvanceAge", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
        advBtnObj.transform.SetParent(topBannerObj.transform, false);

        RectTransform advRT = advBtnObj.GetComponent<RectTransform>();
        advRT.anchorMin = new Vector2(1f, 0.5f);
        advRT.anchorMax = new Vector2(1f, 0.5f);
        advRT.pivot     = new Vector2(1f, 0.5f);
        advRT.sizeDelta = new Vector2(195f, 36f);
        advRT.anchoredPosition = new Vector2(-8f, 0f);

        advanceAgeImage = advBtnObj.GetComponent<Image>();
        advanceAgeImage.color = DeepNavyButton;

        advanceAgeOutline = advBtnObj.GetComponent<Outline>();
        advanceAgeOutline.effectColor = BurnishedGold;
        advanceAgeOutline.effectDistance = new Vector2(1.5f, -1.5f);

        advanceAgeBtn = advBtnObj.GetComponent<Button>();
        advanceAgeBtn.onClick.AddListener(() =>
        {
            TryAdvanceAge();
        });

        // Hover tooltip on Advance Age button
        var trigger = advBtnObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();
        var enter = new UnityEngine.EventSystems.EventTrigger.Entry
            { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
        enter.callback.AddListener((_) =>
        {
            string tip = GetAdvanceAgeTooltip();
            ResourceBuildingUI.Instance?.ShowNotification(tip);
        });
        trigger.triggers.Add(enter);

        GameObject advTxtObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        advTxtObj.transform.SetParent(advBtnObj.transform, false);
        RectTransform advTxtRT = advTxtObj.GetComponent<RectTransform>();
        advTxtRT.anchorMin = Vector2.zero;
        advTxtRT.anchorMax = Vector2.one;
        advTxtRT.sizeDelta = Vector2.zero;

        advanceAgeText = advTxtObj.GetComponent<TextMeshProUGUI>();
        if (sharedFontAsset != null) advanceAgeText.font = sharedFontAsset;
        advanceAgeText.fontSize = 13.5f;
        advanceAgeText.alignment = TextAlignmentOptions.Center;
        advanceAgeText.fontStyle = FontStyles.Bold;
        advanceAgeText.text = "<color=#E5C158><b>ADVANCE AGE</b></color>";
    }

    private void UpdateTopBanner()
    {
        if (topBannerObj == null) return;

        // 1. Update Current Age Text
        if (ageDisplayText != null)
        {
            ageDisplayText.text = $"<color=#E5C158><b>{GetCurrentAgeDisplay()}</b></color>";
        }

        // 2. Count live idle villagers
        int idleCount = CountLocalIdleVillagers();
        if (idleVillagerText != null)
        {
            if (idleCount > 0)
            {
                idleVillagerText.text = $"<color=#FFE080><b>IDLE VILLAGER [.] ({idleCount})</b></color>";
                if (idleVillagerOutline != null)
                    idleVillagerOutline.effectColor = BurnishedGoldFull;
            }
            else
            {
                idleVillagerText.text = "<color=#889AA8>IDLE VILLAGER [.] (0)</color>";
                if (idleVillagerOutline != null)
                    idleVillagerOutline.effectColor = new Color(0.40f, 0.45f, 0.55f, 0.50f);
            }
        }

        // 3. Update Advance Age Button State
        if (advanceAgeText != null)
        {
            int currentAge = AgeManager.Instance != null ? AgeManager.Instance.CurrentAge : 1;
            if (currentAge >= AgeManager.AGE_IMPERIAL)
            {
                advanceAgeText.text = "<color=#889AA8>IMPERIAL AGE [MAX]</color>";
                if (advanceAgeBtn != null) advanceAgeBtn.interactable = false;
                if (advanceAgeOutline != null)
                    advanceAgeOutline.effectColor = new Color(0.40f, 0.45f, 0.55f, 0.50f);
            }
            else
            {
                if (advanceAgeBtn != null) advanceAgeBtn.interactable = true;
                bool affordable = CanAdvanceAge();
                string nextShort = GetNextAgeShortName();

                if (affordable)
                {
                    advanceAgeText.text = $"<color=#FFE080><b>ADVANCE TO {nextShort.ToUpper()}</b></color>";
                    if (advanceAgeOutline != null)
                        advanceAgeOutline.effectColor = BurnishedGoldFull;
                }
                else
                {
                    advanceAgeText.text = $"<color=#D0C090>ADVANCE: {nextShort}</color>";
                    if (advanceAgeOutline != null)
                        advanceAgeOutline.effectColor = BurnishedGold;
                }
            }
        }
    }

    // ── Idle Villager Selection & Camera Centering ─────────────────────────

    private void HandleIdleVillagerHotkey()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
            UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Period) || Input.GetKeyDown(KeyCode.KeypadPeriod))
        {
            SelectNextIdleVillager();
        }
    }

    public void SelectNextIdleVillager()
    {
        var list = UnitSelectionManager.Instance?.allUnitsList;
        if (list == null || list.Count == 0) return;

        var idleVillagers = new List<Villager>();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is Villager v && !v.IsDead && v.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex && v.IsIdle)
            {
                idleVillagers.Add(v);
            }
        }

        if (idleVillagers.Count == 0)
        {
            Debug.Log("[AoE4HUDThemer] No idle villagers found.");
            ResourceBuildingUI.Instance?.ShowNotification("No idle villagers available.");
            return;
        }

        lastIdleVillagerIndex = (lastIdleVillagerIndex + 1) % idleVillagers.Count;
        Villager target = idleVillagers[lastIdleVillagerIndex];

        SelectionManager.Instance?.SelectSingleUnit(target);
        SelectionManager.Instance?.CenterCameraOnPosition(target.transform.position);

        AudioManager.Instance?.PlayUnitSelect();
    }

    private int CountLocalIdleVillagers()
    {
        var list = UnitSelectionManager.Instance?.allUnitsList;
        if (list == null || list.Count == 0) return 0;

        int count = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is Villager v && !v.IsDead && v.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex && v.IsIdle)
                count++;
        }
        return count;
    }
}
