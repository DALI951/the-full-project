using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CullingManager — Clean, solid culling architecture for MinAge.
/// Ensures objects never fade, alter material alpha, or use semi-transparent rendering.
/// Visibility is handled strictly by toggling Renderer.enabled.
/// Objects inside the explored/revealed area have a generous cull distance
/// so they never pop, flicker, or get cut off.
/// </summary>
public class CullingManager : MonoBehaviour
{
    public static CullingManager Instance { get; private set; }

    [Header("Camera")]
    [SerializeField] private Camera mainCamera;

    [Header("Visibility Range")]
    [Tooltip("Generous cull distance ensuring no popping in revealed territory.")]
    [SerializeField] private float cullDistance = 350f;

    [Header("Debug")]
    [SerializeField] private bool logDebugInfo = false;

    private readonly List<Renderer> managedRenderers = new List<Renderer>();
    private float sqrCullDistance;
    private Transform camTransform;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        float mapHalfSize = MapBoundary.Instance != null ? MapBoundary.Instance.HalfSize : 500f;
        cullDistance = Mathf.Max(mapHalfSize * 2.5f, 350f);
        sqrCullDistance = cullDistance * cullDistance;
    }

    private void Start()
    {
        FindMainCamera();
    }

    private void FindMainCamera()
    {
        if (mainCamera != null && IsValidGameCamera(mainCamera))
        {
            camTransform = mainCamera.transform;
            return;
        }

        if (Camera.main != null && IsValidGameCamera(Camera.main))
        {
            mainCamera = Camera.main;
            camTransform = mainCamera.transform;
            return;
        }

        RTSCamera rts = FindObjectOfType<RTSCamera>();
        if (rts != null && rts.TryGetComponent(out Camera cam) && IsValidGameCamera(cam))
        {
            mainCamera = cam;
            camTransform = cam.transform;
            return;
        }

        Camera[] cams = FindObjectsOfType<Camera>();
        foreach (Camera c in cams)
        {
            if (IsValidGameCamera(c))
            {
                mainCamera = c;
                camTransform = c.transform;
                return;
            }
        }
    }

    private bool IsValidGameCamera(Camera cam)
    {
        if (cam == null) return false;
        string n = cam.name.ToLower();
        if (n.Contains("mini") || n.Contains("portrait") || n.Contains("ui")) return false;
        if (cam.targetTexture != null) return false;
        return true;
    }

    public void RegisterRenderer(Renderer r)
    {
        if (r != null && !managedRenderers.Contains(r))
            managedRenderers.Add(r);
    }

    public void UnregisterRenderer(Renderer r)
    {
        managedRenderers.Remove(r);
    }

    private void LateUpdate()
    {
        if (camTransform == null)
        {
            FindMainCamera();
            if (camTransform == null) return;
        }

        Vector3 camPos = camTransform.position;

        for (int i = managedRenderers.Count - 1; i >= 0; i--)
        {
            Renderer r = managedRenderers[i];
            if (r == null)
            {
                managedRenderers.RemoveAt(i);
                continue;
            }

            Vector3 pos = r.transform.position;
            float sqrDist = (pos - camPos).sqrMagnitude;
            bool withinRange = sqrDist <= sqrCullDistance;

            bool isExplored = true;
            if (FogOfWar.Instance != null)
                isExplored = FogOfWar.Instance.IsExplored(pos);

            bool inFrustum = true;
            if (mainCamera != null)
            {
                Vector3 vp = mainCamera.WorldToViewportPoint(pos);
                inFrustum = vp.z > 0f && vp.x > -0.2f && vp.x < 1.2f && vp.y > -0.2f && vp.y < 1.2f;
            }

            bool visible = withinRange && isExplored && inFrustum;
            if (r.enabled != visible)
                r.enabled = visible;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
