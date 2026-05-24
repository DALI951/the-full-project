using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public class MinimapCamera : MonoBehaviour
{
    [Header("Map Size")]
    [SerializeField] private float padding = 4f;
    private float mapHalfSize = 500f;

    [Header("Height")]
    [SerializeField] private float height = 300f;

    [Header("Target Display")]
    [SerializeField] private RawImage minimapDisplay;

    private Camera cam;
    private RenderTexture rt;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        if (MapBoundary.Instance != null)
            mapHalfSize = MapBoundary.Instance.HalfSize;

        // Position and size the camera
        cam.orthographic = true;
        cam.orthographicSize = mapHalfSize + padding;
        transform.position = new Vector3(0f, height, 0f);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // Create render texture
        int res = 512;
        rt = new RenderTexture(res, res, 16, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;
        rt.Create();
        cam.targetTexture = rt;

        // Send to UI
        if (minimapDisplay != null)
            minimapDisplay.texture = rt;
        else
            Debug.LogError("[MinimapCamera] 'Minimap Display' is not assigned! Drag MinimapDisplay RawImage here.");
    }

    private void LateUpdate()
    {
        // Lock position so nothing can move it
        transform.position = new Vector3(0f, height, 0f);
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void OnDestroy()
    {
        if (rt != null)
        {
            cam.targetTexture = null;
            rt.Release();
            Destroy(rt);
        }
    }
}