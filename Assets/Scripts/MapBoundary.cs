using UnityEngine;

/// <summary>
/// MapBoundary — single source of truth for the playable area.
/// Must match SpawnAreaManager.areaHalfSize.
///
/// Attach to an empty GameObject called "MapBoundary".
/// Other systems call MapBoundary.Instance.Clamp(pos) to keep things inside.
///
/// Fixes:
///   Bug 6 (animals leave map) — AnimalNode calls Clamp every frame.
///   Feature 3 (camera + unit boundaries).
/// </summary>
[DefaultExecutionOrder(-100)]
public class MapBoundary : MonoBehaviour
{
    public static MapBoundary Instance { get; private set; }

    [Tooltip("Half-size of the square play area. Match SpawnAreaManager.")]
    [SerializeField] private float halfSize = 23f;

    [Tooltip("Extra inner margin so units/animals never touch the exact edge.")]
    [SerializeField] private float margin = 1f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Clamps a world XZ position to within the map.</summary>
    public Vector3 Clamp(Vector3 pos)
    {
        float limit = halfSize - margin;
        pos.x = Mathf.Clamp(pos.x, -limit, limit);
        pos.z = Mathf.Clamp(pos.z, -limit, limit);
        return pos;
    }

    /// <summary>Returns true if a world position is inside the map.</summary>
    public bool IsInside(Vector3 pos)
    {
        float limit = halfSize - margin;
        return Mathf.Abs(pos.x) <= limit && Mathf.Abs(pos.z) <= limit;
    }

    /// <summary>The camera pan limit (slightly larger than unit limit).</summary>
    public float CameraLimit => halfSize;
    public float HalfSize    => halfSize;

    // ── Editor gizmo ─────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        float h = halfSize;
        Vector3[] corners =
        {
            new Vector3(-h, 0,  h), new Vector3( h, 0,  h),
            new Vector3( h, 0, -h), new Vector3(-h, 0, -h)
        };
        for (int i = 0; i < 4; i++)
            Gizmos.DrawLine(corners[i], corners[(i+1)%4]);
    }
#endif
}
