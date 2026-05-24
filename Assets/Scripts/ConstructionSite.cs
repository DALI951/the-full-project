using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using Mirror;

public class ConstructionSite : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float buildTime      = 10f;
    [SerializeField] private GameObject completedPrefab;
    private string buildingName = "Building";

    [SyncVar] private float progress       = 0f;
    [SyncVar] private bool  isComplete     = false;
    [SyncVar] private int   pendingOwnerId = -1;
    [SyncVar] private int   refundFood, refundWood, refundGold;

    private List<Villager> builders = new List<Villager>();

    public void Initialize(GameObject prefab, float time,
        int costFood = 0, int costWood = 0, int costGold = 0)
    {
        completedPrefab = prefab;
        buildTime       = time;
        buildingName    = prefab != null ? prefab.name : "Building";
        refundFood      = costFood;
        refundWood      = costWood;
        refundGold      = costGold;
    }

    public void SetOwnerOnServer(int ownerId)
    {
        if (isClient && !isServer) return;
        pendingOwnerId = ownerId;
    }

    public void AssignBuilder(Villager v)
    {
        if (!builders.Contains(v))
            builders.Add(v);
        if (pendingOwnerId < 0 && v != null)
            pendingOwnerId = v.OwnerPlayerId;
    }

    private void Update()
    {
        if (isComplete) return;
        builders.RemoveAll(b => b == null);
        if (builders.Count == 0) return;

        int activeBuilders = 0;
        foreach (Villager b in builders)
        {
            float dist = Vector3.Distance(b.transform.position, transform.position);
            if (dist <= 3f) activeBuilders++;
        }

        if (activeBuilders == 0) return;

        if (!isClient || isServer)
        {
            progress += Time.deltaTime * activeBuilders;
            if (progress >= buildTime)
                Complete();
        }
    }

    private void Complete()
    {
        if (isClient && !isServer) return;
        isComplete = true;
        if (completedPrefab != null)
        {
            GameObject built = Instantiate(completedPrefab,
                transform.position, transform.rotation);
            int layer = LayerMask.NameToLayer("Building");
            SetLayerRecursive(built, layer);
            if (pendingOwnerId >= 0 && built.TryGetComponent(out Building b))
                b.SetOwner(pendingOwnerId);
            if (NetworkServer.active)
                NetworkServer.Spawn(built);
        }
        foreach (Villager b in builders)
            if (b != null) b.OnBuildingComplete();
        builders.Clear();
        if (NetworkServer.active)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    private void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursive(t.gameObject, layer);
    }

    private void OnMouseDown()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        GameUI.Instance?.HideBuildingUI();
        UnitInfoUI.Instance?.Hide();
        ResourceInfoUI.Instance?.Hide();
        BuildingInfoUI.Instance?.ShowConstructionSite(this);
    }

    public string BuildingName => buildingName;

    public int MaxHealth
    {
        get
        {
            if (completedPrefab == null) return 100;
            Building b = completedPrefab.GetComponent<Building>();
            return b != null ? b.maxBuildingHealth : 100;
        }
    }

    public int CurrentHealth =>
        (int)(buildTime > 0 ? (progress / buildTime) * MaxHealth : 0);

    public bool HasActiveBuilders
    {
        get
        {
            foreach (Villager b in builders)
            {
                if (b == null) continue;
                if (Vector3.Distance(b.transform.position, transform.position) <= 3f)
                    return true;
            }
            return false;
        }
    }

    public void Demolish()
    {
        if (isClient && !isServer) return;
        if (isComplete) return;
        float ratio = 1f - (buildTime > 0 ? Mathf.Clamp01(progress / buildTime) : 0f);
        NetworkedPlayer owner = NetworkedPlayer.Get(pendingOwnerId);
        if (owner != null)
            owner.AddResources(
                Mathf.RoundToInt(refundFood * ratio),
                Mathf.RoundToInt(refundWood * ratio),
                Mathf.RoundToInt(refundGold * ratio));
        else
            ResourceManager.Instance?.AddResources(
                Mathf.RoundToInt(refundFood * ratio),
                Mathf.RoundToInt(refundWood * ratio),
                Mathf.RoundToInt(refundGold * ratio));
        foreach (Villager b in builders)
            if (b != null) b.OnBuildingComplete();
        builders.Clear();
        if (NetworkServer.active)
            NetworkServer.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    public float Progress   => progress;
    public float BuildTime  => buildTime;
    public bool  IsComplete => isComplete;
}
