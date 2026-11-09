using UnityEngine;
using TMPro;
using System.Collections;

public class FloatingDamageText : MonoBehaviour
{
    [SerializeField] private float fadeDuration = 0.95f;
    [SerializeField] private float floatDistance = 1.6f;
    [SerializeField] private AnimationCurve floatCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private TextMeshPro tmp;
    private Color startColor = Color.red;
    private Vector3 startPosition;
    private Vector3 baseScale = Vector3.one * 0.32f;

    private void Awake()
    {
        EnsureTextMeshPro();
    }

    private void EnsureTextMeshPro()
    {
        if (tmp == null)
        {
            tmp = GetComponent<TextMeshPro>();
            if (tmp == null) tmp = GetComponentInChildren<TextMeshPro>();
            if (tmp == null)
            {
                tmp = gameObject.AddComponent<TextMeshPro>();
            }
        }
        if (tmp != null)
        {
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 4.4f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.sortingOrder = 200;
            tmp.extraPadding = true;

            // Crisp black outline for high-contrast readability against any terrain
            tmp.outlineWidth = 0.26f;
            tmp.outlineColor = new Color32(0, 0, 0, 245);

            var rt = tmp.rectTransform;
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(8f, 2.5f);
            }

            // Drop-shadow underlay configuration
            if (tmp.fontMaterial != null)
            {
                tmp.fontMaterial.EnableKeyword("OUTLINE_ON");
                tmp.fontMaterial.EnableKeyword("UNDERLAY_ON");
                tmp.fontMaterial.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.85f));
                tmp.fontMaterial.SetFloat("_UnderlayOffsetX", 0.25f);
                tmp.fontMaterial.SetFloat("_UnderlayOffsetY", -0.25f);
                tmp.fontMaterial.SetFloat("_UnderlayDilate", 0.1f);
                tmp.fontMaterial.SetFloat("_UnderlaySoftness", 0.12f);
            }

            startColor = tmp.color;
        }
    }

    private void OnEnable()
    {
        EnsureTextMeshPro();
        startPosition = transform.position;
        if (transform.localScale.x > 0.45f || transform.localScale.x < 0.15f)
            baseScale = Vector3.one * 0.32f;
        else
            baseScale = transform.localScale;

        StartCoroutine(Animate());
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            transform.rotation = cam.transform.rotation;
        }
    }

    private IEnumerator Animate()
    {
        float elapsed = 0f;
        Vector3 targetPos = startPosition + Vector3.up * floatDistance;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float curveT = floatCurve != null ? floatCurve.Evaluate(t) : t;

            transform.position = Vector3.Lerp(startPosition, targetPos, curveT);

            // Punchy scale effect: quick pop on impact, then smooth settle
            float scalePop = t < 0.15f
                ? Mathf.Lerp(0.7f, 1.15f, t / 0.15f)
                : Mathf.Lerp(1.15f, 0.95f, (t - 0.15f) / 0.85f);
            transform.localScale = baseScale * scalePop;

            if (tmp != null)
            {
                float alpha = Mathf.Lerp(1f, 0f, t * t);
                Color c = startColor;
                c.a = alpha;
                tmp.color = c;

                Color oc = Color.black;
                oc.a = alpha * 0.95f;
                tmp.outlineColor = oc;
            }

            yield return null;
        }

        Destroy(gameObject);
    }

    public void SetDamage(int amount, Color? color = null)
    {
        EnsureTextMeshPro();
        if (tmp != null)
        {
            tmp.text = amount > 0 ? $"-{amount}" : amount.ToString();
            startColor = color ?? new Color(1f, 0.22f, 0.22f, 1f); // Vibrant AoE combat crimson
            tmp.color = startColor;
        }
    }

    public void SetText(string text, Color? color = null)
    {
        EnsureTextMeshPro();
        if (tmp != null)
        {
            tmp.text = text;
            if (color.HasValue)
            {
                startColor = color.Value;
            }
            else
            {
                // Auto-detect RTS resource color for crisp theme matching
                string lower = text.ToLowerInvariant();
                if (lower.Contains("food") || lower.Contains("meat") || lower.Contains("wheat"))
                    startColor = new Color(0.95f, 0.55f, 0.15f); // Warm amber
                else if (lower.Contains("wood") || lower.Contains("tree") || lower.Contains("timber"))
                    startColor = new Color(0.35f, 0.95f, 0.4f);  // Forestry emerald
                else if (lower.Contains("gold") || lower.Contains("coin") || lower.Contains("treasure"))
                    startColor = new Color(1f, 0.88f, 0.18f);    // Radiant gold
                else if (lower.Contains("stone") || lower.Contains("rock"))
                    startColor = new Color(0.75f, 0.82f, 0.9f);  // Slate stone
                else
                    startColor = Color.white;
            }
            tmp.color = startColor;
        }
    }

    public static FloatingDamageText Spawn(Vector3 position, int amount, Color? color = null)
    {
        GameObject go = new GameObject("FloatingDamageText");
        go.transform.position = position;
        FloatingDamageText fdt = go.AddComponent<FloatingDamageText>();
        fdt.EnsureTextMeshPro();
        fdt.SetDamage(amount, color);
        return fdt;
    }

    public static FloatingDamageText Spawn(Vector3 position, string text, Color? color = null)
    {
        GameObject go = new GameObject("FloatingDamageText");
        go.transform.position = position;
        FloatingDamageText fdt = go.AddComponent<FloatingDamageText>();
        fdt.EnsureTextMeshPro();
        fdt.SetText(text, color);
        return fdt;
    }

    public static FloatingDamageText SpawnResource(Vector3 position, string resourceType, int amount)
    {
        string sign = amount >= 0 ? "+" : "";
        string text = $"{sign}{amount} {resourceType}";
        return Spawn(position, text, null);
    }
}
