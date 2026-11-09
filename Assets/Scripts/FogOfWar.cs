using UnityEngine;
using Mirror;
using System.Collections.Generic;

[DefaultExecutionOrder(-100)]
public class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance { get; private set; }

    [Header("World-Space Rendering")]
    [SerializeField] private MeshRenderer fogPlaneRenderer;
    [SerializeField] private float fogHeight = 0.06f;
    private float mapHalfSize = 23f;

    [Header("Resolution")]
    [SerializeField] private int textureResolution = 256;
    private int resolution = 256;

    [Header("Starting Vision")]
    [SerializeField] private float initialRevealRadius = 22f;

    [Header("Colors")]
    [SerializeField] private Color unexploredColor = new Color(0.02f, 0.03f, 0.05f, 0.96f);
    [SerializeField] private Color exploredColor   = new Color(0.04f, 0.06f, 0.10f, 0.42f);
    [SerializeField] private Color visibleColor    = new Color(0f, 0f, 0f, 0f);

    // Runtime
    // Cells currently in state 2 ("visible"). Tracking them in a list means
    // Update() touches only the cells the fog-revealers actually cover.
    private readonly List<int> visibleCells = new List<int>();

    private Texture2D fogTexture;
    private byte[]    cellState;
    private Color[]   pixels;

    // Set to 1 by Reveal() for the current frame.
    // Update() demotes a cell to "explored" only if it was NOT refreshed (freshThisFrame == 0).
    // Cells that WERE refreshed remain in visibleCells, and freshThisFrame is reset to 0 for next frame.
    private byte[]    freshThisFrame;

    // Dirty rectangle tracking to minimize texture uploads
    private int dirtyMinX = int.MaxValue, dirtyMaxX = int.MinValue;
    private int dirtyMinY = int.MaxValue, dirtyMaxY = int.MinValue;

    private const int FOG_BORDER = 32;
    private Material  fogMaterial;

    private bool HasDirtyRect => dirtyMaxX >= dirtyMinX && dirtyMaxY >= dirtyMinY;

    private void MarkDirty(int px, int py)
    {
        if (px < dirtyMinX) dirtyMinX = px;
        if (px > dirtyMaxX) dirtyMaxX = px;
        if (py < dirtyMinY) dirtyMinY = py;
        if (py > dirtyMaxY) dirtyMaxY = py;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (fogHeight > 0.15f) fogHeight = 0.06f;

        // Auto-fix explored color if old black / high-opacity preset was serialized in scene
        if (exploredColor.a > 0.45f || (exploredColor.r == 0f && exploredColor.g == 0f && exploredColor.b == 0f))
            exploredColor = new Color(0.04f, 0.06f, 0.10f, 0.42f);

        if (unexploredColor.r == 0f && unexploredColor.g == 0f && unexploredColor.b == 0f)
            unexploredColor = new Color(0.02f, 0.03f, 0.05f, 0.96f);

        // AoE4 Golden-Hour ambient atmosphere
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.68f, 0.74f, 0.85f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.52f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.30f, 0.25f);

        if (MapBoundary.Instance != null)
            mapHalfSize = MapBoundary.Instance.HalfSize;

        if (fogPlaneRenderer == null)
        {
            Debug.LogError("[FogOfWar] Fog Plane Renderer NOT assigned!");
            enabled = false;
            return;
        }

        fogMaterial = fogPlaneRenderer.material;

        if (textureResolution < 256) textureResolution = 256;
        resolution = textureResolution;

        if (initialRevealRadius < 22f) initialRevealRadius = 22f;

        float usableRatio = (float)(resolution - 2 * FOG_BORDER) / resolution;
        fogMaterial.SetFloat("_MapHalfSize", mapHalfSize / usableRatio);

        fogTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        fogTexture.filterMode = FilterMode.Bilinear;
        fogTexture.wrapMode   = TextureWrapMode.Clamp;
        cellState      = new byte[resolution * resolution];
        freshThisFrame = new byte[resolution * resolution];
        pixels         = new Color[resolution * resolution];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = unexploredColor;

        fogTexture.SetPixels(pixels);
        fogTexture.Apply();
        fogMaterial.SetTexture("_FogTex", fogTexture);
        fogMaterial.renderQueue = 3000;
        fogMaterial.SetInt("_ZWrite", 0);
        fogMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);

        ElevateFogPlaneAboveTerrain();
    }

    private void Start()
    {
        ElevateFogPlaneAboveTerrain();

        Vector3 revealCenter = Vector3.zero;
        SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
        if (area != null)
            revealCenter = area.GetSpawnPosition(PlayerColorManager.LocalPlayerIndex);

        Debug.Log($"[FogOfWar] Starting reveal radius: {initialRevealRadius:F1} at {revealCenter}");

        // Pure network clients (joiners) skip initial reveal here;
        // their correct spawn position is revealed by RevealForPlayer()
        // via NetworkedPlayer.TargetSetLocalPlayerIndex().
        bool isPureClient = NetworkClient.active && !NetworkServer.active;
        if (!isPureClient)
            Reveal(revealCenter, initialRevealRadius);
    }

    public void RevealForPlayer(int playerIndex)
    {
        if (initialRevealRadius < 22f) initialRevealRadius = 22f;
        Vector3 center = Vector3.zero;
        SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
        if (area != null) center = area.GetSpawnPosition(playerIndex);
        Reveal(center, initialRevealRadius);
    }

    private void ElevateFogPlaneAboveTerrain()
    {
        if (fogPlaneRenderer == null) return;

        float baseY = 0f;

        foreach (Terrain t in Terrain.activeTerrains)
            if (t.transform.position.y > baseY) baseY = t.transform.position.y;

        if (baseY <= 0f)
        {
            if (Physics.Raycast(new Vector3(0f, 9999f, 0f), Vector3.down, out RaycastHit hit, 99999f))
                baseY = hit.point.y;
        }

        fogPlaneRenderer.transform.position = new Vector3(0f, baseY + fogHeight, 0f);

        float targetWorldSize = mapHalfSize * 8f;
        fogPlaneRenderer.transform.localScale =
            new Vector3(targetWorldSize / 10f, 1f, targetWorldSize / 10f);

        Debug.Log($"[FogOfWar] Fog plane at y={baseY + fogHeight:F2}, covers ±{targetWorldSize / 2f} units");
    }

    private void Update()
    {
        // Demote cells that were visible (state 2) but NOT refreshed this frame back to explored (state 1)
        if (visibleCells.Count == 0) return;

        for (int i = visibleCells.Count - 1; i >= 0; i--)
        {
            int idx = visibleCells[i];

            if (freshThisFrame[idx] == 0)
            {
                // Out of sight -> demote to explored and repaint
                cellState[idx] = 1;
                pixels[idx]    = exploredColor;
                MarkDirty(idx % resolution, idx / resolution);
                visibleCells.RemoveAt(i);
            }
            else
            {
                // Refreshed this frame -> keep in visibleCells, reset freshThisFrame for next frame
                freshThisFrame[idx] = 0;
            }
        }
    }

    public void Reveal(Vector3 worldPos, float radiusWorld)
    {
        int cx = WorldToCell(worldPos.x);
        int cy = WorldToCell(worldPos.z);
        int usable = resolution - 2 * FOG_BORDER;
        int cr = Mathf.CeilToInt(radiusWorld / (mapHalfSize * 2f) * usable);
        float crSqr = cr * cr;
        float fadeStart = cr * 0.85f; // Outer 15% smooth feathering

        for (int dy = -cr; dy <= cr; dy++)
        {
            for (int dx = -cr; dx <= cr; dx++)
            {
                float distSqr = dx * dx + dy * dy;
                if (distSqr > crSqr) continue;

                int px = cx + dx;
                int py = cy + dy;
                if (px < 0 || px >= resolution || py < 0 || py >= resolution) continue;

                int idx = py * resolution + px;
                freshThisFrame[idx] = 1;

                // Calculate smooth feathered color across the outer 15%
                float dist = Mathf.Sqrt(distSqr);
                Color cellColor = visibleColor;
                if (dist > fadeStart)
                {
                    float t = Mathf.InverseLerp(fadeStart, cr, dist);
                    t = Mathf.SmoothStep(0f, 1f, t);
                    cellColor = Color.Lerp(visibleColor, exploredColor, t);
                }

                if (cellState[idx] < 2)
                {
                    cellState[idx] = 2;
                    visibleCells.Add(idx);
                    pixels[idx] = cellColor;
                    MarkDirty(px, py);
                }
                else
                {
                    // If already visible, pick the most transparent (clearest) sample
                    if (cellColor.a < pixels[idx].a)
                    {
                        pixels[idx] = cellColor;
                        MarkDirty(px, py);
                    }
                }
            }
        }
    }

    private void LateUpdate()
    {
        // Nothing changed this frame -> skip texture upload entirely
        if (!HasDirtyRect) return;

        fogTexture.SetPixels(pixels);
        fogTexture.Apply(false, false);

        dirtyMinX = int.MaxValue; dirtyMaxX = int.MinValue;
        dirtyMinY = int.MaxValue; dirtyMaxY = int.MinValue;
    }

    private int WorldToCell(float worldCoord)
    {
        float t    = (worldCoord + mapHalfSize) / (mapHalfSize * 2f);
        int usable = resolution - 2 * FOG_BORDER;
        int cell   = Mathf.FloorToInt(t * usable) + FOG_BORDER;
        return Mathf.Clamp(cell, FOG_BORDER, resolution - 1 - FOG_BORDER);
    }

    public bool IsVisible(Vector3 worldPos)
    {
        int cx  = WorldToCell(worldPos.x);
        int cy  = WorldToCell(worldPos.z);
        int idx = cy * resolution + cx;
        return cellState[idx] == 2;
    }

    public bool IsExplored(Vector3 worldPos)
    {
        int cx  = WorldToCell(worldPos.x);
        int cy  = WorldToCell(worldPos.z);
        int idx = cy * resolution + cx;
        return cellState[idx] >= 1;
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
