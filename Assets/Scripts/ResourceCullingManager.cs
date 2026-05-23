using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ResourceCullingManager v10 — Clean rewrite. Integrates FogOfWar.
/// Resources hidden if: (1) beyond cull distance OR (2) in unexplored fog.
/// Never deactivates GameObjects, only toggles Renderer.enabled.
/// </summary>
public class ResourceCullingManager : MonoBehaviour
{
    public static ResourceCullingManager Instance { get; private set; }

    [Header("Camera (REQUIRED — drag Main Camera here)")]
    [SerializeField] private Camera mainCamera;

    [Header("Debug")]
    [SerializeField] private bool logDebugInfo = true;

    private readonly List<ResourceNode> resources = new List<ResourceNode>();
    private float sqrCullDistance;
    private Transform camTransform;
    private bool initialized = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        FindCamera();

        float mapHalfSize = MapBoundary.Instance != null ? MapBoundary.Instance.HalfSize : 500f;
        float dist = Mathf.Max(mapHalfSize * 2.0f, 150f);
        sqrCullDistance = dist * dist;

        if (logDebugInfo)
            Debug.Log($"[CullingManager] Map half-size: {mapHalfSize:F0} | Cull distance: {dist:F0}");
    }

    private void FindCamera()
    {
        if (mainCamera != null && IsValidMainCamera(mainCamera))
        {
            camTransform = mainCamera.transform;
            initialized = true;
            if (logDebugInfo) Debug.Log($"[CullingManager] Camera: {camTransform.name}");
            return;
        }
        else if (mainCamera != null)
        {
            Debug.LogWarning($"[CullingManager] '{mainCamera.name}' is a minimap! Searching...");
        }

        if (Camera.main != null && IsValidMainCamera(Camera.main))
        {
            mainCamera = Camera.main;
            camTransform = mainCamera.transform;
            initialized = true;
            if (logDebugInfo) Debug.Log($"[CullingManager] Camera: {camTransform.name}");
            return;
        }

        RTSCamera rts = FindObjectOfType<RTSCamera>();
        if (rts != null && rts.TryGetComponent(out Camera cam) && IsValidMainCamera(cam))
        {
            mainCamera = cam;
            camTransform = cam.transform;
            initialized = true;
            if (logDebugInfo) Debug.Log($"[CullingManager] Camera: {camTransform.name}");
            return;
        }

        Camera[] allCams = FindObjectsOfType<Camera>();
        foreach (Camera c in allCams)
        {
            string lower = c.name.ToLower();
            if (lower.Contains("mini") || lower.Contains("portrait") || lower.Contains("ui")) continue;
            if (lower.Contains("rts") || lower.Contains("main") || lower.Contains("game"))
            {
                mainCamera = c;
                camTransform = c.transform;
                initialized = true;
                if (logDebugInfo) Debug.Log($"[CullingManager] Camera: {camTransform.name}");
                return;
            }
        }

        foreach (Camera c in allCams)
        {
            if (c.name.ToLower().Contains("mini")) continue;
            if (c.targetTexture != null) continue;
            mainCamera = c;
            camTransform = c.transform;
            initialized = true;
            Debug.LogWarning($"[CullingManager] Fallback: {c.name}");
            return;
        }

        Debug.LogError("[CullingManager] NO CAMERA! Resources visible (failsafe).");
        initialized = false;
    }

    private bool IsValidMainCamera(Camera cam)
    {
        if (cam == null) return false;
        string name = cam.name.ToLower();
        if (name.Contains("mini")) return false;
        if (name.Contains("portrait")) return false;
        if (name.Contains("ui")) return false;
        if (cam.targetTexture != null) return false;
        return true;
    }

    private void LateUpdate()
    {
        if (!initialized || camTransform == null)
        {
            foreach (var node in resources)
            {
                if (node == null) continue;
                SetRenderers(node, true);
            }
            return;
        }

        Vector3 camPos = camTransform.position;

        for (int i = resources.Count - 1; i >= 0; i--)
        {
            ResourceNode node = resources[i];
            if (node == null)
            {
                resources.RemoveAt(i);
                continue;
            }

            float sqrDist = (node.transform.position - camPos).sqrMagnitude;
            bool withinRange = sqrDist <= sqrCullDistance;

            bool isExplored = true;
            if (FogOfWar.Instance != null)
                isExplored = FogOfWar.Instance.IsExplored(node.transform.position);

            bool inFrustum = true;
            if (mainCamera != null)
            {
                Vector3 vp = mainCamera.WorldToViewportPoint(node.transform.position);
                inFrustum = vp.z > 0f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.15f && vp.y < 1.15f;
            }
            bool shouldShow = withinRange && isExplored && inFrustum;
            SetRenderers(node, shouldShow);
        }
    }

    private void SetRenderers(ResourceNode node, bool visible)
    {
        if (node.VisualRoot != null && node.VisualRoot != node.gameObject)
        {
            var renderers = node.VisualRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                if (r.enabled != visible) r.enabled = visible;
        }
        else
        {
            var renderers = node.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                if (r.enabled != visible) r.enabled = visible;
        }
    }

    public void ForceShowAll()
    {
        foreach (var node in resources)
        {
            if (node == null) continue;
            SetRenderers(node, true);
        }
    }

    public void Register(ResourceNode node)
    {
        if (node != null && !resources.Contains(node))
            resources.Add(node);
    }

    public void Unregister(ResourceNode node)
    {
        resources.Remove(node);
    }
}