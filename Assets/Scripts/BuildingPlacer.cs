using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.AI;

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

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (!isPlacing) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
        {
            Vector3 ghostPos = hit.point;
            if (NavMesh.SamplePosition(ghostPos, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
                ghostPos = new Vector3(ghostPos.x, navHit.position.y, ghostPos.z);

            // Grid snap
            ghostPos.x = Mathf.Round(ghostPos.x / snapGridSize) * snapGridSize;
            ghostPos.z = Mathf.Round(ghostPos.z / snapGridSize) * snapGridSize;

            if (ghostObject != null)
                ghostObject.transform.position = ghostPos;

            // Obstacle check → colour feedback
            isValidPlacement = !CheckPlacementBlocked(ghostPos);
            UpdateGhostColor(isValidPlacement);

            if (Input.GetMouseButtonDown(0) && isValidPlacement &&
                !EventSystem.current.IsPointerOverGameObject())
            {
                PlaceBuilding(hit.point);
            }
        }
    }

    public void StartPlacing(GameObject buildPrefab, GameObject constructionSitePrefab,
        float time, Material ghostMaterial,
        int costFood = 0, int costWood = 0, int costGold = 0)
    {
        CancelPlacement();
        buildingPrefab = buildPrefab;
        sitePrefab     = constructionSitePrefab;
        buildTime      = time;
        isPlacing      = true;
        storedCostFood = costFood;
        storedCostWood = costWood;
        storedCostGold = costGold;

        if (buildPrefab == null)
        {
            Debug.LogError("[BuildingPlacer] buildPrefab is NULL!");
            return;
        }

        ghostObject      = Instantiate(buildPrefab);
        ghostObject.name = "GhostPreview";
        
        Renderer[] renderers = ghostObject.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (ghostMaterial != null) r.material = ghostMaterial;
        }
        
        foreach (MonoBehaviour mb in ghostObject.GetComponentsInChildren<MonoBehaviour>())
            mb.enabled = false;
        foreach (Collider c in ghostObject.GetComponentsInChildren<Collider>())
            c.enabled = false;
        foreach (UnityEngine.AI.NavMeshObstacle o in
            ghostObject.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>())
            o.enabled = false;
    }

    private void PlaceBuilding(Vector3 pos)
    {
        if (sitePrefab == null) return;

        if (NetworkedPlayer.LocalInstance != null && Mirror.NetworkClient.active)
        {
            NetworkedPlayer.LocalInstance.CmdPlaceBuilding(
                buildingPrefab != null ? buildingPrefab.name : "",
                pos, Quaternion.identity, buildTime,
                storedCostFood, storedCostWood, storedCostGold);
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
        GameObject siteGO = Instantiate(sitePrefab, pos, Quaternion.identity);
        ConstructionSite site = siteGO.GetComponent<ConstructionSite>();
        site?.Initialize(buildingPrefab, buildTime, storedCostFood, storedCostWood, storedCostGold);

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
        isPlacing = false;
        if (ghostObject != null) Destroy(ghostObject);
        ghostObject = null;
    }

    private bool CheckPlacementBlocked(Vector3 pos)
    {
        Collider[] cols = Physics.OverlapSphere(pos, placementCheckRadius);
        foreach (Collider c in cols)
        {
            if (ghostObject != null && c.transform.IsChildOf(ghostObject.transform)) continue;
            if (c.GetComponentInParent<ResourceNode>()     != null) return true;
            if (c.GetComponentInParent<Unit>()             != null) return true;
            if (c.GetComponentInParent<Building>()         != null) return true;
            if (c.GetComponentInParent<ConstructionSite>() != null) return true;
        }
        return false;
    }

    private void UpdateGhostColor(bool valid)
    {
        if (ghostObject == null) return;
        Color c = valid ? validColor : invalidColor;
        foreach (Renderer r in ghostObject.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            Material m = r.material;
            if      (m.HasProperty("_Color"))     m.SetColor("_Color",     c);
            else if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }

    public bool IsPlacing => isPlacing;
}