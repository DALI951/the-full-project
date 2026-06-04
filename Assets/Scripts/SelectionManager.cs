using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// SelectionManager — right-click move/gather, ground deselect, shift-click, double-click.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; }

    [Header("Layers")]
    [SerializeField] private LayerMask resourceLayer;
    [SerializeField] private LayerMask groundLayer;

    [Header("Group Move")]
    [SerializeField] private float groupSpacing    = 2.2f;
    [SerializeField] private float typeGroupOffset = 6f;

    [Header("Cursor")]
    [SerializeField] private Texture2D swordCursor;
    [SerializeField] private Vector2   cursorHotspot = new Vector2(8f, 8f);

    private List<Unit> selectedUnits    = new List<Unit>();
    private Building   selectedBuilding = null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private Collider[] overlapCache = new Collider[128];

    private void Update()
    {
        UpdateCursor();
        if (IsPointerOverVisiblePanel())
        {
            if (!Input.GetMouseButtonDown(1)) return;
        }
        HandleGroundDeselect();
        HandleRightClick();
    }

    // ── Ground click — deselect + close panels ────────────────────────────
    private void HandleGroundDeselect()
    {
        if (!Input.GetMouseButtonUp(0)) return;
        if (UnitSelectionBox.Instance != null && UnitSelectionBox.Instance.JustFinishedDrag) return;
        if (IsPointerOverVisiblePanel()) return;
        if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsPlacing) return;

        Ray ray = Camera.main != null ? Camera.main.ScreenPointToRay(Input.mousePosition) : new Ray();
        if (Camera.main == null) return;

        bool hitUnit     = false;
        bool hitBuilding = false;
        bool hitResource = false;

        bool hitSite = false;
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            hitUnit     = hit.collider.GetComponentInParent<Unit>()             != null;
            hitBuilding = hit.collider.GetComponentInParent<Building>()         != null;
            hitResource = hit.collider.GetComponentInParent<ResourceNode>()     != null;
            hitSite     = hit.collider.GetComponentInParent<ConstructionSite>() != null;
        }

        if (!hitUnit && !hitBuilding && !hitResource && !hitSite)
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shift) { DeselectAll(); CloseAllPanels(); }
        }
    }

    // ── Right-click: gather or move ───────────────────────────────────────
    private void HandleRightClick()
    {
        if (!Input.GetMouseButtonDown(1)) return;
        if (selectedUnits.Count == 0) return;
        if (IsPointerOverVisiblePanel()) return;

        if (Camera.main == null)
        {
            Debug.LogError("[SelectionManager] Camera.main is NULL!");
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // ── Enemy unit? → attack ──────────────────────────────────────
        if (Physics.Raycast(ray, out RaycastHit unitHit, Mathf.Infinity))
        {
            Unit target = unitHit.collider.GetComponentInParent<Unit>();
            if (target != null && !selectedUnits.Contains(target))
            {
                bool issued = false;
                foreach (Unit u in selectedUnits)
                {
                    if (u is Villager) continue;
                    if (!u.IsEnemy(target)) continue;
                    u.SetAttackTarget(target);
                    u.SetHuntTarget(target.UnitName);
                    issued = true;
                }
                if (issued)
                {
                    MoveFlag.Instance?.ClearAllFlags();
                    foreach (Unit u in selectedUnits)
                        if (u != null) u.ClearWaypoints();
                    return;
                }
            }
        }

        // ── Enemy building? → attack ──────────────────────────────────
        if (Physics.Raycast(ray, out RaycastHit bldHit, Mathf.Infinity))
        {
            Building bldTarget = bldHit.collider.GetComponentInParent<Building>();
            if (bldTarget != null && bldTarget.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex)
            {
                bool isAlly = NetworkedPlayer.LocalInstance != null
                    && NetworkedPlayer.SameTeam(PlayerColorManager.LocalPlayerIndex, bldTarget.OwnerPlayerId);
                if (!isAlly)
                {
                    bool issued = false;
                    foreach (Unit u in selectedUnits)
                    {
                        if (u is Villager) continue;
                        u.SetBuildingTarget(bldTarget);
                        issued = true;
                    }
                    if (issued) { MoveFlag.Instance?.ClearAllFlags(); return; }
                }
            }
        }

        // ── Construction site? → assign villagers to build ───────────────
        if (Physics.Raycast(ray, out RaycastHit siteHit, Mathf.Infinity))
        {
            ConstructionSite site = siteHit.collider.GetComponentInParent<ConstructionSite>();
            if (site != null && !site.IsComplete)
            {
                if (NetworkedPlayer.LocalInstance != null)
                {
                    foreach (Unit u in selectedUnits)
                    {
                        if (u is Villager v)
                            NetworkedPlayer.LocalInstance.CmdAssignBuilder(site.netId, v.netId);
                    }
                }
                else
                {
                    foreach (Unit u in selectedUnits)
                    {
                        if (u is Villager v) v.BuildAt(site);
                    }
                }
                MoveFlag.Instance?.ClearAllFlags();
                return;
            }
        }

        // ── Resource Building? → send selected villagers to work ─────────
        if (Physics.Raycast(ray, out RaycastHit rbHit, Mathf.Infinity))
        {
            ResourceBuilding rb = rbHit.collider.GetComponentInParent<ResourceBuilding>();
            if (rb != null)
            {
                bool assigned = false;
                foreach (Unit u in selectedUnits)
                {
                    if (u is Villager v && v.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                        if (rb.TryAddVillager(v)) assigned = true;
                }
                if (assigned) { MoveFlag.Instance?.ClearAllFlags(); return; }
            }
        }

        // ── Resource node? → gather (Villagers) or move (military) ────
        if (Physics.Raycast(ray, out RaycastHit resHit, Mathf.Infinity, resourceLayer))
        {
            ResourceNode node = resHit.collider.GetComponentInParent<ResourceNode>();
            if (node != null)
            {
                MoveFlag.Instance?.ClearAllFlags();
                foreach (Unit u in selectedUnits)
                    if (u != null) u.ClearWaypoints();

                if (NetworkedPlayer.LocalInstance != null)
                {
                    Vector3 pos = node.transform.position;
                    foreach (Unit u in selectedUnits)
                    {
                        if (u is Villager v)
                            NetworkedPlayer.LocalInstance.CmdVillagerGatherAt(v.netId, pos);
                        else
                            NetworkedPlayer.LocalInstance.CmdMoveUnit(u.netId, pos);
                    }
                }
                else
                {
                    foreach (Unit u in selectedUnits)
                    {
                        if (u is Villager v) v.GatherFrom(node);
                        else                 u.SetFirstWaypoint(node.transform.position);
                    }
                }
                return;
            }
        }

        // ── Ground → move in formation ────────────────────────────────
        if (!TryGetGroundPoint(out Vector3 groundPoint)) return;

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (shift)
        {
            AddFormationWaypoints(groundPoint);
            MoveFlag.Instance?.ShowFlag(groundPoint, true);
        }
        else
        {
            if (NetworkedPlayer.LocalInstance != null)
            {
                uint[] ids = new uint[selectedUnits.Count];
                for (int i = 0; i < selectedUnits.Count; i++)
                    ids[i] = selectedUnits[i].netId;
                NetworkedPlayer.LocalInstance.CmdMoveUnits(ids, groundPoint);
            }
            else
            {
                foreach (Unit u in selectedUnits)
                    if (u != null) u.ClearWaypoints();
            }

            MoveFlag.Instance?.ClearAllFlags();
            MoveFlag.Instance?.ShowFlag(groundPoint, false);
            if (NetworkedPlayer.LocalInstance == null)
                AssignFormationWaypoints(groundPoint);
        }
    }

    public static bool IsPointerOverInteractiveUI()
    {
        if (EventSystem.current == null) return false;
        var pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = Input.mousePosition;
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var r in results)
        {
            string name = r.gameObject.name.ToLower();
            if (name.Contains("viewport")) continue;
            if (name.Contains("rawimage") && !name.Contains("button")) continue;
            if (name.Contains("tooltip")) continue; 
            if (name.Contains("panel") && !name.Contains("button")) continue;

            if (r.gameObject.GetComponent<Button>() != null) return true;
            if (r.gameObject.GetComponent<TMPro.TMP_InputField>() != null) return true;
            if (r.gameObject.GetComponent<Slider>() != null) return true;
            if (name.Contains("button")) return true;
        }
        return false;
    }

    private bool TryGetGroundPoint(out Vector3 point)
    {
        if (Camera.main == null)
        {
            point = Vector3.zero;
            return false;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        // Try dedicated ground layer first
        if (groundLayer != 0)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
            {
                point = hit.point;
                return true;
            }
        }

        // Fallback: raycast everything, skip units/buildings/resources
        RaycastHit[] all = Physics.RaycastAll(ray, Mathf.Infinity);
        System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));
        int hitCount = Mathf.Min(all.Length, overlapCache.Length);

        for (int i = 0; i < hitCount; i++)
        {
            var h = all[i];
            if (h.collider.GetComponentInParent<Unit>() != null) continue;
            if (h.collider.GetComponentInParent<Building>() != null) continue;
            if (h.collider.GetComponentInParent<ResourceNode>() != null) continue;

            point = h.point;
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    // ── Formation movement by unit type ─────────────────────────────────
    public void AssignFormationWaypoints(Vector3 center)
    {
        var groups = new Dictionary<string, List<Unit>>();
        foreach (Unit u in selectedUnits)
        {
            if (u == null) continue;
            if (!groups.ContainsKey(u.UnitName))
                groups[u.UnitName] = new List<Unit>();
            groups[u.UnitName].Add(u);
        }

        int groupIndex = 0;
        int groupCount = groups.Count;
        float totalWidth = (groupCount - 1) * typeGroupOffset;

        foreach (var kvp in groups)
        {
            List<Unit> group = kvp.Value;
            float xShift = -totalWidth / 2f + groupIndex * typeGroupOffset;
            Vector3 groupCenter = center + new Vector3(xShift, 0, 0);

            float slowest = float.MaxValue;
            foreach (Unit u in group)
                if (u != null && u.BaseSpeed < slowest) slowest = u.BaseSpeed;

            int cols = Mathf.CeilToInt(Mathf.Sqrt(group.Count));
            for (int i = 0; i < group.Count; i++)
            {
                Unit u = group[i];
                if (u == null) continue;

                int   col  = i % cols;
                int   row  = i / cols;
                float xOff = (col - (cols - 1) / 2f) * groupSpacing;
                float zOff = -row * groupSpacing;

                Vector3 dest = groupCenter + new Vector3(xOff, 0, zOff);

                if (MapBoundary.Instance != null)
                    dest = MapBoundary.Instance.Clamp(dest);

                if (UnityEngine.AI.NavMesh.SamplePosition(dest, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                    dest = hit.position;

                u.SetMoveSpeed(slowest);
                u.SetFirstWaypoint(dest);
            }
            groupIndex++;
        }

        StartCoroutine(RestoreSpeedsWhenArrived());
    }

    private IEnumerator RestoreSpeedsWhenArrived()
    {
        yield return new WaitForSeconds(0.3f);
        while (true)
        {
            bool done = true;
            foreach (Unit u in selectedUnits)
            {
                if (u == null) continue;
                var ag = u.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (ag != null && ag.isOnNavMesh && ag.remainingDistance > 0.5f)
                { done = false; break; }
            }
            if (done) break;
            yield return null;
        }
        MoveFlag.Instance?.ClearAllFlags(); 
        foreach (Unit u in selectedUnits)
            if (u != null) u.RestoreSpeed();
    }

    // ── PUBLIC — called by Unit.OnMouseDown ───────────────────────────────
    public void SelectSingleUnit(Unit unit)
    {
        if (unit == null || unit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex)
            return;

        DeselectAll();
        selectedUnits.Add(unit);
        unit.Select(PlayerColorManager.LocalPlayerColor);
        UnitInfoUI.Instance?.ShowUnit(unit);
        GameUI.Instance?.HideBuildingUI();
        ResourceInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.Hide();
    }

    public void ShiftClickUnit(Unit unit)
    {
        if (unit == null || unit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex)
            return;

        if (selectedUnits.Contains(unit))
        { unit.Deselect(); selectedUnits.Remove(unit); }
        else
        { selectedUnits.Add(unit); unit.Select(PlayerColorManager.LocalPlayerColor); }
        RefreshInfoPanelPublic();
    }

    // ── Shift-queue with formation ──────────────────────────────────────
    public void AddFormationWaypoints(Vector3 center)
    {
        var groups = new Dictionary<string, List<Unit>>();
        foreach (Unit u in selectedUnits)
        {
            if (u == null) continue;
            if (!groups.ContainsKey(u.UnitName))
                groups[u.UnitName] = new List<Unit>();
            groups[u.UnitName].Add(u);
        }

        int groupIndex = 0;
        int groupCount = groups.Count;
        float totalWidth = (groupCount - 1) * typeGroupOffset;

        foreach (var kvp in groups)
        {
            List<Unit> group = kvp.Value;
            float xShift = -totalWidth / 2f + groupIndex * typeGroupOffset;
            Vector3 groupCenter = center + new Vector3(xShift, 0, 0);

            int cols = Mathf.CeilToInt(Mathf.Sqrt(group.Count));
            for (int i = 0; i < group.Count; i++)
            {
                Unit u = group[i];
                if (u == null) continue;

                int col = i % cols;
                int row = i / cols;
                float xOff = (col - (cols - 1) / 2f) * groupSpacing;
                float zOff = -row * groupSpacing;

                Vector3 dest = groupCenter + new Vector3(xOff, 0, zOff);
                if (MapBoundary.Instance != null)
                    dest = MapBoundary.Instance.Clamp(dest);
                if (UnityEngine.AI.NavMesh.SamplePosition(dest, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                    dest = hit.position;

                u.AddWaypoint(dest);
            }
            groupIndex++;
        }
    }

    public void SelectAllVisibleOfType(string name)
    {
        DeselectAll();
        int local = PlayerColorManager.LocalPlayerIndex;
        var list  = UnitSelectionManager.Instance != null
            ? UnitSelectionManager.Instance.allUnitsList
            : null;
        if (list == null)
        {
            foreach (Unit u in FindObjectsOfType<Unit>())
                TryAddVisibleOfType(u, name, local);
        }
        else
        {
            foreach (Unit u in list)
                TryAddVisibleOfType(u, name, local);
        }
        RefreshInfoPanelPublic();
    }

    private void TryAddVisibleOfType(Unit u, string name, int localOwner)
    {
        if (u == null || u.UnitName != name || u.OwnerPlayerId != localOwner) return;
        if (u.IsInsideBuilding) return;
        if (Camera.main == null) return;
        Vector3 sp = Camera.main.WorldToScreenPoint(u.transform.position);
        if (sp.z < 0 || sp.x < 0 || sp.x > Screen.width ||
            sp.y < 0 || sp.y > Screen.height) return;
        selectedUnits.Add(u);
        u.Select(PlayerColorManager.LocalPlayerColor);
    }

    public void SelectBuilding(Building building)
    {
        DeselectAll();
        selectedBuilding = building;
        selectedBuilding.Select();
        UnitInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();
    }

    // ── PUBLIC — called by UnitSelectionBox ───────────────────────────────

    public void AddUnitToSelection(Unit unit)
    {
        if (unit == null || selectedUnits.Contains(unit)) return;
        if (unit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex) return;
        selectedUnits.Add(unit);
        unit.Select(PlayerColorManager.LocalPlayerColor);
    }

    public void RefreshInfoPanelPublic() => RefreshInfoPanel();

    // ── Deselect ─────────────────────────────────────────────────────────

    public void DeselectAll()
    {
        foreach (Unit u in selectedUnits) if (u != null) u.Deselect();
        selectedUnits.Clear();
        MoveFlag.Instance?.ClearAllFlags();
        if (selectedBuilding != null)
        {
            selectedBuilding.Deselect();
            selectedBuilding = null;
            GameUI.Instance?.HideBuildingUI();
        }
        UnitInfoUI.Instance?.Hide();
    }

    private void CloseAllPanels()
    {
        GameUI.Instance?.HideBuildingUI();
        UnitInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.Hide();
    }

    private void RefreshInfoPanel()
    {
        if      (selectedUnits.Count == 1) UnitInfoUI.Instance?.ShowUnit(selectedUnits[0]);
        else if (selectedUnits.Count > 1)  UnitInfoUI.Instance?.ShowMultiple(selectedUnits);
        else                               UnitInfoUI.Instance?.Hide();
    }

    // ── Visible-panel registry ────────────────────────────────────────────
    private static readonly List<RectTransform> s_blockingPanels = new List<RectTransform>();

    public static void RegisterBlockingPanel(RectTransform rt)
    {
        if (rt != null && !s_blockingPanels.Contains(rt))
            s_blockingPanels.Add(rt);
    }

    public static bool IsRegisteredBlockingPanel(RectTransform rt)
        => rt != null && s_blockingPanels.Contains(rt);
    public static bool IsPointerOverVisiblePanel()
    {
        if (EventSystem.current == null) return false;

        var pointerData = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results     = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var r in results)
        {
            if (r.gameObject == null) continue;

            // ── skip visually invisible elements ────────────────────────
            var img = r.gameObject.GetComponent<UnityEngine.UI.Image>();
            if (img != null && img.color.a < 0.01f) continue;

            var cg = r.gameObject.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.01f) continue;
            // ────────────────────────────────────────────────────────────

            Transform t = r.gameObject.transform;
            while (t != null)
            {
                var rt = t.GetComponent<RectTransform>();
                if (rt != null && s_blockingPanels.Contains(rt) && rt.gameObject.activeInHierarchy)
                    return true;
                t = t.parent;
            }
        }
        return false;
    }

    private void UpdateCursor()
    {
        if (selectedUnits.Count == 0 || Camera.main == null)
        { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }

        bool hasMilitary = false;
        foreach (Unit u in selectedUnits)
            if (!(u is Villager)) { hasMilitary = true; break; }
        if (!hasMilitary) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            Unit     eu = hit.collider.GetComponentInParent<Unit>();
            Building eb = hit.collider.GetComponentInParent<Building>();
            bool hostile =
                (eu != null && eu.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex) ||
                (eb != null && eb.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex);
            if (hostile && swordCursor != null)
            { Cursor.SetCursor(swordCursor, cursorHotspot, CursorMode.Auto); return; }
        }
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }
    public void RemoveUnitFromDragSelection(Unit unit)
    {
        if (unit == null) return;
        if (selectedUnits.Remove(unit))
            unit.Deselect();
    }
    public List<Unit> SelectedUnits    => selectedUnits;
    public Building   SelectedBuilding => selectedBuilding;
}