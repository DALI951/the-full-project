using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// UnitSelectionBox — drag-to-select rectangle.
///
/// Features:
/// - Fast/split-second drag-box selection with live swept unit caching.
/// - 3-point screen-space bounding box check (feet, center, head) + 16px padding so touching any part selects units.
/// - Blocks dragging over UI, Minimap, and bottom panel.
/// </summary>
public class UnitSelectionBox : MonoBehaviour
{
    Camera myCam;

    [SerializeField] RectTransform boxVisual;
    [SerializeField] Canvas        uiCanvas;
    [SerializeField] float         dragThreshold = 4f;
    [SerializeField] private RectTransform bottomBar;

    Vector2 startPosition;      // screen-space start of drag
    Vector2 endPosition;
    bool    isDragging       = false;
    bool    hasDragOrigin    = false;
    bool    startOnUI        = false;
    float   dragStartTime    = 0f;

    public static UnitSelectionBox Instance { get; private set; }
    public bool JustFinishedDrag { get; private set; }
    public bool IsDragging => isDragging;

    private HashSet<Unit> sweptUnits         = new HashSet<Unit>();
    private HashSet<Unit> _realtimeDragUnits = new HashSet<Unit>();
    private HashSet<Unit> _nextDragUnits     = new HashSet<Unit>();
    private readonly List<Unit> s_removedBuffer = new List<Unit>(64);

    private static readonly Vector3[] s_cornerBuffer = new Vector3[4];

    private void Awake()
    {
        dragThreshold = Mathf.Min(dragThreshold, 4f);
    }

    private void Start()
    {
        Instance = this;
        myCam = Camera.main;
        dragThreshold = Mathf.Min(dragThreshold, 4f);
        if (boxVisual != null) boxVisual.gameObject.SetActive(false);
        SelectionManager.RegisterBlockingPanel(boxVisual);
    }

    private System.Collections.IEnumerator ResetDragFlag()
    {
        yield return null;
        JustFinishedDrag = false;
    }

    private bool wasMouseDown = false;

    private void Update()
    {
        bool isDown = Input.GetMouseButton(0);
        bool justReleased = wasMouseDown && !isDown;
        wasMouseDown = isDown;

        bool pointerOverUI = SelectionManager.IsPointerOverBlockingUI() ||
                             (MinimapSystem.Instance != null && MinimapSystem.Instance.IsPointerOverMinimap()) ||
                             IsMouseOverBottomPanel();

        if (pointerOverUI && !isDragging)
        {
            if (justReleased && isDragging)
            {
                endPosition = Input.mousePosition;
                JustFinishedDrag = true;
                SelectUnits();
                CancelDrag();
            }
            return;
        }

        if (isDown && !isDragging && !hasDragOrigin && !pointerOverUI)
        {
            hasDragOrigin = true;
            startPosition = Input.mousePosition;
            endPosition   = startPosition;
            startOnUI     = false;
        }

        if (isDown && !startOnUI && hasDragOrigin)
        {
            endPosition = Input.mousePosition;

            if (!isDragging &&
                Vector2.Distance(startPosition, endPosition) > dragThreshold)
            {
                isDragging = true;
                dragStartTime = Time.unscaledTime;
                sweptUnits.Clear();
                _realtimeDragUnits.Clear();
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!shiftHeld) SelectionManager.Instance?.DeselectAll();
                if (boxVisual != null) boxVisual.gameObject.SetActive(true);
            }

            if (isDragging)
            {
                DrawVisual();
                UpdateRealtimeSelection();
            }
        }

        if (justReleased)
        {
            endPosition = Input.mousePosition;
            if (isDragging && !startOnUI)
            {
                JustFinishedDrag = true;
                SelectUnits();
            }
            CancelDrag();
        }
    }

    // ── Draw the UI rectangle ─────────────────────────────────────────────
    void DrawVisual()
    {
        if (boxVisual == null) return;

        Vector2 s = startPosition;
        Vector2 e = endPosition;

        // Always convert screen positions to canvas local space
        RectTransform canvasRect = uiCanvas != null
            ? uiCanvas.GetComponent<RectTransform>()
            : null;

        if (canvasRect != null)
        {
            Camera uiCam = uiCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : uiCanvas.worldCamera;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, startPosition, uiCam, out s);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, endPosition, uiCam, out e);
        }

        boxVisual.anchoredPosition = (s + e) / 2f;
        boxVisual.sizeDelta = new Vector2(
            Mathf.Abs(s.x - e.x),
            Mathf.Abs(s.y - e.y));
    }

    public bool DoesUnitOverlapBox(Unit unit, Rect screenRect)
    {
        if (unit == null) return false;
        if (myCam == null) myCam = Camera.main;
        if (myCam == null) return false;

        Vector3 posFeet   = unit.transform.position;
        Vector3 posCenter = posFeet + Vector3.up * 0.6f;
        Vector3 posHead   = posFeet + Vector3.up * 1.2f;

        Vector3 sFeet   = myCam.WorldToScreenPoint(posFeet);
        Vector3 sCenter = myCam.WorldToScreenPoint(posCenter);
        Vector3 sHead   = myCam.WorldToScreenPoint(posHead);

        // If all 3 points are behind camera, skip
        if (sFeet.z < 0f && sCenter.z < 0f && sHead.z < 0f) return false;

        float minX = Mathf.Min(sFeet.x, Mathf.Min(sCenter.x, sHead.x)) - 16f;
        float maxX = Mathf.Max(sFeet.x, Mathf.Max(sCenter.x, sHead.x)) + 16f;
        float minY = Mathf.Min(sFeet.y, Mathf.Min(sCenter.y, sHead.y)) - 16f;
        float maxY = Mathf.Max(sFeet.y, Mathf.Max(sCenter.y, sHead.y)) + 16f;

        Rect unitRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return screenRect.Overlaps(unitRect, true);
    }

    private void UpdateRealtimeSelection()
    {
        if (myCam == null) myCam = Camera.main;
        if (myCam == null) return;

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        Rect currentRect = new Rect(
            Mathf.Min(startPosition.x, endPosition.x),
            Mathf.Min(startPosition.y, endPosition.y),
            Mathf.Abs(endPosition.x - startPosition.x),
            Mathf.Abs(endPosition.y - startPosition.y));

        var list = UnitSelectionManager.Instance?.allUnitsList;
        if (list == null) return;

        _nextDragUnits.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            Unit unit = list[i];
            if (unit == null || unit.IsDead || unit.IsInsideBuilding) continue;
            if (!unit.IsSelectable) continue;
            if (unit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex) continue;

            if (DoesUnitOverlapBox(unit, currentRect))
            {
                _nextDragUnits.Add(unit);
                sweptUnits.Add(unit);
            }
        }

        // Live-select newly covered units immediately so green circles appear
        foreach (Unit u in _nextDragUnits)
        {
            if (!_realtimeDragUnits.Contains(u))
                SelectionManager.Instance?.AddUnitToSelection(u);
        }

        bool quickDrag = (Time.unscaledTime - dragStartTime) < 0.35f;

        if (!shift && !quickDrag)
        {
            // For longer deliberate drags, unselect units that moved outside the box
            s_removedBuffer.Clear();
            foreach (Unit u in _realtimeDragUnits)
                if (!_nextDragUnits.Contains(u))
                    s_removedBuffer.Add(u);

            for (int i = 0; i < s_removedBuffer.Count; i++)
            {
                SelectionManager.Instance?.RemoveUnitFromDragSelection(s_removedBuffer[i]);
                sweptUnits.Remove(s_removedBuffer[i]);
            }
        }

        // Swap sets
        HashSet<Unit> previous = _realtimeDragUnits;
        _realtimeDragUnits = _nextDragUnits;
        _nextDragUnits = previous;

        SelectionManager.Instance?.RefreshInfoPanelPublic();
    }

    // ── Select units inside the rect ──────────────────────────────────────
    void SelectUnits()
    {
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (!shift && sweptUnits.Count == 0 && _realtimeDragUnits.Count == 0)
        {
            SelectionManager.Instance?.DeselectAll();
        }

        // Add all units accumulated in sweptUnits
        foreach (Unit u in sweptUnits)
        {
            if (u != null && !u.IsDead && !u.IsInsideBuilding && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
            {
                SelectionManager.Instance?.AddUnitToSelection(u);
            }
        }

        // Add all units currently in realtime drag units
        foreach (Unit u in _realtimeDragUnits)
        {
            if (u != null && !u.IsDead && !u.IsInsideBuilding && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
            {
                SelectionManager.Instance?.AddUnitToSelection(u);
            }
        }

        // Fallback bounding check if both sets were somehow empty
        if (sweptUnits.Count == 0 && _realtimeDragUnits.Count == 0)
        {
            Rect currentRect = new Rect(
                Mathf.Min(startPosition.x, endPosition.x),
                Mathf.Min(startPosition.y, endPosition.y),
                Mathf.Abs(endPosition.x - startPosition.x),
                Mathf.Abs(endPosition.y - startPosition.y));

            var list = UnitSelectionManager.Instance?.allUnitsList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    Unit unit = list[i];
                    if (unit == null || unit.IsDead || unit.IsInsideBuilding) continue;
                    if (!unit.IsSelectable || unit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex) continue;
                    if (DoesUnitOverlapBox(unit, currentRect))
                    {
                        SelectionManager.Instance?.AddUnitToSelection(unit);
                    }
                }
            }
        }

        sweptUnits.Clear();
        _realtimeDragUnits.Clear();
        SelectionManager.Instance?.RefreshInfoPanelPublic();
    }

    // ── Reset ─────────────────────────────────────────────────────────────
    void CancelDrag()
    {
        sweptUnits.Clear();
        _realtimeDragUnits.Clear();
        isDragging    = false;
        hasDragOrigin = false;
        startOnUI     = false;
        startPosition = Vector2.zero;
        endPosition   = Vector2.zero;
        if (boxVisual != null) boxVisual.gameObject.SetActive(false);
        StartCoroutine(ResetDragFlag());
    }

    private bool IsMouseOverBottomPanel()
    {
        if (bottomBar == null) return false;
        bottomBar.GetWorldCorners(s_cornerBuffer);
        float panelScreenTop = RectTransformUtility.WorldToScreenPoint(null, s_cornerBuffer[1]).y;
        return Input.mousePosition.y < panelScreenTop;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
