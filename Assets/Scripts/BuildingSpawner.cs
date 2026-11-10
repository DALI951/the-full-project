using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class BuildingSpawner : MonoBehaviour
{
    private static bool s_OfflineSpawned = false;

    [Header("Building Prefabs")]
    [SerializeField] private GameObject homeSitePrefab;
    [SerializeField] private GameObject barracksPrefab;

    [Header("Starting Units")]
    [SerializeField] private GameObject villagerPrefab;
    [SerializeField] private int startingVillagerCount = 3;

    [Header("Offsets from spawn point")]
    [SerializeField] private float homeSiteOffset = -8f;
    [SerializeField] private float barracksOffset =  8f;
    [SerializeField] private float inwardFromEdge = 4f;

    [Header("Player (single-player only)")]
    [SerializeField] public int playerIndex = 0;

    [Header("Layer")]
    [SerializeField] private string buildingLayerName = "Building";

    [Header("Placement")]
    [SerializeField] private float buildingYOffset = 0f;

    public static readonly List<Vector3> BasePositions = new List<Vector3>();

    private void Awake()
    {
        s_OfflineSpawned = false;
        BasePositions.Clear();
        // Enforce clean AoE4 placement: HomeSite directly on spawn point, Barracks cleanly 8.5m on flank
        homeSiteOffset = 0f;
        barracksOffset = 8.5f;
    }

    private void Start()
    {
        if (!NetworkClient.active)
        {
            if (s_OfflineSpawned || gameObject.name.Contains("AI") || playerIndex != 0)
                return;

            s_OfflineSpawned = true;

            // Spawn local human player base (Player 0)
            SpawnForPlayer(playerIndex, playerIndex, null, true);

            // Spawn Enemy AI base (Player 1) on opposite side of map
            SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
            int count = area != null ? area.GetSpawnCount() : 10;
            int enemyPlayerIdx = 1;
            int enemySpawnIdx = count > 0 ? (playerIndex + count / 2) % count : 1;

            if (EnemyAI.Instance != null)
                EnemyAI.Instance.enemyPlayerId = enemyPlayerIdx;

            SpawnForPlayer(enemyPlayerIdx, enemySpawnIdx, null, true);

            // Center camera on human player
            FindObjectOfType<RTSCamera>()?.SetCameraPosition(playerIndex);
        }
    }

    public void SpawnForAllPlayers()
    {
        List<NetworkedPlayer> all = new List<NetworkedPlayer>(NetworkedPlayer.AllPlayersList);
        if (all.Count == 1)
        {
            SpawnForPlayer(all[0].playerIndex, all[0].playerIndex, all[0].connectionToClient, false);

            int enemyPlayerIdx = all[0].playerIndex + 1;
            if (EnemyAI.Instance != null)
                EnemyAI.Instance.enemyPlayerId = enemyPlayerIdx;
            SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
            int count = area != null ? area.GetSpawnCount() : 10;
            int enemySpawnIdx = count > 0 ? (all[0].playerIndex + count / 2) % count : 0;
            SpawnForPlayer(enemyPlayerIdx, enemySpawnIdx, null, false);
            return;
        }
        if (all.Count < 2)
            return;

        NetworkedPlayer ai = all.Find(p => IsPlayerAI(p));
        if (ai != null && EnemyAI.Instance != null)
        {
            EnemyAI.Instance.enemyPlayerId = ai.playerIndex;
        }

        all.Sort((a, b) => a.teamIndex.CompareTo(b.teamIndex));

        int pCount = all.Count;
        int team0Count = 0;
        foreach (NetworkedPlayer np in all)
            if (np.teamIndex == all[0].teamIndex) team0Count++;

        bool sameTeam = team0Count == pCount;

        for (int i = 0; i < pCount; i++)
        {
            int spawnIdx;
            if (sameTeam)
                spawnIdx = i;
            else
            {
                // "Opponent goes across the map" = half a ring away.
                int half = pCount / 2;
                spawnIdx = i < team0Count ? i : (i + half) % pCount;
            }
            SpawnForPlayer(all[i].playerIndex, spawnIdx, all[i].connectionToClient, false);
        }
    }

    private void SpawnForPlayer(int playerIdx, int spawnIdx, NetworkConnectionToClient conn, bool local)
    {
        SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
        if (area == null) { Debug.LogError("[BuildingSpawner] No SpawnAreaManager found!"); return; }

        Vector3 spawnPos  = area.GetSpawnPosition(spawnIdx);
        Vector3 toCenter  = (Vector3.zero - spawnPos).normalized;
        if (toCenter.sqrMagnitude > 0.001f)
            spawnPos += toCenter * Mathf.Max(0f, inwardFromEdge);
        Vector3 side      = Vector3.Cross(toCenter, Vector3.up).normalized;
        Quaternion rot    = Quaternion.LookRotation(toCenter);

        int buildingLayer = LayerMask.NameToLayer(buildingLayerName);
        Vector3 homeSitePos = spawnPos + side * homeSiteOffset;
        Vector3 barracksPos = spawnPos + side * barracksOffset;

        if (!BasePositions.Contains(homeSitePos))
            BasePositions.Add(homeSitePos);

        if (local)
        {
            SpawnBuildingLocal(homeSitePrefab, homeSitePos, rot, buildingLayer, playerIdx);
            SpawnBuildingLocal(barracksPrefab, barracksPos, rot, buildingLayer, playerIdx);
            SpawnStartingVillagers(homeSitePos, toCenter, side, playerIdx, null, true);
            if (playerIdx == PlayerColorManager.LocalPlayerIndex || playerIdx == playerIndex)
            {
                FindObjectOfType<RTSCamera>()?.SetCameraPosition(playerIdx);
            }
        }
        else
        {
            SpawnBuilding(homeSitePrefab, homeSitePos, rot, buildingLayer, playerIdx, conn);
            SpawnBuilding(barracksPrefab, barracksPos, rot, buildingLayer, playerIdx, conn);
            SpawnStartingVillagers(homeSitePos, toCenter, side, playerIdx, conn, false);
        }
    }

    private void SpawnStartingVillagers(Vector3 homeSitePos, Vector3 toCenter, Vector3 side, int ownerIndex, NetworkConnectionToClient conn, bool local)
    {
        GameObject vPrefab = villagerPrefab;
        if (vPrefab == null && homeSitePrefab != null)
        {
            Building b = homeSitePrefab.GetComponent<Building>();
            if (b != null && b.SpawnablePrefabs != null && b.SpawnablePrefabs.Count > 0)
                vPrefab = b.SpawnablePrefabs[0];
        }
        if (vPrefab == null)
        {
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in all)
            {
                if (go != null && !go.scene.IsValid() && go.name.Equals("Villager", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (go.GetComponent<Unit>() != null)
                    {
                        vPrefab = go;
                        break;
                    }
                }
            }
        }
        if (vPrefab == null) return;

        // Arrange starting villagers neatly in front of the Town Center (3.5m offset facing map center)
        float villagerSpacing = 1.3f;
        float startOffset = -(startingVillagerCount - 1) * 0.5f * villagerSpacing;
        Quaternion villagerRot = Quaternion.LookRotation(toCenter);

        for (int i = 0; i < startingVillagerCount; i++)
        {
            Vector3 offset = toCenter * 3.5f + side * (startOffset + i * villagerSpacing);
            Vector3 vPos = homeSitePos + offset;

            if (MapBoundary.Instance != null) vPos = MapBoundary.Instance.Clamp(vPos);
            if (UnityEngine.AI.NavMesh.SamplePosition(vPos, out UnityEngine.AI.NavMeshHit hit, 10f, UnityEngine.AI.NavMesh.AllAreas))
                vPos = hit.position;
            else if (Physics.Raycast(new Vector3(vPos.x, vPos.y + 200f, vPos.z), Vector3.down, out RaycastHit gHit, 400f))
                vPos.y = gHit.point.y;

            GameObject vGo = Instantiate(vPrefab, vPos, villagerRot);
            if (vGo.TryGetComponent(out Unit u))
            {
                u.SetOwner(ownerIndex);
            }

            // Account for starting population
            NetworkedPlayer owner = NetworkedPlayer.Get(ownerIndex);
            if (owner != null)
            {
                owner.AddPopulation(1);
            }
            else if (ownerIndex == PlayerColorManager.LocalPlayerIndex)
            {
                ResourceManager.Instance?.AddPopulation(1);
            }
            else if (EnemyAI.Instance != null && ownerIndex == EnemyAI.Instance.enemyPlayerId)
            {
                EnemyAI.Instance.AddPopulation(1);
            }

            if (!local && NetworkServer.active)
            {
                NetworkServer.Spawn(vGo, conn);
            }
        }
    }

    private void SpawnStartingVillagers(Vector3 homeSitePos, int ownerIndex, NetworkConnectionToClient conn, bool local)
    {
        Vector3 toCenter = (Vector3.zero - homeSitePos).normalized;
        if (toCenter.sqrMagnitude < 0.001f) toCenter = Vector3.forward;
        Vector3 side = Vector3.Cross(toCenter, Vector3.up).normalized;
        SpawnStartingVillagers(homeSitePos, toCenter, side, ownerIndex, conn, local);
    }

    private void SpawnBuildingLocal(GameObject prefab, Vector3 pos, Quaternion rot, int layer, int ownerIndex)
    {
        if (prefab == null) return;
        if (MapBoundary.Instance != null) pos = MapBoundary.Instance.Clamp(pos);
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 30f, UnityEngine.AI.NavMesh.AllAreas))
            pos = hit.position;
        else pos.y = 0f;
        if (Physics.Raycast(new Vector3(pos.x, pos.y + 200f, pos.z), Vector3.down, out RaycastHit groundHit, 400f))
            pos.y = groundHit.point.y;
        pos.y += buildingYOffset;
        GameObject go = Instantiate(prefab, pos, rot);
        AlignBottomToGround(go, pos.y);
        SetLayerRecursive(go, layer);
        if (go.GetComponentInChildren<Collider>() == null)
        { BoxCollider bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(2f, 2f, 2f); }
        if (go.TryGetComponent(out Building building))
            building.SetOwner(ownerIndex);
    }

    private void SpawnBuilding(GameObject prefab, Vector3 pos, Quaternion rot, int layer, int ownerIndex, NetworkConnectionToClient conn)
    {
        if (prefab == null) return;
        if (MapBoundary.Instance != null) pos = MapBoundary.Instance.Clamp(pos);
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 30f, UnityEngine.AI.NavMesh.AllAreas))
            pos = hit.position;
        else pos.y = 0f;
        if (Physics.Raycast(new Vector3(pos.x, pos.y + 200f, pos.z), Vector3.down, out RaycastHit groundHit, 400f))
            pos.y = groundHit.point.y;
        pos.y += buildingYOffset;
        GameObject go = Instantiate(prefab, pos, rot);
        AlignBottomToGround(go, pos.y);
        SetLayerRecursive(go, layer);
        if (go.GetComponentInChildren<Collider>() == null)
        { BoxCollider bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(2f, 2f, 2f); }
        if (go.TryGetComponent(out Building building))
            building.SetOwner(ownerIndex);
        if (NetworkServer.active)
            NetworkServer.Spawn(go, conn);
    }

    private void AlignBottomToGround(GameObject go, float targetGroundY)
    {
        if (go == null) return;
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds totalBounds = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is ParticleSystemRenderer || !renderers[i].enabled) continue;
                if (!hasBounds)
                {
                    totalBounds = renderers[i].bounds;
                    hasBounds = true;
                }
                else
                {
                    totalBounds.Encapsulate(renderers[i].bounds);
                }
            }
            if (hasBounds)
            {
                float bottomY = totalBounds.min.y;
                float diff = targetGroundY - bottomY;
                if (Mathf.Abs(diff) > 0.01f)
                {
                    go.transform.position += new Vector3(0f, diff, 0f);
                }
            }
        }
    }

    private void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private static bool IsPlayerAI(NetworkedPlayer p)
    {
        if (p == null) return false;
        var field = typeof(NetworkedPlayer).GetField("isAI");
        if (field != null && field.FieldType == typeof(bool))
            return (bool)field.GetValue(p);
        var prop = typeof(NetworkedPlayer).GetProperty("isAI");
        if (prop != null && prop.PropertyType == typeof(bool))
            return (bool)prop.GetValue(p);

        if (p.connectionToClient == null && p.netIdentity != null && !p.isLocalPlayer)
            return true;
        if (!string.IsNullOrEmpty(p.displayName) && (p.displayName.StartsWith("Bot") || p.displayName.StartsWith("AI")))
            return true;
        return false;
    }
}
