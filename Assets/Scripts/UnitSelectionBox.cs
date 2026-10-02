using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// UnitSelectionBox — drag-to-select rectangle.
///
/// Fix Bug 3: startPosition is now recorded at the very first line of
/// GetMouseButtonDown, before any EventSystem or UI checks.
/// This prevents the box from starting at (0,0) when the click origin
/// was briefly on a UI element.
///
/// Attach to GameManager.
/// Assign boxVisual (UI Image, pivot+anchor = 0.5,0.5) and uiCanvas.
/// </summary>
public class UnitSelectionBox : MonoBehaviour
{
    Camera myCam;

    [SerializeField] RectTransform boxVisual;
    [SerializeField] Canvas        uiCanvas;
    [SerializeField] float         dragThreshold = 8f;
    [SerializeField] private RectTransform bottomBar;

    Vector2 startPosition;      // screen-space start of drag
    Vector2 endPosition;
    bool    isDragging       = false;
    bool    hasDragOrigin    = false; // replaces checking startPosition == Vector2.zero (ambiguous at screen origin)
    bool    startOnUI        = false; // was the click origin on a UI element?
    public static UnitSelectionBox Instance { get; private set; }
    public bool JustFinishedDrag { get; private set; }
    private HashSet<Unit> _realtimeDragUnits = new HashSet<Unit>();

    // Scratch sets reused across frames. UpdateRealtimeSelection() runs EVERY
    // frame while dragging and used to allocate a fresh HashSet<Unit> each time,
    // then reassigned _realtimeDragUnits to it (so the old one became garbage).
    private HashSet<Unit> _nextDragUnits = new HashSet<Unit>();
    private readonly List<Unit>  s_removedBuffer = new List<Unit>(64);

    private static readonly Vector3[] s_cornerBuffer = new Vector3[4];

    private void Start()
    {
        Instance = this;
        myCam = Camera.main;
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

        if (SelectionManager.IsPointerOverVisiblePanel() && !isDragging)
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

        if (isDown && !isDragging && !hasDragOrigin && !IsMouseOverBottomPanel())
        {
            hasDragOrigin = true;
            startPosition = Input.mousePosition;
            endPosition   = startPosition;
            startOnUI = SelectionManager.IsPointerOverVisiblePanel();
        }

        if (isDown && !startOnUI && !IsMouseOverBottomPanel())
        {
            endPosition = Input.mousePosition;

            if (!isDragging &&
                Vector2.Distance(startPosition, endPosition) > dragThreshold)
            {
                isDragging = true;
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
    
    // ── Select units inside the rect ──────────────────────────────────────
    void SelectUnits()
    {
        // Real-time selection already built the correct state
        if (_realtimeDragUnits.Count > 0)
        {
            _realtimeDragUnits.Clear();
            SelectionManager.Instance?.RefreshInfoPanelPublic();
            return;
        }

        bool shift = Input.GetKey(KeyCode.LeftShift) ||
                    Input.GetKey(KeyCode.RightShift);

        if (!shift) SelectionManager.Instance?.DeselectAll();

        Rect currentRect = new Rect(
            Mathf.Min(startPosition.x, endPosition.x),
            Mathf.Min(startPosition.y, endPosition.y),
            Mathf.Abs(endPosition.x - startPosition.x),
            Mathf.Abs(endPosition.y - startPosition.y));

        var list = UnitSelectionManager.Instance?.allUnitsList;

        if (list == null)
        { Debug.LogError("[SelectionBox] UnitSelectionManager list is NULL"); return; }

        foreach (Unit unit in list)
        {
            if (unit == null) continue;
            if (unit.IsInsideBuilding) continue;
            Vector3 sp = myCam.WorldToScreenPoint(unit.transform.position);
            if (sp.z < 0) continue;
            if (currentRect.Contains(new Vector2(sp.x, sp.y)))
                SelectionManager.Instance?.AddUnitToSelection(unit);
        }

        SelectionManager.Instance?.RefreshInfoPanelPublic();
    }

    // ── Reset ─────────────────────────────────────────────────────────────
    void CancelDrag()
    {
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
        // corners[] was allocated fresh on every call, and this runs 2-3x per frame.
        bottomBar.GetWorldCorners(s_cornerBuffer);
        // corners[0] = bottom-left, corners[1] = top-left in world space
        // Convert to screen space
        float panelScreenTop = RectTransformUtility.WorldToScreenPoint(null, s_cornerBuffer[1]).y;
        return Input.mousePosition.y < panelScreenTop;
    }
    private void UpdateRealtimeSelection()
    {
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        Rect currentRect = new Rect(
            Mathf.Min(startPosition.x, endPosition.x),
            Mathf.Min(startPosition.y, endPosition.y),
            Mathf.Abs(endPosition.x - startPosition.x),
            Mathf.Abs(endPosition.y - startPosition.y));

        var list = UnitSelectionManager.Instance?.allUnitsList;
        if (list == null) return;

        _nextDragUnits.Clear();
        foreach (Unit unit in list)
        {
            if (unit == null || unit.IsInsideBuilding) continue;
            if (!unit.IsSelectable) continue;
            if (unit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex) continue;
            Vector3 sp = myCam.WorldToScreenPoint(unit.transform.position);
            if (sp.z < 0) continue;
            if (currentRect.Contains(new Vector2(sp.x, sp.y)))
                _nextDragUnits.Add(unit);
        }

        foreach (Unit u in _nextDragUnits)
            if (!_realtimeDragUnits.Contains(u))
                SelectionManager.Instance?.AddUnitToSelection(u);

        if (!shift)
        {
            // RemoveAt on the enumerator would throw, so snapshot the keys.
            s_removedBuffer.Clear();
            foreach (Unit u in _realtimeDragUnits)
                if (!_nextDragUnits.Contains(u))
                    s_removedBuffer.Add(u);
            for (int i = 0; i < s_removedBuffer.Count; i++)
                SelectionManager.Instance?.RemoveUnitFromDragSelection(s_removedBuffer[i]);
        }

        // Swap the two sets instead of allocating a new one.
        HashSet<Unit> previous = _realtimeDragUnits;
        _realtimeDragUnits = _nextDragUnits;
        _nextDragUnits = previous;

        SelectionManager.Instance?.RefreshInfoPanelPublic();
    }

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// A destroyed MonoBehaviour is NOT a real CLR null (Unity fake-null only affects
    /// == and !=). Any "Instance?.Foo()" call site elsewhere would therefore still
    /// dispatch into this dead object and throw MissingReferenceException the next
    /// time the scene reloads. Clearing the field makes it a true null.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
