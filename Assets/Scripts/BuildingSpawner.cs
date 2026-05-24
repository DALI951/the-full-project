using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class BuildingSpawner : MonoBehaviour
{
    [Header("Building Prefabs")]
    [SerializeField] private GameObject homeSitePrefab;
    [SerializeField] private GameObject barracksPrefab;

    [Header("Offsets from spawn point")]
    [SerializeField] private float homeSiteOffset = -8f;
    [SerializeField] private float barracksOffset =  8f;
    [SerializeField] private float inwardFromEdge = 4f;

    [Header("Player (single-player only)")]
    [SerializeField] public int playerIndex = 0;

    [Header("Layer")]
    [SerializeField] private string buildingLayerName = "Building";

    [Header("Placement")]
    [SerializeField] private float buildingYOffset = 1f;

    private void Start()
    {
        if (!NetworkClient.active)
            SpawnForPlayer(playerIndex, playerIndex, null, true);
    }

    public void SpawnForAllPlayers()
    {
        List<NetworkedPlayer> all = new List<NetworkedPlayer>(NetworkedPlayer.AllPlayersList);
        if (all.Count == 1)
        {
            SpawnForPlayer(all[0].playerIndex, all[0].playerIndex, all[0].connectionToClient, false);

            int enemyPlayerIdx = 8;
            SpawnAreaManager area = FindObjectOfType<SpawnAreaManager>();
            int count = area != null ? area.GetSpawnCount() : 10;
            int enemySpawnIdx = count > 0 ? (all[0].playerIndex + count / 2) % count : 0;
            SpawnForPlayer(enemyPlayerIdx, enemySpawnIdx, null, false);
            return;
        }
        if (all.Count < 2)
            return;

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
                if (i < team0Count)
                    spawnIdx = i;
                else
                    spawnIdx = 9 - (i - team0Count);
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

        if (local)
        {
            SpawnBuildingLocal(homeSitePrefab, spawnPos + side * homeSiteOffset, rot, buildingLayer, playerIdx);
            SpawnBuildingLocal(barracksPrefab, spawnPos + side * barracksOffset, rot, buildingLayer, playerIdx);
        }
        else
        {
            SpawnBuilding(homeSitePrefab, spawnPos + side * homeSiteOffset, rot, buildingLayer, playerIdx, conn);
            SpawnBuilding(barracksPrefab, spawnPos + side * barracksOffset, rot, buildingLayer, playerIdx, conn);
        }
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
        SetLayerRecursive(go, layer);
        if (go.GetComponentInChildren<Collider>() == null)
        { BoxCollider bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(2f, 2f, 2f); }
        if (go.TryGetComponent(out Building building))
            building.SetOwner(ownerIndex);
        NetworkServer.Spawn(go, conn);
    }

    private void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }
}
