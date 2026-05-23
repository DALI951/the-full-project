using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UIClickDebugger — attach to GameManager.
///
/// Two jobs:
///   1. AUTO-PATCH: every <patchInterval> seconds, finds every Image / CanvasGroup
///      that is visually invisible (alpha ≈ 0) and disables its raycastTarget so it
///      can never silently eat clicks.
///
///   2. OVERLAY: press F9 (configurable) to see every UI element currently under
///      the cursor, its alpha, and whether it is a registered blocking panel.
///      Use this whenever a click "disappears" to find the culprit instantly.
/// </summary>
public class UIClickDebugger : MonoBehaviour
{
    [Header("Debug overlay key (F9 by default)")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F9;

    [Header("Auto-patch invisible raycasters")]
    [Tooltip("Automatically disables raycastTarget on zero-alpha Images every N seconds.")]
    [SerializeField] private bool  autoPatch      = true;
    [SerializeField] private float patchInterval  = 3f;

    // ── runtime ───────────────────────────────────────────────────────
    private bool    overlayOn  = false;
    private string  hitInfo    = "";
    private float   patchTimer = 0f;
    private GUIStyle box;

    // ── lifecycle ─────────────────────────────────────────────────────
    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            overlayOn = !overlayOn;
            Debug.Log($"[UIClickDebugger] overlay {(overlayOn ? "ON  (press {toggleKey} to hide)" : "OFF")}");
        }

        if (autoPatch)
        {
            patchTimer += Time.unscaledDeltaTime;
            if (patchTimer >= patchInterval) { patchTimer = 0f; RunAutoPatch(); }
        }

        if (overlayOn) hitInfo = BuildHitInfo();
    }

    // ── auto-patch ────────────────────────────────────────────────────

    private void RunAutoPatch()
    {
        // 1. Images with alpha == 0 and raycastTarget == true
        foreach (Image img in FindObjectsOfType<Image>(true))
        {
            if (img.raycastTarget && img.color.a < 0.01f)
            {
                img.raycastTarget = false;
                Debug.Log($"[UIClickDebugger] patched zero-alpha Image → {Path(img.transform)}");
            }
        }

        // 2. CanvasGroups with alpha == 0 and blocksRaycasts == true
        foreach (CanvasGroup cg in FindObjectsOfType<CanvasGroup>(true))
        {
            if (cg.blocksRaycasts && cg.alpha < 0.01f)
            {
                cg.blocksRaycasts = false;
                Debug.Log($"[UIClickDebugger] patched zero-alpha CanvasGroup → {Path(cg.transform)}");
            }
        }
    }

    // ── overlay ───────────────────────────────────────────────────────

    private string BuildHitInfo()
    {
        if (EventSystem.current == null) return "No EventSystem";

        var pd = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pd, results);

        if (results.Count == 0) return "— nothing —";

        var sb = new System.Text.StringBuilder();
        foreach (var r in results)
        {
            if (r.gameObject == null) continue;
            float a   = GetAlpha(r.gameObject);
            bool  reg = IsRegisteredBlocker(r.gameObject.transform);
            sb.AppendLine($"  [{r.depth:D2}]  {r.gameObject.name}   alpha={a:F2}  registered={reg}");
        }
        return sb.ToString();
    }

    private void OnGUI()
    {
        if (!overlayOn) return;
        if (box == null)
        {
            box = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 11,
                alignment = TextAnchor.UpperLeft,
                wordWrap  = false,
                richText  = false
            };
            box.normal.textColor = Color.yellow;
        }
        string text = $"[{toggleKey} = hide]  UI under cursor:\n{hitInfo}";
        Vector2 sz  = box.CalcSize(new GUIContent(text));
        GUI.Box(new Rect(8, 8, sz.x + 16, sz.y + 10), text, box);
    }

    // ── helpers ───────────────────────────────────────────────────────

    private static float GetAlpha(GameObject go)
    {
        var img = go.GetComponent<Image>();
        if (img) return img.color.a;
        var tmp = go.GetComponent<TMP_Text>();
        if (tmp) return tmp.color.a;
        var cg  = go.GetComponent<CanvasGroup>();
        if (cg)  return cg.alpha;
        return 1f;
    }

    private static bool IsRegisteredBlocker(Transform t)
    {
        while (t != null)
        {
            var rt = t.GetComponent<RectTransform>();
            if (rt != null && SelectionManager.IsRegisteredBlockingPanel(rt)) return true;
            t = t.parent;
        }
        return false;
    }

    private static string Path(Transform t)
    {
        var parts = new List<string>();
        while (t != null) { parts.Insert(0, t.name); t = t.parent; }
        return string.Join("/", parts);
    }
}