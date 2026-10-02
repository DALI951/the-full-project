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

    [Header("Base Placement")]
    [Tooltip("How far INSIDE the map edge each spawn point sits. Bases used to sit " +
             "exactly on the boundary, which clipped half of every starter ring of " +
             "resources and starved bases on the edge side. Keep this at least as " +
             "large as ResourceSpawner.startMaxRadius.")]
    [SerializeField] private float   spawnInset = 36f;

    [Tooltip("If the spawn ring radius would fall outside the map, clamp it to 45% of the half size.")]
    [SerializeField, Range(0.1f, 0.95f)] private float minRadiusFraction = 0.45f;

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
    /// Distributes N points evenly by ANGLE on a circle that is inset from the map
    /// edge. Even-by-angle matters: the old perimeter walk put "opposite" players
    /// ~159 units apart on the SAME edge instead of the far corner, and edge bases
    /// lost half their starter ring to clamping.
    /// </summary>
    private void GenerateSpawnPoints()
    {
        int n = Mathf.Max(1, spawnPointCount);
        spawnPositions = new Vector3[n];

        float radius = Mathf.Max(areaHalfSize - Mathf.Max(0f, spawnInset),
                                 areaHalfSize * minRadiusFraction);

        for (int i = 0; i < n; i++)
        {
            // Offset by half a step so no base sits exactly on the +X axis.
            float a = ((i + 0.5f) / n) * Mathf.PI * 2f;
            spawnPositions[i] = areaCenter + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
        }
    }

    /// <summary>
    /// DEAD CODE kept out of the build path: the ring generator above replaced the
    /// old square-perimeter walk. Retained only as documentation of the old layout.
    private static Vector3 PerimeterPoint_UNUSED(float dist, float half)
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

        // Draw spawn points (same ring GenerateSpawnPoints uses)
        int n = Mathf.Max(1, spawnPointCount);
        float radius = Mathf.Max(h - Mathf.Max(0f, spawnInset), h * minRadiusFraction);
        for (int i = 0; i < n; i++)
        {
            float a = ((i + 0.5f) / n) * Mathf.PI * 2f;
            Vector3 pt = c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.color = (markerColors != null && markerColors.Length > 0)
                ? markerColors[i % markerColors.Length] : Color.white;
            Gizmos.DrawSphere(pt, 0.8f);
        }
    }
#endif
}
