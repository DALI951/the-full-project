using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MinimapSystem : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    public static MinimapSystem Instance { get; private set; }

    [Header("Camera")]
    [SerializeField] private Camera mainCamera;

    [Header("Dot Sizes")]
    [SerializeField] private float unitDotSize = 5f;
    [SerializeField] private float buildingDotSize = 8f;
    [SerializeField] private float resourceDotSize = 2f;

    [Header("Layers")]
    [Tooltip("Set to your Ground / Terrain layer. Leave as 'Everything' if unsure.")]
    [SerializeField] private LayerMask groundLayerMask = ~0;

    private RectTransform[] vpCornerDots = new RectTransform[4];
    private RectTransform[] vpLines      = new RectTransform[4];
    private Vector2[]       vpMiniPos    = new Vector2[4];

    private RectTransform minimapRect;
    private Dictionary<Transform, RectTransform> dots = new Dictionary<Transform, RectTransform>();

    private enum DotKind : byte { Resource = 1, Unit = 2, Building = 3 }

    private struct DotEntry
    {
        public RectTransform Dot;
        public DotKind       Kind;
        public Unit          Unit;      // only meaningful when Kind == Unit
        public int           OwnerId;   // cached for buildings; units read live off Unit
    }

    private readonly Dictionary<Transform, DotEntry> dotKinds = new Dictionary<Transform, DotEntry>();

    // Reused buffers - these were allocated fresh every single frame.
    private readonly List<Transform> staleDots = new List<Transform>();
    private static readonly Vector3[] viewportCorners =
    {
        new Vector3(0f, 0f, 0f), // bottom-left
        new Vector3(1f, 0f, 0f), // bottom-right
        new Vector3(1f, 1f, 0f), // top-right
        new Vector3(0f, 1f, 0f), // top-left
    };

    // Viewport-rect dirty tracking
    private Vector3? _lastViewportCamPos;
    private Vector3 _lastViewportCamRot;
    private float   _lastViewportAspect;

    private RectTransform   vpContainer;
    private bool isDraggingMinimapCam = false;
    private int lastCommandFrame = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        minimapRect = GetComponent<RectTransform>();
        CreateViewportOverlay();

        Graphic graphic = GetComponent<Graphic>();
        if (graphic == null)
        {
            graphic = gameObject.AddComponent<Image>();
            ((Image)graphic).color = new Color(0, 0, 0, 0.01f);
        }
        graphic.raycastTarget = true;

        RegisterWithSelectionManager();
    }

    private void Start()
    {
        RegisterWithSelectionManager();
        ScanExisting();
    }

    private void RegisterWithSelectionManager()
    {
        if (minimapRect != null)
        {
            SelectionManager.RegisterBlockingPanel(minimapRect);
            if (minimapRect.parent is RectTransform parentRt)
            {
                SelectionManager.RegisterBlockingPanel(parentRt);
            }
        }
    }

    /// <summary>
    /// Checks whether the screen mouse position is over the minimap or its immediate 6px boundary padding.
    /// </summary>
    public bool IsPointerOverMinimap()
    {
        if (minimapRect == null || !gameObject.activeInHierarchy) return false;

        Canvas canvas = GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        if (RectTransformUtility.RectangleContainsScreenPoint(minimapRect, Input.mousePosition, cam))
            return true;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(minimapRect, Input.mousePosition, cam, out Vector2 local))
        {
            Rect r = minimapRect.rect;
            r.xMin -= 6f;
            r.xMax += 6f;
            r.yMin -= 6f;
            r.yMax += 6f;
            if (r.Contains(local))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Converts a screen point to normalized (0..1, 0..1) coordinates across the minimap rectangle.
    /// Clamps edge clicks so border clicks map cleanly to the world edge.
    /// </summary>
    public bool TryGetNormalized(Vector2 screenPoint, out Vector2 norm)
    {
        norm = Vector2.zero;
        if (minimapRect == null) return false;

        Canvas canvas = GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(minimapRect, screenPoint, cam, out Vector2 local))
            return false;

        Rect rect = minimapRect.rect;
        if (rect.width <= 0f || rect.height <= 0f) return false;

        // Allow 6px padding around the rect
        if (local.x < rect.xMin - 6f || local.x > rect.xMax + 6f ||
            local.y < rect.yMin - 6f || local.y > rect.yMax + 6f)
            return false;

        float normX = Mathf.Clamp01((local.x - rect.xMin) / rect.width);
        float normY = Mathf.Clamp01((local.y - rect.yMin) / rect.height);
        norm = new Vector2(normX, normY);
        return true;
    }

    public bool TryGetNormalized(out Vector2 norm)
    {
        return TryGetNormalized(Input.mousePosition, out norm);
    }

    /// <summary>
    /// Maps normalized minimap coordinates (0..1, 0..1) to world X, Z coordinates.
    /// </summary>
    public Vector3 MinimapToWorld(Vector2 norm)
    {
        float halfSize = MapBoundary.Instance != null ? MapBoundary.Instance.HalfSize : 500f;
        float worldX = (norm.x * halfSize * 2f) - halfSize;
        float worldZ = (norm.y * halfSize * 2f) - halfSize;
        return new Vector3(worldX, 0f, worldZ);
    }

    private void Update()
    {
        // 1. Right-click inside minimap -> Move command to minimap point
        if (Input.GetMouseButtonDown(1) && IsPointerOverMinimap())
        {
            if (TryGetNormalized(Input.mousePosition, out Vector2 norm))
            {
                lastCommandFrame = Time.frameCount;
                CommandMoveToWorld(MinimapToWorld(norm));
            }
        }

        // 2. Left-click inside minimap -> Pan camera ground look-at to minimap point
        bool isUnitDragging = UnitSelectionBox.Instance != null && UnitSelectionBox.Instance.IsDragging;

        if (Input.GetMouseButtonDown(0))
        {
            if (!isUnitDragging && IsPointerOverMinimap())
            {
                isDraggingMinimapCam = true;
                if (TryGetNormalized(Input.mousePosition, out Vector2 norm))
                {
                    CenterCameraOnWorld(MinimapToWorld(norm));
                }
            }
        }
        else if (Input.GetMouseButton(0) && isDraggingMinimapCam)
        {
            if (TryGetNormalized(Input.mousePosition, out Vector2 norm))
            {
                CenterCameraOnWorld(MinimapToWorld(norm));
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            isDraggingMinimapCam = false;
        }
    }

    public void CenterCameraOnWorld(Vector3 worldPos)
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        float y = mainCamera.transform.position.y;
        if (y <= 0.01f) y = 25f;

        Vector3 fwd = mainCamera.transform.forward;
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
        mainCamera.transform.position = newPos;
    }

    private void ScanExisting()
    {
        if (UnitSelectionManager.Instance != null)
            foreach (var u in UnitSelectionManager.Instance.allUnitsList)
                if (u != null) TrackUnit(u);

        foreach (var b in FindObjectsOfType<Building>())
            if (b != null) TrackBuilding(b);

        foreach (var r in ResourceNode.AllNodes)
            if (r != null && !r.IsEmpty) TrackResource(r);
    }

    public void TrackUnit(Unit unit)
    {
        if (unit == null || dots.ContainsKey(unit.transform)) return;
        var dot = CreateDot(unitDotSize);
        dots[unit.transform] = dot;
        dotKinds[unit.transform] = new DotEntry
        {
            Dot = dot, Kind = DotKind.Unit, Unit = unit, OwnerId = unit.OwnerPlayerId
        };
        SetDotColor(dot, unit.OwnerPlayerId);
    }

    public void TrackBuilding(Building building)
    {
        if (building == null || dots.ContainsKey(building.transform)) return;
        var dot = CreateDot(buildingDotSize);
        dots[building.transform] = dot;
        dotKinds[building.transform] = new DotEntry
        {
            Dot = dot, Kind = DotKind.Building, Unit = null, OwnerId = building.OwnerPlayerId
        };
        SetDotColor(dot, building.OwnerPlayerId);
    }

    public void TrackResource(ResourceNode node)
    {
        if (node == null || dots.ContainsKey(node.transform) || node.IsEmpty) return;
        var dot = CreateDot(resourceDotSize);
        dots[node.transform] = dot;
        dotKinds[node.transform] = new DotEntry
        {
            Dot = dot, Kind = DotKind.Resource, Unit = null, OwnerId = -1
        };
        dot.GetComponent<UnityEngine.UI.Image>().color = Color.white;
    }

    public void Untrack(Transform t)
    {
        if (dots.TryGetValue(t, out var dot))
        {
            if (dot != null) Destroy(dot.gameObject);
            dots.Remove(t);
        }
        if (t != null) dotKinds.Remove(t);
    }

    private RectTransform CreateDot(float size)
    {
        GameObject go = new GameObject("Dot", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(minimapRect, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        go.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        return rt;
    }

    private void SetDotColor(RectTransform dot, int ownerId)
    {
        var img = dot.GetComponent<UnityEngine.UI.Image>();
        if (img == null) return;
        if (PlayerColorManager.Instance != null)
            img.color = PlayerColorManager.Instance.GetColor(ownerId);
        else
            img.color = ownerId == 0 ? Color.cyan : Color.red;
    }

    private Vector2 WorldToMinimapLocal(Vector3 worldPos, bool clamp = true)
    {
        float halfSize = MapBoundary.Instance != null ? MapBoundary.Instance.HalfSize : 500f;
        float pctX = (worldPos.x + halfSize) / (halfSize * 2f);
        float pctY = (worldPos.z + halfSize) / (halfSize * 2f);
        if (clamp)
        {
            pctX = Mathf.Clamp01(pctX);
            pctY = Mathf.Clamp01(pctY);
        }
        Rect rect = minimapRect.rect;
        return new Vector2(pctX * rect.width, pctY * rect.height);
    }

    private Vector3? MinimapLocalToWorld(Vector2 localPos)
    {
        if (minimapRect == null) return null;
        Rect rect = minimapRect.rect;
        if (rect.width <= 0f || rect.height <= 0f) return null;
        float normX = Mathf.Clamp01((localPos.x - rect.xMin) / rect.width);
        float normY = Mathf.Clamp01((localPos.y - rect.yMin) / rect.height);
        return MinimapToWorld(new Vector2(normX, normY));
    }

    private void LateUpdate()
    {
        if (minimapRect == null) return;

        staleDots.Clear();
        foreach (var kvp in dots)
        {
            var t = kvp.Key;
            var dot = kvp.Value;
            if (t == null || dot == null) { staleDots.Add(t); continue; }

            if (!t.gameObject.activeInHierarchy)
            {
                dot.gameObject.SetActive(false);
                continue;
            }

            if (dotKinds.TryGetValue(t, out DotEntry entry))
            {
                if (entry.Kind == DotKind.Resource)
                {
                    bool explored = FogOfWar.Instance == null ||
                                    FogOfWar.Instance.IsExplored(t.position);
                    if (!explored)
                    {
                        dot.gameObject.SetActive(false);
                        continue;
                    }
                }
                else if (entry.Kind == DotKind.Unit)
                {
                    Unit unit = entry.Unit;
                    int owner = unit != null ? unit.OwnerPlayerId : entry.OwnerId;
                    if (owner != PlayerColorManager.LocalPlayerIndex)
                    {
                        bool isAlly = NetworkedPlayer.LocalInstance != null
                            && NetworkedPlayer.SameTeam(PlayerColorManager.LocalPlayerIndex, owner);
                        if (!isAlly)
                        {
                            bool vis = FogOfWar.Instance == null ||
                                       FogOfWar.Instance.IsVisible(t.position);
                            if (!vis)
                            {
                                dot.gameObject.SetActive(false);
                                continue;
                            }
                        }
                    }
                }
            }

            dot.gameObject.SetActive(true);
            dot.anchoredPosition = WorldToMinimapLocal(t.position);
        }
        for (int i = 0; i < staleDots.Count; i++) Untrack(staleDots[i]);

        UpdateViewportRect();
    }

    private void CreateViewportOverlay()
    {
        GameObject cont = new GameObject("VP_Container", typeof(RectTransform), typeof(RectMask2D));
        cont.transform.SetParent(minimapRect, false);
        vpContainer           = cont.GetComponent<RectTransform>();
        vpContainer.anchorMin = Vector2.zero;
        vpContainer.anchorMax = Vector2.one;
        vpContainer.offsetMin = Vector2.zero;
        vpContainer.offsetMax = Vector2.zero;

        Color dotColor  = new Color(1f, 0.95f, 0.4f, 1f);
        Color lineColor = new Color(1f, 0.95f, 0.4f, 0.8f);

        for (int i = 0; i < 4; i++)
        {
            GameObject dGO = new GameObject($"VP_Corner{i}", typeof(RectTransform), typeof(Image));
            dGO.transform.SetParent(vpContainer, false);
            vpCornerDots[i]           = dGO.GetComponent<RectTransform>();
            vpCornerDots[i].anchorMin = Vector2.zero;
            vpCornerDots[i].anchorMax = Vector2.zero;
            vpCornerDots[i].pivot     = new Vector2(0.5f, 0.5f);
            vpCornerDots[i].sizeDelta = new Vector2(3f, 3f);
            var dImg           = dGO.GetComponent<Image>();
            dImg.color         = dotColor;
            dImg.raycastTarget = false;

            GameObject lGO = new GameObject($"VP_Line{i}", typeof(RectTransform), typeof(Image));
            lGO.transform.SetParent(vpContainer, false);
            vpLines[i]           = lGO.GetComponent<RectTransform>();
            vpLines[i].anchorMin = Vector2.zero;
            vpLines[i].anchorMax = Vector2.zero;
            vpLines[i].pivot     = new Vector2(0.5f, 0.5f);
            var lImg           = lGO.GetComponent<Image>();
            lImg.color         = lineColor;
            lImg.raycastTarget = false;
        }
    }

    private void UpdateViewportRect()
    {
        if (mainCamera == null || vpContainer == null) return;

        Transform camT = mainCamera.transform;
        if (_lastViewportCamPos.HasValue && _lastViewportCamPos == camT.position &&
            _lastViewportCamRot == camT.eulerAngles && _lastViewportAspect == mainCamera.aspect)
            return;

        _lastViewportCamPos    = camT.position;
        _lastViewportCamRot    = camT.eulerAngles;
        _lastViewportAspect    = mainCamera.aspect;

        float halfSize = MapBoundary.Instance != null ? MapBoundary.Instance.HalfSize : 500f;

        for (int i = 0; i < 4; i++)
        {
            Ray ray = mainCamera.ViewportPointToRay(viewportCorners[i]);
            Vector3 worldPos;

            if (Physics.Raycast(ray, out RaycastHit terrainHit, halfSize * 10f,
                    groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                worldPos = terrainHit.point;
            }
            else if (ray.direction.y < -0.0001f)
            {
                float t = -ray.origin.y / ray.direction.y;
                worldPos = ray.origin + ray.direction * t;
            }
            else
            {
                Vector3 flat = new Vector3(ray.direction.x, 0f, ray.direction.z).normalized;
                worldPos = ray.origin + flat * halfSize * 4f;
            }

            vpMiniPos[i] = WorldToMinimapLocal(worldPos, false);
        }

        for (int i = 0; i < 4; i++)
            vpCornerDots[i].anchoredPosition = vpMiniPos[i];

        for (int i = 0; i < 4; i++)
        {
            Vector2 a      = vpMiniPos[i];
            Vector2 b      = vpMiniPos[(i + 1) % 4];
            Vector2 dir    = b - a;
            float   length = dir.magnitude;
            float   angle  = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            vpLines[i].anchoredPosition = (a + b) * 0.5f;
            vpLines[i].sizeDelta        = new Vector2(length, 1.5f);
            vpLines[i].localEulerAngles = new Vector3(0, 0, angle);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (TryGetNormalized(eventData.position, out Vector2 norm))
            {
                CenterCameraOnWorld(MinimapToWorld(norm));
            }
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (Time.frameCount != lastCommandFrame)
            {
                if (TryGetNormalized(eventData.position, out Vector2 norm))
                {
                    lastCommandFrame = Time.frameCount;
                    CommandMoveToWorld(MinimapToWorld(norm));
                }
            }
        }
    }

    public void CommandMoveToWorld(Vector3 worldPos)
    {
        var selected = SelectionManager.Instance != null ? SelectionManager.Instance.GetSelectedUnits() : null;
        if (selected == null || selected.Count == 0) return;

        // Sample ground height
        Vector3 target = worldPos;
        if (Physics.Raycast(new Vector3(worldPos.x, 200f, worldPos.z), Vector3.down, out RaycastHit gHit, 400f, groundLayerMask, QueryTriggerInteraction.Ignore))
        {
            target.y = gHit.point.y;
        }
        else if (UnityEngine.AI.NavMesh.SamplePosition(target, out UnityEngine.AI.NavMeshHit navHit, 20f, UnityEngine.AI.NavMesh.AllAreas))
        {
            target.y = navHit.position.y;
        }

        if (MapBoundary.Instance != null)
            target = MapBoundary.Instance.Clamp(target);

        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (NetworkedPlayer.LocalInstance != null)
        {
            if (SelectionManager.Instance != null)
            {
                SelectionManager.Instance.CalculateFormationPositions(target, out uint[] formIds, out Vector3[] positions);
                if (formIds.Length > 0)
                {
                    NetworkedPlayer.LocalInstance.CmdMoveUnitsFormation(formIds, positions, shift);
                }
                else
                {
                    var ids = new List<uint>();
                    for (int i = 0; i < selected.Count; i++)
                        if (selected[i] != null && selected[i].OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                            ids.Add(selected[i].netId);
                    if (ids.Count > 0)
                        NetworkedPlayer.LocalInstance.CmdMoveUnits(ids.ToArray(), target);
                }
            }
            else
            {
                var ids = new List<uint>();
                for (int i = 0; i < selected.Count; i++)
                    if (selected[i] != null && selected[i].OwnerPlayerId == PlayerColorManager.LocalPlayerIndex)
                        ids.Add(selected[i].netId);
                if (ids.Count > 0)
                    NetworkedPlayer.LocalInstance.CmdMoveUnits(ids.ToArray(), target);
            }

            if (shift)
            {
                MoveFlag.Instance?.ShowFlag(target, true);
            }
            else
            {
                MoveFlag.Instance?.ClearAllFlags();
                MoveFlag.Instance?.ShowFlag(target, false);
            }
        }
        else
        {
            if (shift)
            {
                SelectionManager.Instance?.AddFormationWaypoints(target);
                MoveFlag.Instance?.ShowFlag(target, true);
            }
            else
            {
                foreach (Unit u in selected)
                    if (u != null) u.ClearWaypoints();

                MoveFlag.Instance?.ClearAllFlags();
                MoveFlag.Instance?.ShowFlag(target, false);
                SelectionManager.Instance?.AssignFormationWaypoints(target);
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Handled in Update / OnPointerDown
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
