using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AgeManager — tracks AoE4-style Age I to Age IV progression,
/// civilization tech bonuses, military veterancy stats, and economic modifiers.
/// Auto-instantiates after scene load if not already in the scene.
/// </summary>
public class AgeManager : MonoBehaviour
{
    public static AgeManager Instance { get; private set; }

    public const int AGE_DARK     = 1;
    public const int AGE_FEUDAL   = 2;
    public const int AGE_CASTLE   = 3;
    public const int AGE_IMPERIAL = 4;

    private static readonly string[] AgeNames = new string[]
    {
        "AGE I - DARK AGE",
        "AGE II - FEUDAL AGE",
        "AGE III - CASTLE AGE",
        "AGE IV - IMPERIAL AGE"
    };

    [Header("Current Progression")]
    [Range(1, 4)]
    [SerializeField] private int currentAge = 1;

    /// <summary>
    /// Event fired whenever the age advances: (int ageNumber, string ageName).
    /// </summary>
    public static event System.Action<int, string> OnAgeAdvanced;

    // ── Active Bonuses (Calculated from currentAge) ───────────────────
    public int   CurrentAge              => currentAge;
    public static int CurrentAgeStatic   => Instance != null ? Instance.currentAge : 1;
    public int   MilitaryDamageBonus     => GetMilitaryDamageBonus(currentAge);
    public int   MilitaryHpBonus         => GetMilitaryHpBonus(currentAge);
    public float VillagerSpeedMultiplier => GetVillagerSpeedMultiplier(currentAge);
    public int   GatherBonus             => GetGatherBonus(currentAge);

    // ── HUD Banner Animation ──────────────────────────────────────────
    private string bannerText = "";
    private float  bannerTimer = 0f;
    private const float BANNER_DURATION = 4.5f;
    private GUIStyle bannerStyle;
    private GUIStyle subBannerStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        if (Instance == null)
        {
            var existing = Object.FindObjectOfType<AgeManager>();
            if (existing != null)
            {
                Instance = existing;
            }
            else
            {
                GameObject go = new GameObject("[AgeManager]");
                Instance = go.AddComponent<AgeManager>();
                DontDestroyOnLoad(go);
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (bannerTimer > 0f)
        {
            bannerTimer -= Time.deltaTime;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Developer shortcut: F8 to advance Age
        if (Input.GetKeyDown(KeyCode.F8))
        {
            AdvanceAge();
        }
#endif
    }

    // ── Age Progression API ───────────────────────────────────────────

    /// <summary>Advance by 1 age up to Age IV (Imperial Age).</summary>
    [ContextMenu("Advance Age")]
    public bool AdvanceAge()
    {
        if (currentAge >= AGE_IMPERIAL)
        {
            Debug.Log("[AgeManager] Already at Maximum Age (Age IV - Imperial Age).");
            return false;
        }
        return SetAge(currentAge + 1);
    }

    /// <summary>Advance directly to a specific target age (1..4).</summary>
    public bool AdvanceAge(int targetAge)
    {
        return SetAge(targetAge);
    }

    /// <summary>Sets the current age, triggers HUD banner, audio, and notifies listeners.</summary>
    public bool SetAge(int newAge)
    {
        newAge = Mathf.Clamp(newAge, AGE_DARK, AGE_IMPERIAL);
        if (newAge == currentAge && bannerTimer > 0f) return false;

        currentAge = newAge;
        string name = GetAgeName(currentAge);
        Debug.Log($"[AgeManager] <<< CIVILIZATION ADVANCED TO {name} (Age {currentAge}) >>>");

        // Trigger banner & fanfare
        bannerText = name;
        bannerTimer = BANNER_DURATION;
        PlayAgeUpAudio();

        // Fire public event for HUD and units
        OnAgeAdvanced?.Invoke(currentAge, name);
        return true;
    }

    public static string GetAgeName(int age)
    {
        int idx = Mathf.Clamp(age - 1, 0, AgeNames.Length - 1);
        return AgeNames[idx];
    }

    public string GetAgeName() => GetAgeName(currentAge);

    // ── Stat Multipliers & Bonuses ────────────────────────────────────

    /// <summary>Bonus military attack damage awarded at the given age.</summary>
    public static int GetMilitaryDamageBonus(int age)
    {
        return age switch
        {
            AGE_FEUDAL   => 4,   // +4 Damage (~20% buff)
            AGE_CASTLE   => 10,  // +10 Damage (~50% buff)
            AGE_IMPERIAL => 18,  // +18 Damage (~90% buff)
            _            => 0
        };
    }

    /// <summary>Bonus military maximum health awarded at the given age.</summary>
    public static int GetMilitaryHpBonus(int age)
    {
        return age switch
        {
            AGE_FEUDAL   => 25,  // +25 HP (~20% buff)
            AGE_CASTLE   => 60,  // +60 HP (~50% buff)
            AGE_IMPERIAL => 110, // +110 HP (~90% buff)
            _            => 0
        };
    }

    /// <summary>Villager movement speed multiplier at the given age.</summary>
    public static float GetVillagerSpeedMultiplier(int age)
    {
        return age switch
        {
            AGE_FEUDAL   => 1.10f, // +10% Speed
            AGE_CASTLE   => 1.20f, // +20% Speed
            AGE_IMPERIAL => 1.35f, // +35% Speed
            _            => 1.0f
        };
    }

    /// <summary>Bonus resources collected per gathering cycle at the given age.</summary>
    public static int GetGatherBonus(int age)
    {
        return age switch
        {
            AGE_FEUDAL   => 2,  // +2 resources per tick
            AGE_CASTLE   => 5,  // +5 resources per tick
            AGE_IMPERIAL => 8,  // +8 resources per tick
            _            => 0
        };
    }

    private void PlayAgeUpAudio()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayWaveHorn();
            AudioManager.Instance.PlayNotification();
        }
    }

    // ── On-Screen Medieval Age Banner ─────────────────────────────────
    private void OnGUI()
    {
        if (bannerTimer <= 0f || string.IsNullOrEmpty(bannerText)) return;

        float alpha = Mathf.Clamp01(bannerTimer / 0.8f);
        if (bannerTimer > BANNER_DURATION - 0.5f)
        {
            alpha = Mathf.Clamp01((BANNER_DURATION - bannerTimer) / 0.5f);
        }

        if (bannerStyle == null)
        {
            bannerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 26,
                fontStyle = FontStyle.Bold
            };
        }

        if (subBannerStyle == null)
        {
            subBannerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Normal
            };
        }

        float bannerWidth = 480f;
        float bannerHeight = 64f;
        float x = (Screen.width - bannerWidth) * 0.5f;
        float y = 58f;

        // Dark medieval parchment backdrop
        Color prevColor = GUI.color;
        GUI.color = new Color(0.04f, 0.05f, 0.07f, 0.90f * alpha);
        GUI.Box(new Rect(x, y, bannerWidth, bannerHeight), GUIContent.none);

        // Gold trim borders
        GUI.color = new Color(0.85f, 0.72f, 0.28f, 0.85f * alpha);
        GUI.Box(new Rect(x + 2, y + 2, bannerWidth - 4, 2), GUIContent.none);
        GUI.Box(new Rect(x + 2, y + bannerHeight - 4, bannerWidth - 4, 2), GUIContent.none);

        // Main Title (Golden AoE Banner text)
        bannerStyle.normal.textColor = new Color(1f, 0.88f, 0.40f, alpha);
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y + 6, bannerWidth, 32), bannerText, bannerStyle);

        // Subtitle (Civilization Tech Upgrades Applied)
        subBannerStyle.normal.textColor = new Color(0.85f, 0.88f, 0.92f, alpha * 0.9f);
        string subText = $"+{MilitaryDamageBonus} Military DMG  *  +{MilitaryHpBonus} HP  *  +{GatherBonus} Harvest Bonus";
        GUI.Label(new Rect(x, y + 36, bannerWidth, 22), subText, subBannerStyle);

        GUI.color = prevColor;
    }
}

/// <summary>
/// Lightweight procedural generator and cached material factory for medieval unit gear.
/// Ensures all weapon and shield meshes have colliders removed and use opaque metallic / team materials.
/// </summary>
public static class MedievalGearHelper
{
    private static Material _steelMat;
    private static Material _darkIronMat;
    private static Material _brassMat;
    private static Material _woodMat;
    private static readonly Dictionary<int, Material> _teamMats = new Dictionary<int, Material>();

    public static Shader GetStandardShader()
    {
        Shader s = Shader.Find("Standard");
        if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("Mobile/Diffuse");
        if (s == null) s = Shader.Find("Diffuse");
        return s;
    }

    public static Material GetSteelMaterial()
    {
        if (_steelMat == null)
        {
            _steelMat = new Material(GetStandardShader());
            _steelMat.color = new Color(0.85f, 0.88f, 0.92f, 1f);
            if (_steelMat.HasProperty("_Metallic")) _steelMat.SetFloat("_Metallic", 0.85f);
            if (_steelMat.HasProperty("_Glossiness")) _steelMat.SetFloat("_Glossiness", 0.75f);
        }
        return _steelMat;
    }

    public static Material GetDarkIronMaterial()
    {
        if (_darkIronMat == null)
        {
            _darkIronMat = new Material(GetStandardShader());
            _darkIronMat.color = new Color(0.22f, 0.22f, 0.25f, 1f);
            if (_darkIronMat.HasProperty("_Metallic")) _darkIronMat.SetFloat("_Metallic", 0.65f);
            if (_darkIronMat.HasProperty("_Glossiness")) _darkIronMat.SetFloat("_Glossiness", 0.40f);
        }
        return _darkIronMat;
    }

    public static Material GetBrassMaterial()
    {
        if (_brassMat == null)
        {
            _brassMat = new Material(GetStandardShader());
            _brassMat.color = new Color(0.86f, 0.72f, 0.26f, 1f);
            if (_brassMat.HasProperty("_Metallic")) _brassMat.SetFloat("_Metallic", 0.88f);
            if (_brassMat.HasProperty("_Glossiness")) _brassMat.SetFloat("_Glossiness", 0.78f);
        }
        return _brassMat;
    }

    public static Material GetWoodMaterial()
    {
        if (_woodMat == null)
        {
            _woodMat = new Material(GetStandardShader());
            _woodMat.color = new Color(0.36f, 0.24f, 0.15f, 1f);
            if (_woodMat.HasProperty("_Metallic")) _woodMat.SetFloat("_Metallic", 0.05f);
            if (_woodMat.HasProperty("_Glossiness")) _woodMat.SetFloat("_Glossiness", 0.25f);
        }
        return _woodMat;
    }

    public static Material GetTeamMaterial(int ownerId)
    {
        if (!_teamMats.TryGetValue(ownerId, out Material mat) || mat == null)
        {
            mat = new Material(GetStandardShader());
            Color c = PlayerColorManager.Instance != null
                ? PlayerColorManager.Instance.GetColor(ownerId)
                : (ownerId == 0 ? Color.cyan : (ownerId == 1 ? Color.red : Color.blue));
            mat.color = c;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.30f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.55f);
            _teamMats[ownerId] = mat;
        }
        return mat;
    }

    public static Transform FindBone(Transform root, params string[] candidateNames)
    {
        if (root == null) return null;
        foreach (string name in candidateNames)
        {
            Transform found = FindBoneRecursive(root, name);
            if (found != null) return found;
        }
        return null;
    }

    private static Transform FindBoneRecursive(Transform current, string targetName)
    {
        if (current.name.IndexOf(targetName, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return current;
        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindBoneRecursive(current.GetChild(i), targetName);
            if (found != null) return found;
        }
        return null;
    }

    public static GameObject CreatePrimitiveWithoutCollider(PrimitiveType type, string name, Transform parent, Material mat, Vector3 localPos, Quaternion localRot, Vector3 localScale)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Collider col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;
        if (mat != null)
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }
        return go;
    }
}
