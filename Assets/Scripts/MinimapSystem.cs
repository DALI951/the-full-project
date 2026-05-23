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
        SetDotColor(dot, unit.OwnerPlayerId);
    }

    public void TrackBuilding(Building building)
    {
        if (building == null || dots.ContainsKey(building.transform)) return;
        var dot = CreateDot(buildingDotSize);
        dots[building.transform] = dot;
        SetDotColor(dot, building.OwnerPlayerId);
    }

    public void TrackResource(ResourceNode node)
    {
        if (node == null || dots.ContainsKey(node.transform) || node.IsEmpty) return;
        var dot = CreateDot(resourceDotSize);
        dots[node.transform] = dot;
        dot.GetComponent<UnityEngine.UI.Image>().color = Color.white;
    }

    public void Untrack(Transform t)
    {
        if (dots.TryGetValue(t, out var dot))
        {
            if (dot != null) Destroy(dot.gameObject);
            dots.Remove(t);
        }
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

        List<Transform> toRemove = new List<Transform>();
        foreach (var kvp in dots)
        {
            var t = kvp.Key;
            var dot = kvp.Value;
            if (t == null || dot == null) { toRemove.Add(t); continue; }

            if (!t.gameObject.activeInHierarchy) 
            { 
                dot.gameObject.SetActive(false); 
                continue; 
            }

            ResourceNode res = t.GetComponent<ResourceNode>();
            if (res != null)
            {
                bool explored = FogOfWar.Instance == null || 
                                FogOfWar.Instance.IsExplored(t.position);
                if (!explored)
                {
                    dot.gameObject.SetActive(false);
                    continue;
                }
            }
            Unit unitDot = t.GetComponent<Unit>();
            if (unitDot != null && unitDot.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex)
            {
                bool isAlly = NetworkedPlayer.LocalInstance != null
                    && NetworkedPlayer.SameTeam(PlayerColorManager.LocalPlayerIndex, unitDot.OwnerPlayerId);
                if (!isAlly)
                {
                    bool vis = FogOfWar.Instance == null || FogOfWar.Instance.IsVisible(t.position);
                    if (!vis)
                    {
                        dot.gameObject.SetActive(false);
                        continue;
                    }
                }
            }

            dot.gameObject.SetActive(true);
            dot.anchoredPosition = WorldToMinimapLocal(t.position);
        }
        foreach (var t in toRemove) Untrack(t);

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

        float halfSize = MapBoundary.Instance != null ? MapBoundary.Instance.HalfSize : 500f;

        Vector3[] vpPoints =
        {
            new Vector3(0, 0, 0),  // bottom-left
            new Vector3(1, 0, 0),  // bottom-right
            new Vector3(1, 1, 0),  // top-right
            new Vector3(0, 1, 0),  // top-left
        };

        // REPLACE this block (leave everything else in UpdateViewportRect unchanged)
        for (int i = 0; i < 4; i++)
        {
            Ray     ray      = mainCamera.ViewportPointToRay(vpPoints[i]);
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
        // Duplicate the right-click logic here as fallback
        if (eventData.button != PointerEventData.InputButton.Right) return;
        
        Vector2 local;
        Camera eventCamera = eventData.pressEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(minimapRect, eventData.position, eventCamera, out local))
            return;

        Rect rect = minimapRect.rect;
        if (local.x < 0 || local.x > rect.width || local.y < 0 || local.y > rect.height)
            return;

        Vector3? world = MinimapLocalToWorld(local);
        if (!world.HasValue) return;

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
        
        // CRITICAL: Stop the click from propagating to 3D raycasts
        eventData.Use();
    }
}