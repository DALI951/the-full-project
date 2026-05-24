using UnityEngine;

/// <summary>
/// RTSCamera — WASD / edge-pan + scroll zoom.
/// Feature 3: camera position is clamped to MapBoundary every frame.
/// </summary>
public class RTSCamera : MonoBehaviour
{
    public static bool SkipNextFrame { get; set; } = false;
    [Header("Pan")]
    [SerializeField] private float panSpeed        = 20f;
    [SerializeField] private float edgePanThreshold = 10f;
    [SerializeField] private bool  useEdgePan      = true;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 5f;
    [SerializeField] private float minHeight = 5f;
    [SerializeField] private float maxHeight = 50f;

    [Header("Map Clamp")]
    [Tooltip("Extra camera movement beyond map bounds so screen edges can still reach the full map.")]
    [SerializeField] private float edgeAccessPadding = 8f;
    private void Start()
    {
        SetCameraPosition(PlayerColorManager.LocalPlayerIndex);
    }

    public void SetCameraPosition(int playerIndex)
    {
        Vector3 focus = Vector3.zero;
        SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
        if (area != null)
        {
            focus = area.GetSpawnPosition(playerIndex);
            Vector3 toCenter = Vector3.zero - focus;
            if (toCenter.sqrMagnitude > 0.001f)
                focus += toCenter.normalized * 10f;
        }

        float y = transform.position.y;
        if (y <= 0.01f) y = 30f;
        transform.position = new Vector3(focus.x, y, focus.z);
        ClampToMap();
    }
    private void Update()
    {
        if (!SkipNextFrame)
        {
            HandlePan();
            HandleZoom();
        }
        SkipNextFrame = false;
        
        ClampToMap();
    }
    private void HandlePan()
    {
        Vector3 move = Vector3.zero;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    move += Vector3.forward;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  move += Vector3.back;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  move += Vector3.left;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += Vector3.right;

        if (useEdgePan)
        {
            Vector2 m = Input.mousePosition;
            if (m.x < edgePanThreshold)                 move += Vector3.left;
            if (m.x > Screen.width  - edgePanThreshold) move += Vector3.right;
            if (m.y < edgePanThreshold)                 move += Vector3.back;
            if (m.y > Screen.height - edgePanThreshold) move += Vector3.forward;
        }

        transform.Translate(move.normalized * panSpeed * Time.deltaTime, Space.World);
    }

    private void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Approximately(scroll, 0f)) return;

        Vector3 pos = transform.position;
        pos.y -= scroll * zoomSpeed * 10f;
        pos.y = Mathf.Clamp(pos.y, minHeight, maxHeight);
        transform.position = pos;
    }

    private void ClampToMap()
    {
        if (MapBoundary.Instance == null) return;

        float limit = MapBoundary.Instance.CameraLimit + Mathf.Max(0f, edgeAccessPadding);
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, -limit, limit);
        pos.z = Mathf.Clamp(pos.z, -limit, limit);
        transform.position = pos;
    }
}
