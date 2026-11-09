using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.AI;
using System.Collections.Generic;

public class BuildingPlacer : MonoBehaviour
{
    public static BuildingPlacer Instance { get; private set; }

    [Header("Layers")]
    [SerializeField] private LayerMask groundLayer;

    [SerializeField] private float buildingYOffset = 1f;

    [Header("Placement Validation")]
    [SerializeField] private float snapGridSize         = 2f;
    [SerializeField] private float placementCheckRadius = 1.5f;
    [SerializeField] private Color validColor           = new Color(0.2f, 0.8f, 0.2f, 0.7f);
    [SerializeField] private Color invalidColor         = new Color(1f,   0.1f, 0.1f, 0.7f);

    private bool isValidPlacement = true;
    private int  storedCostFood, storedCostWood, storedCostGold;
    
    private GameObject    ghostObject;
    private GameObject    buildingPrefab;
    private GameObject    sitePrefab;
    private float         buildTime;
    private bool          isPlacing = false;
    private Material      currentGhostMaterial;

    // Rotation & Wall Drag state
    private float         currentRotationY = 0f;
    private bool          isWallDragging   = false;
    private Vector3       wallDragStart;
    private List<GameObject> wallGhostSegments = new List<GameObject>();

    private bool IsWallPlacement =>
        buildingPrefab != null && buildingPrefab.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0;

    public bool IsPlacing => isPlacing;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (!isPlacing) return;

        // Rotation hotkey 'R' (45 degree increments)
        if (Input.GetKeyDown(KeyCode.R))
        {
            currentRotationY = (currentRotationY + 45f) % 360f;
            if (ghostObject != null)
                ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
        }

        // Cancel placement with right click or Escape
        if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
        {
            if (isWallDragging)
            {
                isWallDragging = false;
                ClearWallGhosts();
                if (ghostObject != null)
                {
                    ghostObject.SetActive(true);
                    ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
                }
                return;
            }

            CancelPlacement();
            return;
        }

        if (Camera.main == null) return;
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
        {
            Vector3 mouseGroundPos = hit.point;
            if (NavMesh.SamplePosition(mouseGroundPos, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
                mouseGroundPos = new Vector3(mouseGroundPos.x, navHit.position.y, mouseGroundPos.z);

            // Grid snap
            mouseGroundPos.x = Mathf.Round(mouseGroundPos.x / snapGridSize) * snapGridSize;
            mouseGroundPos.z = Mathf.Round(mouseGroundPos.z / snapGridSize) * snapGridSize;

            if (IsWallPlacement)
            {
                UpdateWallPlacement(mouseGroundPos);
            }
            else
            {
                UpdateNormalPlacement(mouseGroundPos);
            }
        }
    }

    public void StartPlacing(GameObject buildPrefab, GameObject constructionSitePrefab,
        float time, Material ghostMaterial,
        int costFood = 0, int costWood = 0, int costGold = 0)
    {
        CancelPlacement();
        buildingPrefab       = buildPrefab;
        sitePrefab           = constructionSitePrefab;
        buildTime            = time;
        isPlacing            = true;
        storedCostFood       = costFood;
        storedCostWood       = costWood;
        storedCostGold       = costGold;
        currentRotationY     = 0f;
        isWallDragging       = false;
        currentGhostMaterial = ghostMaterial;

        if (buildPrefab == null)
        {
            Debug.LogError("[BuildingPlacer] buildPrefab is NULL!");
            return;
        }

        ghostObject = CreateGhostSegment();
        if (ghostObject != null)
        {
            ghostObject.name = "GhostPreview";
        }
    }

    private void UpdateNormalPlacement(Vector3 ghostPos)
    {
        if (ghostObject != null)
        {
            ghostObject.SetActive(true);
            ghostObject.transform.position = ghostPos;
            ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
        }

        isValidPlacement = !CheckPlacementBlocked(ghostPos, placementCheckRadius);
        UpdateGhostColor(ghostObject, isValidPlacement);

        if (Input.GetMouseButtonDown(0) && isValidPlacement &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            PlaceBuilding(ghostPos, Quaternion.Euler(0f, currentRotationY, 0f));
        }
    }

    private void UpdateWallPlacement(Vector3 mouseGroundPos)
    {
        if (!isWallDragging)
        {
            if (ghostObject != null)
            {
                ghostObject.SetActive(true);
                ghostObject.transform.position = mouseGroundPos;
                ghostObject.transform.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
            }

            isValidPlacement = !CheckPlacementBlocked(mouseGroundPos, 0.7f);
            int costF = GetSegmentCostFood(0);
            int costW = GetSegmentCostWood(0);
            int costG = GetSegmentCostGold(0);
            if (!CanAfford(costF, costW, costG))
                isValidPlacement = false;

            UpdateGhostColor(ghostObject, isValidPlacement);

            if (Input.GetMouseButtonDown(0) &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                if (!isValidPlacement)
                {
                    AudioManager.Instance?.PlayUIError();
                    return;
                }

                isWallDragging = true;
                wallDragStart  = mouseGroundPos;
                if (ghostObject != null) ghostObject.SetActive(false);
                UpdateWallDragPreview(mouseGroundPos);
            }
        }
        else
        {
            // Currently dragging a wall line
            UpdateWallDragPreview(mouseGroundPos);

            if (Input.GetMouseButtonUp(0))
            {
                PlaceWallLine(mouseGroundPos);
            }
        }
    }

    private void UpdateWallDragPreview(Vector3 curEnd)
    {
        Vector3 delta = curEnd - wallDragStart;
        delta.y = 0f;
        float dist = delta.magnitude;
        float spacing = 2.0f;

        int count;
        Vector3 dir;
        Quaternion segRot;

        if (dist < 0.5f)
        {
            count  = 1;
            dir    = Vector3.forward;
            segRot = Quaternion.Euler(0f, currentRotationY, 0f);
        }
        else
        {
            dir    = delta.normalized;
            segRot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
            count  = Mathf.Clamp(Mathf.RoundToInt(dist / spacing) + 1, 1, 25);
        }

        EnsureGhostSegmentCount(count);

        int remFood = ResourceManager.Instance != null ? ResourceManager.Instance.Food : 9999;
        int remWood = ResourceManager.Instance != null ? ResourceManager.Instance.Wood : 9999;
        int remGold = ResourceManager.Instance != null ? ResourceManager.Instance.Gold : 9999;

        for (int i = 0; i < count; i++)
        {
            Vector3 segPos = count == 1 ? wallDragStart : (wallDragStart + dir * (i * spacing));
            segPos = GetGroundPosition(segPos);

            int costF = GetSegmentCostFood(i);
            int costW = GetSegmentCostWood(i);
            int costG = GetSegmentCostGold(i);

            bool canAfford = (remFood >= costF && remWood >= costW && remGold >= costG);
            bool blocked   = CheckPlacementBlocked(segPos, 0.7f);
            bool valid     = canAfford && !blocked;

            if (valid)
            {
                remFood -= costF;
                remWood -= costW;
                remGold -= costG;
            }

            GameObject ghost = wallGhostSegments[i];
            if (ghost != null)
            {
                ghost.transform.position = segPos;
                ghost.transform.rotation = segRot;
                UpdateGhostColor(ghost, valid);
            }
        }
    }

    private void PlaceWallLine(Vector3 curEnd)
    {
        if (sitePrefab == null || buildingPrefab == null)
        {
            CancelPlacement();
            return;
        }

        Vector3 delta = curEnd - wallDragStart;
        delta.y = 0f;
        float dist = delta.magnitude;
        float spacing = 2.0f;

        int count;
        Vector3 dir;
        Quaternion segRot;

        if (dist < 0.5f)
        {
            count  = 1;
            dir    = Vector3.forward;
            segRot = Quaternion.Euler(0f, currentRotationY, 0f);
        }
        else
        {
            dir    = delta.normalized;
            segRot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
            count  = Mathf.Clamp(Mathf.RoundToInt(dist / spacing) + 1, 1, 25);
        }

        ConstructionSite firstPlacedSite = null;
        int placedCount = 0;

        for (int i = 0; i < count; i++)
        {
            Vector3 segPos = count == 1 ? wallDragStart : (wallDragStart + dir * (i * spacing));
            segPos = GetGroundPosition(segPos);

            if (CheckPlacementBlocked(segPos, 0.7f))
                continue;

            int costF = GetSegmentCostFood(i);
            int costW = GetSegmentCostWood(i);
            int costG = GetSegmentCostGold(i);

            // Multiplayer client branch: send command to server
            if (NetworkedPlayer.LocalInstance != null && Mirror.NetworkClient.active)
            {
                NetworkedPlayer.LocalInstance.CmdPlaceBuilding(
                    buildingPrefab.name,
                    segPos,
                    segRot,
                    buildTime,
                    costF, costW, costG);
                placedCount++;
                continue;
            }

            // Offline single-player: spend resources per segment
            if (ResourceManager.Instance != null)
            {
                if (!ResourceManager.Instance.TrySpend(costF, costW, costG))
                    continue;
            }

            Vector3 spawnPos = segPos + Vector3.up * buildingYOffset;
            GameObject siteGO = Instantiate(sitePrefab, spawnPos, segRot);
            ConstructionSite site = siteGO.GetComponent<ConstructionSite>();
            site?.Initialize(buildingPrefab, buildTime, costF, costW, costG);
            site?.SetOwnerOnServer(PlayerColorManager.LocalPlayerIndex);

            WorldHealthBar.EnsureOn(siteGO, 2.5f, 2.0f);

            if (Mirror.NetworkServer.active)
                Mirror.NetworkServer.Spawn(siteGO);

            if (firstPlacedSite == null)
                firstPlacedSite = site;

            placedCount++;
        }

        if (placedCount > 0)
        {
            AudioManager.Instance?.PlayBuildPlace();

            // In single player, command selected villagers to build starting at the first placed site
            if (firstPlacedSite != null)
            {
                var selected = SelectionManager.Instance?.SelectedUnits;
                if (selected != null)
                {
                    foreach (Unit u in selected)
                    {
                        if (u is Villager v)
                            v.BuildAt(firstPlacedSite);
                    }
                }
            }
        }
        else
        {
            AudioManager.Instance?.PlayUIError();
        }

        CancelPlacement();
    }

    private void PlaceBuilding(Vector3 pos, Quaternion rot)
    {
        if (sitePrefab == null) return;

        // Multiplayer client branch: send command to server, server will spend authority resources
        if (NetworkedPlayer.LocalInstance != null && Mirror.NetworkClient.active)
        {
            NetworkedPlayer.LocalInstance.CmdPlaceBuilding(
                buildingPrefab != null ? buildingPrefab.name : "",
                pos, rot, buildTime,
                storedCostFood, storedCostWood, storedCostGold);
            AudioManager.Instance?.PlayBuildPlace();
            CancelPlacement();
            return;
        }

        if (NavMesh.SamplePosition(pos, out NavMeshHit navHit, 10f, NavMesh.AllAreas))
            pos = navHit.position;
        else
            pos.y = 0f;

        if (Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out RaycastHit groundHit, 20f))
            pos.y = groundHit.point.y;

        pos.y += buildingYOffset;

        // Offline single-player: spend resources EXACTLY ONCE upon successful placement
        if (ResourceManager.Instance != null)
        {
            if (!ResourceManager.Instance.TrySpend(storedCostFood, storedCostWood, storedCostGold))
            {
                Debug.Log("[BuildingPlacer] Cannot place: insufficient resources.");
                AudioManager.Instance?.PlayUIError();
                return;
            }
        }

        GameObject siteGO = Instantiate(sitePrefab, pos, rot);
        ConstructionSite site = siteGO.GetComponent<ConstructionSite>();
        site?.Initialize(buildingPrefab, buildTime, storedCostFood, storedCostWood, storedCostGold);
        site?.SetOwnerOnServer(PlayerColorManager.LocalPlayerIndex);

        // Attach health bar to the new construction site
        WorldHealthBar.EnsureOn(siteGO, 2.5f, 2.0f);

        if (Mirror.NetworkServer.active)
            Mirror.NetworkServer.Spawn(siteGO);

        AudioManager.Instance?.PlayBuildPlace();

        foreach (Unit u in SelectionManager.Instance?.SelectedUnits ?? 
            new System.Collections.Generic.List<Unit>())
        {
            if (u is Villager v)
                v.BuildAt(site);
        }

        CancelPlacement();
    }

    public void CancelPlacement()
    {
        isPlacing      = false;
        isWallDragging = false;
        ClearWallGhosts();
        if (ghostObject != null) Destroy(ghostObject);
        ghostObject    = null;
        buildingPrefab = null;
        sitePrefab     = null;
    }

    private void ClearWallGhosts()
    {
        for (int i = 0; i < wallGhostSegments.Count; i++)
        {
            if (wallGhostSegments[i] != null)
            {
                Destroy(wallGhostSegments[i]);
            }
        }
        wallGhostSegments.Clear();
    }

    private void EnsureGhostSegmentCount(int targetCount)
    {
        while (wallGhostSegments.Count < targetCount)
        {
            GameObject g = CreateGhostSegment();
            if (g != null)
            {
                g.name = "WallGhostSegment_" + wallGhostSegments.Count;
                wallGhostSegments.Add(g);
            }
            else break;
        }
        for (int i = 0; i < wallGhostSegments.Count; i++)
        {
            if (wallGhostSegments[i] != null)
            {
                wallGhostSegments[i].SetActive(i < targetCount);
            }
        }
    }

    private GameObject CreateGhostSegment()
    {
        if (buildingPrefab == null) return null;
        GameObject ghost = Instantiate(buildingPrefab);

        Renderer[] renderers = ghost.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (currentGhostMaterial != null) r.material = currentGhostMaterial;
        }

        foreach (MonoBehaviour mb in ghost.GetComponentsInChildren<MonoBehaviour>())
            mb.enabled = false;
        foreach (Collider c in ghost.GetComponentsInChildren<Collider>())
            c.enabled = false;
        foreach (NavMeshObstacle o in ghost.GetComponentsInChildren<NavMeshObstacle>())
            o.enabled = false;

        return ghost;
    }

    private Vector3 GetGroundPosition(Vector3 worldPos)
    {
        if (Physics.Raycast(worldPos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 50f, groundLayer))
        {
            return hit.point;
        }
        if (Physics.Raycast(worldPos + Vector3.up * 20f, Vector3.down, out RaycastHit hit2, 50f))
        {
            return hit2.point;
        }
        if (NavMesh.SamplePosition(worldPos, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
        {
            return navHit.position;
        }
        return worldPos;
    }

    private bool CheckPlacementBlocked(Vector3 pos, float radius)
    {
        Collider[] cols = Physics.OverlapSphere(pos, radius);
        foreach (Collider c in cols)
        {
            if (ghostObject != null && c.transform.IsChildOf(ghostObject.transform)) continue;
            if (IsGhostCollider(c)) continue;
            if (c.GetComponentInParent<ResourceNode>()     != null) return true;
            if (c.GetComponentInParent<Unit>()             != null) return true;
            if (c.GetComponentInParent<Building>()         != null) return true;
            if (c.GetComponentInParent<ConstructionSite>() != null) return true;
        }
        return false;
    }

    private bool IsGhostCollider(Collider c)
    {
        for (int i = 0; i < wallGhostSegments.Count; i++)
        {
            if (wallGhostSegments[i] != null && c.transform.IsChildOf(wallGhostSegments[i].transform))
                return true;
        }
        return false;
    }

    private void UpdateGhostColor(GameObject targetObject, bool valid)
    {
        if (targetObject == null) return;
        Color c = valid ? validColor : invalidColor;
        foreach (Renderer r in targetObject.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            Material m = r.material;
            if      (m.HasProperty("_Color"))     m.SetColor("_Color",     c);
            else if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }

    private bool CanAfford(int f, int w, int g)
    {
        if (ResourceManager.Instance == null) return true;
        return ResourceManager.Instance.Food >= f &&
               ResourceManager.Instance.Wood >= w &&
               ResourceManager.Instance.Gold >= g;
    }

    private int GetSegmentCostFood(int index)
    {
        if (!IsWallPlacement) return storedCostFood;
        return index == 0 ? storedCostFood : Mathf.Max(0, storedCostFood / 2);
    }

    private int GetSegmentCostWood(int index)
    {
        if (!IsWallPlacement) return storedCostWood;
        if (NetworkedPlayer.LocalInstance != null && Mirror.NetworkClient.active)
        {
            return storedCostWood;
        }
        if (index == 0) return Mathf.Min(storedCostWood, 20);
        return Mathf.Max(5, Mathf.Min(10, storedCostWood / 2));
    }

    private int GetSegmentCostGold(int index)
    {
        if (!IsWallPlacement) return storedCostGold;
        return index == 0 ? storedCostGold : Mathf.Max(0, storedCostGold / 2);
    }

    private void OnDisable()
    {
        CancelPlacement();
    }

    private void OnDestroy()
    {
        ClearWallGhosts();
        if (ghostObject != null) Destroy(ghostObject);
        ghostObject = null;
        if (Instance == this) Instance = null;
    }
}
