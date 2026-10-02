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

    // What each dot represents, resolved once at Track time.
    // LateUpdate used to call GetComponent<ResourceNode>() + GetComponent<Unit>()
    // on EVERY tracked dot EVERY frame (2 lookups x ~300 dots x 60fps = 36k
    // component lookups per second). The component reference is now cached
    // alongside the dot, so the hot loop does zero lookups.
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

    // Viewport-rect dirty tracking: the rect is a pure function of the main camera
    // frustum, so it only needs recomputing when that camera actually changes.
    private Vector3? _lastViewportCamPos;
    private Vector3 _lastViewportCamRot;
    private float   _lastViewportAspect;

    private RectTransform   vpContainer;

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
            ((Image)graphic).color = new Color(0, 0, 0, 0);
        }
        graphic.raycastTarget = true;
    }

    private void Start()
    {
        ScanExisting();
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
        float halfSize = MapBoundary.Instance != null ? MapBoundary.Instance.HalfSize : 500f;
        Rect rect = minimapRect.rect;
        float pctX = localPos.x / rect.width;
        float pctY = localPos.y / rect.height;
        if (pctX < 0 || pctX > 1 || pctY < 0 || pctY > 1) return null;
        return new Vector3((pctX * halfSize * 2f) - halfSize, 0f, (pctY * halfSize * 2f) - halfSize);
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

            // Zero GetComponent calls here: kind + owner were resolved at Track time.
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

    // AFTER
    private void CreateViewportOverlay()
    {
        // RectMask2D container — clips dots and lines to minimap bounds automatically
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
            // Corner dot
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

            // Line from corner i to corner (i+1)%4
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

        // The viewport rectangle is derived purely from the MAIN camera frustum, so it
        // can only change when that camera moves. This used to run 4 physics raycasts
        // EVERY frame, including the vast majority of frames where the camera is parked
        // and the result is bit-for-bit identical. Skip the work until it can differ.
        Transform camT = mainCamera.transform;
        if (_lastViewportCamPos.HasValue && _lastViewportCamPos == camT.position &&
            _lastViewportCamRot == camT.eulerAngles && _lastViewportAspect == mainCamera.aspect)
            return;

        _lastViewportCamPos    = camT.position;
        _lastViewportCamRot    = camT.eulerAngles;
        _lastViewportAspect    = mainCamera.aspect;

        float halfSize = MapBoundary.Instance != null ? MapBoundary.Instance.HalfSize : 500f;

        // REPLACE this block (leave everything else in UpdateViewportRect unchanged)
        // Uses the cached static `viewportCorners` buffer - this method allocated a
        // fresh Vector3[4] every frame.
        for (int i = 0; i < 4; i++)
        {
            Ray ray = mainCamera.ViewportPointToRay(viewportCorners[i]);
            Vector3 worldPos;

            // Try to hit actual terrain / ground mesh first
            if (Physics.Raycast(ray, out RaycastHit terrainHit, halfSize * 10f,
                    groundLayerMask, QueryTriggerInteraction.Ignore))
            {
                worldPos = terrainHit.point;
            }
            else if (ray.direction.y < -0.0001f)
            {
                // Fallback: flat y=0 plane
                float t = -ray.origin.y / ray.direction.y;
                worldPos = ray.origin + ray.direction * t;
            }
            else
            {
                // Ray pointing upward — project to far edge
                Vector3 flat = new Vector3(ray.direction.x, 0f, ray.direction.z).normalized;
                worldPos = ray.origin + flat * halfSize * 4f;
            }

            // No clamping — the RectMask2D container clips anything outside
            vpMiniPos[i] = WorldToMinimapLocal(worldPos, false);
        }

        // Place the 4 corner dots
        for (int i = 0; i < 4; i++)
            vpCornerDots[i].anchoredPosition = vpMiniPos[i];

        // Draw lines connecting adjacent corners
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
        Vector2 local;
        Camera eventCamera = eventData.pressEventCamera;
        // Fallback: if overlay canvas, pressEventCamera is null — use null which ScreenPointToLocal handles
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(minimapRect, eventData.position, eventCamera, out local))
            return;

        Rect rect = minimapRect.rect;
        if (local.x < 0 || local.x > rect.width || local.y < 0 || local.y > rect.height)
            return;

        Vector3? world = MinimapLocalToWorld(local);
        if (!world.HasValue) return;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            Vector3 target = world.Value;
            target.y = mainCamera != null ? mainCamera.transform.position.y : 30f;
            if (MapBoundary.Instance != null)
            {
                float limit = MapBoundary.Instance.CameraLimit;
                target.x = Mathf.Clamp(target.x, -limit, limit);
                target.z = Mathf.Clamp(target.z, -limit, limit);
            }

            RTSCamera.SkipNextFrame = true;
            if (mainCamera != null)
                mainCamera.transform.position = target;
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            var selected = SelectionManager.Instance?.SelectedUnits;
            if (selected == null || selected.Count == 0) return;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (shift)
                SelectionManager.Instance?.AddFormationWaypoints(world.Value);
            else
            {
                foreach (Unit u in selected)
                    if (u != null) u.ClearWaypoints();

                MoveFlag.Instance?.ClearAllFlags();
                MoveFlag.Instance?.ShowFlag(world.Value, false);
                SelectionManager.Instance?.AssignFormationWaypoints(world.Value);
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Right-click handled in OnPointerDown — stub required for IPointerClickHandler interface
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
