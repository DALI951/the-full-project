using UnityEngine;
using Mirror;

[DefaultExecutionOrder(-100)]
public class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance { get; private set; }

    [Header("World-Space Rendering")]
    [SerializeField] private MeshRenderer fogPlaneRenderer;
    private float mapHalfSize = 23f;
    private int resolution = 256;

    [Header("Colors")]
    [SerializeField] private Color unexploredColor = new Color(0.02f, 0.02f, 0.05f, 1f);
    [SerializeField] private Color exploredColor   = new Color(0.15f, 0.12f, 0.08f, 0.50f);
    [SerializeField] private Color visibleColor    = new Color(0f, 0f, 0f, 0f);

    // Runtime
    private Texture2D fogTexture;
    private byte[]    cellState;
    private Color[]   pixels;
    private bool      dirty = true;
    private const int FOG_BORDER = 32;
    private Material  fogMaterial;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (MapBoundary.Instance != null)
            mapHalfSize = MapBoundary.Instance.HalfSize;

        if (fogPlaneRenderer == null)
        {
            Debug.LogError("[FogOfWar] Fog Plane Renderer NOT assigned!");
            enabled = false;
            return;
        }

        fogMaterial = fogPlaneRenderer.material;

        float usableRatio = (float)(resolution - 2 * FOG_BORDER) / resolution;
        fogMaterial.SetFloat("_MapHalfSize", mapHalfSize / usableRatio);

        fogTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        fogTexture.filterMode = FilterMode.Bilinear;
        fogTexture.wrapMode   = TextureWrapMode.Clamp;
        cellState = new byte[resolution * resolution];
        pixels    = new Color[resolution * resolution];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = unexploredColor;

        fogTexture.SetPixels(pixels);
        fogTexture.Apply();
        fogMaterial.SetTexture("_FogTex", fogTexture);
        fogMaterial.renderQueue = 4000;
        fogMaterial.SetInt("_ZWrite", 0);
        fogMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    private void Start()
    {
        ElevateFogPlaneAboveTerrain();

        float revealRadius = Mathf.Clamp(mapHalfSize * 0.04f, 8f, 20f);

        Vector3 revealCenter = Vector3.zero;
        SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
        if (area != null)
            revealCenter = area.GetSpawnPosition(PlayerColorManager.LocalPlayerIndex);

        Debug.Log($"[FogOfWar] Starting reveal radius: {revealRadius:F1} at {revealCenter}");

        // Pure network clients (joiners) skip the initial reveal here.
        // Their correct spawn position is revealed by RevealForPlayer()
        // called from NetworkedPlayer.TargetSetLocalPlayerIndex().
        // Without this guard, joiners would reveal spawn 0 (host's base) by default.
        bool isPureClient = NetworkClient.active && !NetworkServer.active;
        if (!isPureClient)
            Reveal(revealCenter, revealRadius);
    }

    public void RevealForPlayer(int playerIndex)
    {
        float revealRadius = Mathf.Clamp(mapHalfSize * 0.04f, 8f, 20f);
        Vector3 center = Vector3.zero;
        SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
        if (area != null) center = area.GetSpawnPosition(playerIndex);
        Reveal(center, revealRadius);
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

        fogPlaneRenderer.transform.position = new Vector3(0f, baseY + 0.5f, 0f);

        float targetWorldSize = mapHalfSize * 8f;
        fogPlaneRenderer.transform.localScale =
            new Vector3(targetWorldSize / 10f, 1f, targetWorldSize / 10f);

        Debug.Log($"[FogOfWar] Fog plane at y={baseY + 0.5f:F1}, covers ±{targetWorldSize/2f} units");
    }


    private void Update()
    {
        for (int i = 0; i < cellState.Length; i++)
        {
            if (cellState[i] == 2)
            {
                cellState[i] = 1;
                dirty = true;
            }
        }
    }

    public void Reveal(Vector3 worldPos, float radiusWorld)
    {
        int cx = WorldToCell(worldPos.x);
        int cy = WorldToCell(worldPos.z);
        int usable = resolution - 2 * FOG_BORDER;
        int cr = Mathf.CeilToInt(radiusWorld / (mapHalfSize * 2f) * usable);

        for (int dy = -cr; dy <= cr; dy++)
        {
            for (int dx = -cr; dx <= cr; dx++)
            {
                if (dx * dx + dy * dy > cr * cr) continue;
                int px = cx + dx;
                int py = cy + dy;
                if (px < 0 || px >= resolution || py < 0 || py >= resolution) continue;

                int idx = py * resolution + px;
                if (cellState[idx] < 2)
                {
                    cellState[idx] = 2;
                    dirty = true;
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;

        for (int i = 0; i < cellState.Length; i++)
        {
            switch (cellState[i])
            {
                case 0: pixels[i] = unexploredColor; break;
                case 1: pixels[i] = exploredColor;   break;
                case 2: pixels[i] = visibleColor;    break;
            }
        }

        fogTexture.SetPixels(pixels);
        fogTexture.Apply(false);
    }

    private int WorldToCell(float worldCoord)
    {
        float t      = (worldCoord + mapHalfSize) / (mapHalfSize * 2f);
        int usable   = resolution - 2 * FOG_BORDER;
        int cell     = Mathf.FloorToInt(t * usable) + FOG_BORDER;
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
}
