using UnityEngine;

/// <summary>
/// SpawnAreaManager — draws a square play area and places 10 evenly-spaced
/// spawn points around its perimeter (visible as small coloured markers).
///
/// Attach to any empty GameObject. The spawn points are created automatically
/// at runtime; you can also see them in the Scene view via OnDrawGizmos.
/// </summary>
public class SpawnAreaManager : MonoBehaviour
{
    // ─── Inspector ───────────────────────────────────────────────────────
    [Header("Area Settings")]
    [Tooltip("Centre of the play area in world space.")]
    [SerializeField] private Vector3 areaCenter   = Vector3.zero;

    private float   areaHalfSize = 23f;

    [Tooltip("Number of spawn points distributed around the perimeter.")]
    [SerializeField] private int     spawnPointCount = 10;

    [Header("Marker Visuals")]
    [Tooltip("Prefab to instantiate at each spawn point (optional — can be empty).")]
    [SerializeField] private GameObject spawnMarkerPrefab;

    [Tooltip("Colors assigned to each spawn point (cycles if fewer than spawnPointCount).")]
    [SerializeField] private Color[] markerColors = new Color[]
    {
        Color.cyan, Color.red, new Color(1f,0.5f,0f), Color.green,
        Color.magenta, Color.yellow, new Color(0.5f,0f,1f), Color.white,
        new Color(0f,1f,0.5f), new Color(1f,0f,0.5f)
    };

    // ─── Runtime State ───────────────────────────────────────────────────
    private Vector3[] spawnPositions;  // filled in Awake

    // ─── Unity Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        if (MapBoundary.Instance != null)
            areaHalfSize = MapBoundary.Instance.HalfSize;

        GenerateSpawnPoints();
        PlaceMarkers();
    }

    // ─── Spawn Point Generation ───────────────────────────────────────────

    /// <summary>
    /// Distributes N points evenly along the perimeter of the square.
    /// Perimeter order: bottom edge → right edge → top edge → left edge.
    /// </summary>
    private void GenerateSpawnPoints()
    {
        spawnPositions = new Vector3[spawnPointCount];
        float perimeter = areaHalfSize * 8f; // 4 sides × 2*halfSize each
        float step      = perimeter / spawnPointCount;

        for (int i = 0; i < spawnPointCount; i++)
        {
            float dist = i * step;
            spawnPositions[i] = areaCenter + PerimeterPoint(dist, areaHalfSize);
        }
    }

    /// <summary>
    /// Given a distance along the square perimeter (starting at bottom-left,
    /// going clockwise), returns the local offset from the centre.
    /// </summary>
    private static Vector3 PerimeterPoint(float dist, float half)
    {
        float side = half * 2f;

        // Bottom edge (left → right)
        if (dist < side)
            return new Vector3(-half + dist, 0f, -half);
        dist -= side;

        // Right edge (bottom → top)
        if (dist < side)
            return new Vector3(half, 0f, -half + dist);
        dist -= side;

        // Top edge (right → left)
        if (dist < side)
            return new Vector3(half - dist, 0f, half);
        dist -= side;

        // Left edge (top → bottom)
        return new Vector3(-half, 0f, half - dist);
    }

    private void PlaceMarkers()
    {
        for (int i = 0; i < spawnPositions.Length; i++)
        {
            if (spawnMarkerPrefab != null)
            {
                GameObject marker = Instantiate(spawnMarkerPrefab, spawnPositions[i], Quaternion.identity, transform);
                marker.name = $"SpawnPoint_{i}";

                // Color the marker
                Renderer r = marker.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    Color col = markerColors[i % markerColors.Length];
                    r.material.color = col;
                }
            }
        }
    }

    // ─── Public API ──────────────────────────────────────────────────────

    /// <summary>Returns the spawn position for a given index.</summary>
    public Vector3 GetSpawnPosition(int index)
    {
        if (spawnPositions == null || spawnPositions.Length == 0) return areaCenter;
        return spawnPositions[index % spawnPositions.Length];
    }

    public int GetSpawnCount()
    {
        return spawnPositions != null ? spawnPositions.Length : 0;
    }

    // ─── Editor Gizmos ───────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Draw the boundary square
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Vector3 c = areaCenter;
        float   h = areaHalfSize;

        Vector3[] corners =
        {
            c + new Vector3(-h, 0,  h),
            c + new Vector3( h, 0,  h),
            c + new Vector3( h, 0, -h),
            c + new Vector3(-h, 0, -h),
        };

        for (int i = 0; i < 4; i++)
            Gizmos.DrawLine(corners[i], corners[(i + 1) % 4]);

        // Draw spawn points
        float perim = h * 8f;
        float step  = perim / spawnPointCount;
        for (int i = 0; i < spawnPointCount; i++)
        {
            Vector3 pt = areaCenter + PerimeterPoint(i * step, h);
            Gizmos.color = (markerColors != null && markerColors.Length > 0)
                ? markerColors[i % markerColors.Length] : Color.white;
            Gizmos.DrawSphere(pt, 0.8f);
        }
    }
#endif
}
