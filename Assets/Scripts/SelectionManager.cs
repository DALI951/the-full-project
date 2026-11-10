using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// SelectionManager — unit/building selection, group movement, minimap/UI click blocking,
/// control groups (1-9), idle villager cycling (Tab/Period), and stop hotkeys (X/H).
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

    // Double-click detection for single-click selection fallback
    private Unit  lastClickedUnit  = null;
    private float lastClickTime    = -99f;
    private const float DOUBLE_CLICK_TIME = 0.35f;

    // Control Groups (1-9)
    private readonly Dictionary<int, List<Unit>> controlGroups = new Dictionary<int, List<Unit>>();
    private int   lastGroupPressed   = -1;
    private float lastGroupPressTime = -99f;
    private const float GROUP_DOUBLE_TAP_TIME = 0.35f;

    // Idle Villager cycling
    private int lastIdleVillagerIndex = -1;
    private bool prevJustFinishedDrag = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        UpdateCursor();
        HandleHotkeys();

        bool overUI = IsPointerOverBlockingUI();
        if (overUI)
        {
            // Do NOT allow clicks through UI / Minimap into the 3D world
            return;
        }

        bool justFinished = UnitSelectionBox.Instance != null && UnitSelectionBox.Instance.JustFinishedDrag;
        if (justFinished && !prevJustFinishedDrag && selectedUnits.Count > 0)
        {
            AudioManager.Instance?.PlayUnitSelect();
        }
        prevJustFinishedDrag = justFinished;

        HandleLeftClick();
        HandleRightClick();
    }

    // ── RTS Hotkeys ───────────────────────────────────────────────────────
    private void HandleHotkeys()
    {
        // Don't trigger hotkeys if typing in an input field
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            if (EventSystem.current.currentSelectedGameObject.GetComponent<TMPro.TMP_InputField>() != null)
                return;
        }

        // 1. Stop Hotkey (X or H)
        if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.H))
        {
            StopSelectedUnits();
        }

        // 2. Idle Villager Hotkey (Period or Tab)
        if (Input.GetKeyDown(KeyCode.Period) || Input.GetKeyDown(KeyCode.KeypadPeriod) || Input.GetKeyDown(KeyCode.Tab))
        {
            SelectNextIdleVillager();
        }

        // 3. Control Groups (1-9)
        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        for (int i = 1; i <= 9; i++)
        {
            KeyCode alphaKey  = KeyCode.Alpha0 + i;
            KeyCode keypadKey = KeyCode.Keypad0 + i;

            if (Input.GetKeyDown(alphaKey) || Input.GetKeyDown(keypadKey))
            {
                if (ctrlHeld)
                {
                    AssignControlGroup(i);
                }
                else
                {
                    SelectControlGroup(i);
                }
                break;
            }
        }
    }

    public void StopSelectedUnits()
    {
        if (selectedUnits == null || selectedUnits.Count == 0) return;

        foreach (Unit u in selectedUnits)
        {
            if (u == null || u.IsDead) continue;
            if (u is Villager v)
            {
                v.CancelGatheringAndBuilding();
                v.EnterIdle();
            }
            u.ClearWaypoints();
            var ag = u.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (ag != null && ag.isOnNavMesh) ag.ResetPath();

            if (NetworkedPlayer.LocalInstance != null && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
            {
                NetworkedPlayer.LocalInstance.CmdMoveUnit(u.netId, u.transform.position);
            }
        }
        MoveFlag.Instance?.ClearAllFlags();
    }

    private void SelectNextIdleVillager()
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

        if (idleVillagers.Count == 0) return;

        lastIdleVillagerIndex = (lastIdleVillagerIndex + 1) % idleVillagers.Count;
        Villager target = idleVillagers[lastIdleVillagerIndex];

        SelectSingleUnit(target);
        CenterCameraOnPosition(target.transform.position);
    }

    private void AssignControlGroup(int groupNum)
    {
        var group = new List<Unit>();
        for (int i = 0; i < selectedUnits.Count; i++)
        {
            Unit u = selectedUnits[i];
            if (u != null && !u.IsDead && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                group.Add(u);
        }
        controlGroups[groupNum] = group;
    }

    private void SelectControlGroup(int groupNum)
    {
        if (!controlGroups.TryGetValue(groupNum, out var group) || group == null) return;

        // Clean dead / null units
        group.RemoveAll(u => u == null || u.IsDead);
        if (group.Count == 0) return;

        float now = Time.unscaledTime;
        bool isDoubleTap = (lastGroupPressed == groupNum && (now - lastGroupPressTime) <= GROUP_DOUBLE_TAP_TIME);
        lastGroupPressed   = groupNum;
        lastGroupPressTime = now;

        DeselectAll();
        for (int i = 0; i < group.Count; i++)
        {
            AddUnitToSelection(group[i]);
        }
        if (group.Count > 0)
        {
            AudioManager.Instance?.PlayUnitSelect();
        }
        RefreshInfoPanelPublic();

        if (isDoubleTap)
        {
            Vector3 center = Vector3.zero;
            for (int i = 0; i < group.Count; i++) center += group[i].transform.position;
            center /= group.Count;
            CenterCameraOnPosition(center);
        }
    }

    public void CenterCameraOnPosition(Vector3 worldPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        float y = cam.transform.position.y;
        if (y <= 0.01f) y = 25f;

        Vector3 fwd = cam.transform.forward;
        Vector3 newPos;
        if (fwd.y < -0.05f)
        {
            float distToGround = y / -fwd.y;
            newPos = new Vector3(
                worldPos.x - fwd.x * distToGround,
                y,
                worldPos.z - fwd.z * distToGround);
        }
        else
        {
            newPos = new Vector3(worldPos.x, y, worldPos.z - 15f);
        }

        if (MapBoundary.Instance != null)
        {
            float limit = MapBoundary.Instance.CameraLimit;
            newPos.x = Mathf.Clamp(newPos.x, -limit, limit);
            newPos.z = Mathf.Clamp(newPos.z, -limit, limit);
        }

        RTSCamera.SkipNextFrame = true;
        cam.transform.position = newPos;
    }

    // ── Left Click: Unit / Building selection or ground deselect ──────────
    private void HandleLeftClick()
    {
        if (!Input.GetMouseButtonUp(0)) return;
        if (UnitSelectionBox.Instance != null && UnitSelectionBox.Instance.JustFinishedDrag) return;
        if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsPlacing) return;
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Unit clickedUnit = null;

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            clickedUnit = hit.collider.GetComponentInParent<Unit>();
            if (clickedUnit == null)
            {
                Building bld = hit.collider.GetComponentInParent<Building>();
                if (bld != null)
                {
                    SelectBuilding(bld);
                    return;
                }
                ResourceNode res = hit.collider.GetComponentInParent<ResourceNode>();
                if (res != null)
                {
                    DeselectAll();
                    ResourceInfoUI.Instance?.Show(res);
                    return;
                }
                ConstructionSite site = hit.collider.GetComponentInParent<ConstructionSite>();
                if (site != null)
                {
                    DeselectAll();
                    return;
                }
            }
        }

        // Fallback: if exact raycast missed a unit, check SphereCast or OverlapSphere near hit ground
        if (clickedUnit == null)
        {
            if (Physics.SphereCast(ray, 0.6f, out RaycastHit sphereHit, Mathf.Infinity))
            {
                clickedUnit = sphereHit.collider.GetComponentInParent<Unit>();
            }

            if (clickedUnit == null && hit.collider != null)
            {
                Collider[] overlaps = Physics.OverlapSphere(hit.point, 0.9f);
                float closestDist = float.MaxValue;
                for (int i = 0; i < overlaps.Length; i++)
                {
                    Unit u = overlaps[i].GetComponentInParent<Unit>();
                    if (u != null && !u.IsDead)
                    {
                        float d = Vector3.Distance(hit.point, u.transform.position);
                        if (d < closestDist)
                        {
                            closestDist = d;
                            clickedUnit = u;
                        }
                    }
                }
            }
        }

        if (clickedUnit != null)
        {
            if (clickedUnit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex)
            {
                UnitInfoUI.Instance?.ShowEnemyUnit(clickedUnit);
                return;
            }

            float now = Time.unscaledTime;
            bool isDouble = (clickedUnit == lastClickedUnit && (now - lastClickTime) <= DOUBLE_CLICK_TIME);
            lastClickTime = now;
            lastClickedUnit = clickedUnit;

            if (isDouble)
            {
                SelectAllVisibleOfType(clickedUnit.UnitName);
            }
            else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                ShiftClickUnit(clickedUnit);
            }
            else
            {
                SelectSingleUnit(clickedUnit);
            }
            return;
        }

        // Ground clicked: deselect
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!shift)
        {
            DeselectAll();
            CloseAllPanels();
        }
    }

    public void HandleGroundDeselect()
    {
        // Aliased to HandleLeftClick for backwards compatibility
        HandleLeftClick();
    }

    // ── Right-click: gather or move ───────────────────────────────────────
    private void HandleRightClick()
    {
        if (!Input.GetMouseButtonDown(1)) return;
        if (selectedUnits.Count == 0) return;
        if (Camera.main == null) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity);

        // ── Enemy unit? → attack ──────────────────────────────────────
        if (hit.collider != null)
        {
            Unit target = hit.collider.GetComponentInParent<Unit>();
            if (target != null && !selectedUnits.Contains(target))
            {
                bool issued = false;
                Mirror.NetworkIdentity targetNi = target.GetComponent<Mirror.NetworkIdentity>();
                if (NetworkedPlayer.LocalInstance != null && targetNi != null)
                {
                    var idsList = new List<uint>();
                    foreach (Unit u in selectedUnits)
                    {
                        if (u == null || u is Villager) continue;
                        if (!u.IsEnemy(target)) continue;
                        idsList.Add(u.netId);
                        u.SetHuntTarget(target.UnitName);
                        issued = true;
                    }
                    if (idsList.Count > 0)
                    {
                        NetworkedPlayer.LocalInstance.CmdUnitAttack(idsList.ToArray(), targetNi.netId);
                    }
                }
                else
                {
                    foreach (Unit u in selectedUnits)
                    {
                        if (u is Villager) continue;
                        if (!u.IsEnemy(target)) continue;
                        u.SetAttackTarget(target);
                        u.SetHuntTarget(target.UnitName);
                        issued = true;
                    }
                }

                if (issued)
                {
                    AudioManager.Instance?.PlayUnitAttack();
                    EffectManager.Instance?.PlayHitSpark(target.transform.position);
                    MoveFlag.Instance?.ClearAllFlags();
                    foreach (Unit u in selectedUnits)
                        if (u != null) u.ClearWaypoints();
                    return;
                }
            }
        }

        // ── Enemy building? → attack ──────────────────────────────────
        if (hit.collider != null)
        {
            Building bldTarget = hit.collider.GetComponentInParent<Building>();
            if (bldTarget != null && bldTarget.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex)
            {
                bool isAlly = NetworkedPlayer.LocalInstance != null
                    && NetworkedPlayer.SameTeam(PlayerColorManager.LocalPlayerIndex, bldTarget.OwnerPlayerId);
                if (!isAlly)
                {
                    bool issued = false;
                    Mirror.NetworkIdentity bldNi = bldTarget.GetComponent<Mirror.NetworkIdentity>();
                    if (NetworkedPlayer.LocalInstance != null && bldNi != null)
                    {
                        uint[] ids = GetNetIds(selectedUnits);
                        if (ids.Length > 0)
                        {
                            NetworkedPlayer.LocalInstance.CmdUnitAttackBuilding(ids, bldNi.netId);
                            issued = true;
                        }
                    }
                    else
                    {
                        foreach (Unit u in selectedUnits)
                        {
                            if (u == null || u is Villager) continue;
                            u.SetBuildingTarget(bldTarget);
                            issued = true;
                        }
                    }

                    if (issued)
                    {
                        AudioManager.Instance?.PlayUnitAttack();
                        EffectManager.Instance?.PlayHitSpark(bldTarget.transform.position);
                        MoveFlag.Instance?.ClearAllFlags();
                        foreach (Unit u in selectedUnits)
                            if (u != null) u.ClearWaypoints();
                        return;
                    }
                }
            }
        }

        // ── Construction site? → assign villagers to build ───────────────
        if (hit.collider != null)
        {
            ConstructionSite site = hit.collider.GetComponentInParent<ConstructionSite>();
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
        if (hit.collider != null)
        {
            ResourceBuilding rb = hit.collider.GetComponentInParent<ResourceBuilding>();
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
        ResourceNode node = null;
        if (hit.collider != null)
            node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node == null && Physics.Raycast(ray, out RaycastHit resHit, Mathf.Infinity, resourceLayer))
            node = resHit.collider.GetComponentInParent<ResourceNode>();

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

        // ── Ground → move in formation ────────────────────────────────
        if (!TryGetGroundPoint(out Vector3 groundPoint)) return;

        AudioManager.Instance?.PlayUnitMove();

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (NetworkedPlayer.LocalInstance != null)
        {
            CalculateFormationPositions(groundPoint, out uint[] ids, out Vector3[] formationPositions);
            if (ids.Length > 0)
            {
                NetworkedPlayer.LocalInstance.CmdMoveUnitsFormation(ids, formationPositions, shift);
            }

            if (shift)
            {
                MoveFlag.Instance?.ShowFlag(groundPoint, true);
            }
            else
            {
                MoveFlag.Instance?.ClearAllFlags();
                MoveFlag.Instance?.ShowFlag(groundPoint, false);
            }
        }
        else
        {
            if (shift)
            {
                AddFormationWaypoints(groundPoint);
                MoveFlag.Instance?.ShowFlag(groundPoint, true);
            }
            else
            {
                foreach (Unit u in selectedUnits)
                    if (u != null) u.ClearWaypoints();

                MoveFlag.Instance?.ClearAllFlags();
                MoveFlag.Instance?.ShowFlag(groundPoint, false);
                AssignFormationWaypoints(groundPoint);
            }
        }
    }

    public uint[] GetNetIds(List<Unit> units)
    {
        var list = new List<uint>();
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i] != null && !(units[i] is Villager) && units[i].OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                list.Add(units[i].netId);
        }
        return list.ToArray();
    }

    public void CalculateFormationPositions(Vector3 center, out uint[] ids, out Vector3[] positions)
    {
        var validUnits = new List<Unit>();
        for (int i = 0; i < selectedUnits.Count; i++)
        {
            if (selectedUnits[i] != null && selectedUnits[i].OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                validUnits.Add(selectedUnits[i]);
        }

        var groups = new Dictionary<string, List<Unit>>();
        foreach (Unit u in validUnits)
        {
            if (!groups.ContainsKey(u.UnitName))
                groups[u.UnitName] = new List<Unit>();
            groups[u.UnitName].Add(u);
        }

        int groupIndex = 0;
        int groupCount = groups.Count;
        float totalWidth = (groupCount - 1) * typeGroupOffset;

        var idList = new List<uint>();
        var posList = new List<Vector3>();

        foreach (var kvp in groups)
        {
            List<Unit> group = kvp.Value;
            float xShift = -totalWidth / 2f + groupIndex * typeGroupOffset;
            Vector3 groupCenter = center + new Vector3(xShift, 0, 0);

            int cols = Mathf.CeilToInt(Mathf.Sqrt(group.Count));
            for (int i = 0; i < group.Count; i++)
            {
                Unit u = group[i];
                int col = i % cols;
                int row = i / cols;
                float xOff = (col - (cols - 1) / 2f) * groupSpacing;
                float zOff = -row * groupSpacing;

                Vector3 dest = groupCenter + new Vector3(xOff, 0, zOff);
                if (MapBoundary.Instance != null)
                    dest = MapBoundary.Instance.Clamp(dest);
                if (UnityEngine.AI.NavMesh.SamplePosition(dest, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas))
                    dest = hit.position;

                idList.Add(u.netId);
                posList.Add(dest);
            }
            groupIndex++;
        }

        ids = idList.ToArray();
        positions = posList.ToArray();
    }

    // ── Shared UI raycast cache ─────────────────────────────────────────────
    private static PointerEventData   s_uiPointer;
    private static List<RaycastResult> s_uiRaycastResults = new List<RaycastResult>(32);

    private static bool RaycastUI()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;

        if (s_uiPointer == null) s_uiPointer = new PointerEventData(es);
        s_uiPointer.Reset();
        s_uiPointer.position = Input.mousePosition;

        s_uiRaycastResults.Clear();
        es.RaycastAll(s_uiPointer, s_uiRaycastResults);
        return true;
    }

    public static bool IsPointerOverInteractiveUI()
    {
        if (IsPointerOverBlockingUI()) return true;

        if (!RaycastUI()) return false;

        for (int i = 0; i < s_uiRaycastResults.Count; i++)
        {
            RaycastResult r = s_uiRaycastResults[i];
            if (r.gameObject == null) continue;
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

    private static int CompareHitsByDistance(RaycastHit a, RaycastHit b)
        => a.distance.CompareTo(b.distance);

    private bool TryGetGroundPoint(out Vector3 point)
    {
        if (Camera.main == null)
        {
            point = Vector3.zero;
            return false;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (groundLayer != 0)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
            {
                point = hit.point;
                return true;
            }
        }

        RaycastHit[] all = Physics.RaycastAll(ray, Mathf.Infinity);
        System.Array.Sort(all, CompareHitsByDistance);

        for (int i = 0; i < all.Length; i++)
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

    // ── PUBLIC Selection methods ──────────────────────────────────────────
    public void SelectSingleUnit(Unit unit)
    {
        if (unit == null || unit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex)
            return;

        DeselectAll();
        selectedUnits.Add(unit);
        unit.Select(PlayerColorManager.LocalPlayerColor);
        AudioManager.Instance?.PlayUnitSelect();
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
        {
            selectedUnits.Add(unit);
            unit.Select(PlayerColorManager.LocalPlayerColor);
            AudioManager.Instance?.PlayUnitSelect();
        }
        RefreshInfoPanelPublic();
    }

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
        if (selectedUnits.Count > 0)
        {
            AudioManager.Instance?.PlayUnitSelect();
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

    public void AddUnitToSelection(Unit unit)
    {
        if (unit == null || selectedUnits.Contains(unit)) return;
        if (unit.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex) return;
        selectedUnits.Add(unit);
        unit.Select(PlayerColorManager.LocalPlayerColor);
    }

    public void RefreshInfoPanelPublic() => RefreshInfoPanel();

    public void SelectOnlyUnits(List<Unit> unitsToKeep)
    {
        if (unitsToKeep == null || unitsToKeep.Count == 0)
        {
            DeselectAll();
            return;
        }

        var toRemove = new List<Unit>();
        foreach (Unit u in selectedUnits)
        {
            if (u == null || !unitsToKeep.Contains(u))
            {
                toRemove.Add(u);
            }
        }

        foreach (Unit u in toRemove)
        {
            if (u != null) u.Deselect();
            selectedUnits.Remove(u);
        }

        foreach (Unit u in unitsToKeep)
        {
            if (u != null && !selectedUnits.Contains(u) && u.OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
            {
                selectedUnits.Add(u);
                u.Select(PlayerColorManager.LocalPlayerColor);
            }
        }

        if (selectedUnits.Count > 0)
        {
            AudioManager.Instance?.PlayUnitSelect();
        }

        RefreshInfoPanelPublic();
        SelectionTypeBar.Instance?.ShowTypes(selectedUnits);
    }

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

    // ── Visible-panel & UI Blocker Registry ─────────────────────────────────
    private static readonly List<RectTransform> s_blockingPanels = new List<RectTransform>();

    public static void RegisterBlockingPanel(RectTransform rt)
    {
        if (rt != null && !s_blockingPanels.Contains(rt))
            s_blockingPanels.Add(rt);
    }

    public static bool IsRegisteredBlockingPanel(RectTransform rt)
        => rt != null && s_blockingPanels.Contains(rt);

    /// <summary>
    /// Checks whether the cursor is over the Minimap, an EventSystem UI element, or a registered blocking panel.
    /// Prevents any clicks or drags from penetrating through the UI into the 3D game world.
    /// </summary>
    public static bool IsPointerOverBlockingUI()
    {
        // 1. Minimap check (with 6px border padding)
        if (MinimapSystem.Instance != null && MinimapSystem.Instance.IsPointerOverMinimap())
            return true;

        // 2. EventSystem check
        EventSystem es = EventSystem.current;
        if (es != null && es.IsPointerOverGameObject())
        {
            if (RaycastUI())
            {
                for (int i = 0; i < s_uiRaycastResults.Count; i++)
                {
                    RaycastResult r = s_uiRaycastResults[i];
                    if (r.gameObject == null) continue;

                    // Skip zero-alpha items unless explicitly registered
                    var img = r.gameObject.GetComponent<Image>();
                    var rt = r.gameObject.GetComponent<RectTransform>();
                    if (img != null && img.color.a < 0.01f && (rt == null || !s_blockingPanels.Contains(rt)))
                        continue;

                    var cg = r.gameObject.GetComponent<CanvasGroup>();
                    if (cg != null && cg.alpha < 0.01f)
                        continue;

                    // Registered blocking panels
                    Transform t = r.gameObject.transform;
                    while (t != null)
                    {
                        var panelRt = t.GetComponent<RectTransform>();
                        if (panelRt != null && s_blockingPanels.Contains(panelRt) && panelRt.gameObject.activeInHierarchy)
                            return true;
                        t = t.parent;
                    }

                    // Any active UI Selectable, Button, Toggle, Slider, ScrollRect, TMP_InputField, RawImage
                    if (r.gameObject.GetComponent<Selectable>() != null) return true;
                    if (r.gameObject.GetComponent<Button>() != null) return true;
                    if (r.gameObject.GetComponent<RawImage>() != null) return true;
                    if (r.gameObject.GetComponent<ScrollRect>() != null) return true;
                    if (r.gameObject.GetComponent<TMPro.TMP_InputField>() != null) return true;

                    string lowerName = r.gameObject.name.ToLower();
                    if (lowerName.Contains("button") || lowerName.Contains("panel") || lowerName.Contains("minimap") || lowerName.Contains("bar"))
                        return true;
                }
            }
        }

        // 3. Fallback check for registered panels directly
        return IsPointerOverVisiblePanel();
    }

    public static bool IsPointerOverVisiblePanel()
    {
        if (MinimapSystem.Instance != null && MinimapSystem.Instance.IsPointerOverMinimap())
            return true;

        if (!RaycastUI()) return false;

        for (int i = 0; i < s_uiRaycastResults.Count; i++)
        {
            RaycastResult r = s_uiRaycastResults[i];
            if (r.gameObject == null) continue;

            var img = r.gameObject.GetComponent<UnityEngine.UI.Image>();
            var rt = r.gameObject.GetComponent<RectTransform>();
            if (img != null && img.color.a < 0.01f && (rt == null || !s_blockingPanels.Contains(rt)))
                continue;

            var cg = r.gameObject.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.01f) continue;

            Transform t = r.gameObject.transform;
            while (t != null)
            {
                var panelRt = t.GetComponent<RectTransform>();
                if (panelRt != null && s_blockingPanels.Contains(panelRt) && panelRt.gameObject.activeInHierarchy)
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

    public List<Unit> GetSelectedUnits() => selectedUnits;
    public List<Unit> SelectedUnits     => selectedUnits;
    public Building   SelectedBuilding  => selectedBuilding;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
