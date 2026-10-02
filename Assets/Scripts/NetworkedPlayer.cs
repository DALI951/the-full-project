using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class NetworkedPlayer : NetworkBehaviour
{
    public static NetworkedPlayer LocalInstance { get; private set; }

    [SyncVar] public int playerIndex = 0;
    [SyncVar] public string displayName = "Player";
    [SyncVar] public int teamIndex = 0;

    [SyncVar] private int syncFood = 200;
    [SyncVar] private int syncWood = 150;
    [SyncVar] private int syncGold = 100;
    [SyncVar] private int syncPopulation = 0;
    [SyncVar] private int syncMaxPopulation = 20;

    public int Food => syncFood;
    public int Wood => syncWood;
    public int Gold => syncGold;
    public int CurrentPopulation => syncPopulation;
    public int MaxPopulation => syncMaxPopulation;

    private static readonly List<NetworkedPlayer> AllPlayers = new List<NetworkedPlayer>();
    public static List<NetworkedPlayer> AllPlayersList => AllPlayers;

    public override void OnStartClient()
    {
        base.OnStartClient();
        AllPlayers.Add(this);
        if (isLocalPlayer) LocalInstance = this;
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        AllPlayers.Remove(this);
        if (LocalInstance == this) LocalInstance = null;
    }

    [TargetRpc]
    public void TargetSetLocalPlayerIndex(int index)
    {
        PlayerColorManager.LocalPlayerIndex = index;
        RTSCamera cam = FindObjectOfType<RTSCamera>();
        if (cam != null) cam.SetCameraPosition(index);
        FogOfWar fog = FogOfWar.Instance;
        if (fog != null) fog.RevealForPlayer(index);
    }

    public static NetworkedPlayer Get(int index)
    {
        foreach (var p in AllPlayers)
            if (p != null && p.playerIndex == index) return p;
        return null;
    }

    [Server]
    public void ServerSetup(int index, string name, int team)
    {
        playerIndex = index;
        displayName = name;
        teamIndex = team;
        syncFood = 200;
        syncWood = 150;
        syncGold = 100;
    }

    public static bool SameTeam(int playerA, int playerB)
    {
        NetworkedPlayer a = Get(playerA);
        NetworkedPlayer b = Get(playerB);
        if (a == null || b == null) return false;
        return a.teamIndex == b.teamIndex;
    }

    [Server] public bool TrySpend(int f, int w, int g)
    {
        if (syncFood < f || syncWood < w || syncGold < g) return false;
        syncFood -= f; syncWood -= w; syncGold -= g;
        return true;
    }

    [Server] public void AddResources(int f, int w, int g)
    {
        syncFood = UnityEngine.Mathf.Max(0, syncFood + f);
        syncWood = UnityEngine.Mathf.Max(0, syncWood + w);
        syncGold = UnityEngine.Mathf.Max(0, syncGold + g);
    }

    [Server] public void AddPopulation(int a) => syncPopulation = UnityEngine.Mathf.Clamp(syncPopulation + a, 0, syncMaxPopulation);
    [Server] public void RemovePopulation(int a) => syncPopulation = UnityEngine.Mathf.Max(0, syncPopulation - a);
    [Server] public bool CanAddPopulation(int a = 1) => syncPopulation + a <= syncMaxPopulation;

    [Command]
    public void CmdMoveUnit(uint unitNetId, Vector3 destination)
    {
        if (!NetworkServer.spawned.TryGetValue(unitNetId, out NetworkIdentity ni)) return;
        Unit unit = ni.GetComponent<Unit>();
        if (unit == null || unit.OwnerPlayerId != playerIndex) return;
        unit.MoveTo(destination);
    }

    [Command]
    public void CmdMoveUnits(uint[] unitNetIds, Vector3 destination)
    {
        foreach (uint id in unitNetIds)
        {
            if (!NetworkServer.spawned.TryGetValue(id, out NetworkIdentity ni)) continue;
            Unit unit = ni.GetComponent<Unit>();
            if (unit == null || unit.OwnerPlayerId != playerIndex) continue;
            unit.MoveTo(destination);
        }
    }

    [Command]
    public void CmdUnitAttack(uint attackerNetId, uint targetNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(attackerNetId, out NetworkIdentity atkNi)) return;
        if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity tgtNi)) return;
        Unit attacker = atkNi.GetComponent<Unit>();
        Unit target = tgtNi.GetComponent<Unit>();
        if (attacker == null || target == null || attacker.OwnerPlayerId != playerIndex) return;
        attacker.SetAttackTarget(target);
    }

    [Command]
    public void CmdUnitAttackBuilding(uint attackerNetId, uint buildingNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(attackerNetId, out NetworkIdentity atkNi)) return;
        if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity bldNi)) return;
        Unit attacker = atkNi.GetComponent<Unit>();
        Building building = bldNi.GetComponent<Building>();
        if (attacker == null || building == null || attacker.OwnerPlayerId != playerIndex) return;
        attacker.SetBuildingTarget(building);
    }

    [Command]
    public void CmdVillagerGatherAt(uint villagerNetId, Vector3 resourcePosition)
    {
        if (!NetworkServer.spawned.TryGetValue(villagerNetId, out NetworkIdentity vNi)) return;
        Villager villager = vNi.GetComponent<Villager>();
        if (villager == null || villager.OwnerPlayerId != playerIndex) return;
        Collider[] hits = Physics.OverlapSphere(resourcePosition, 1.5f);
        ResourceNode node = null;
        foreach (var hit in hits)
        {
            node = hit.GetComponentInParent<ResourceNode>();
            if (node != null) break;
        }
        if (node != null) villager.GatherFrom(node);
        else villager.MoveTo(resourcePosition);
    }

    [Command]
    public void CmdTrainUnit(uint buildingNetId, int unitIndex)
    {
        if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity ni)) return;
        Building building = ni.GetComponent<Building>();
        if (building == null || building.OwnerPlayerId != playerIndex) return;
        building.SpawnUnit(unitIndex);
    }

    [Command]
    public void CmdPlaceBuilding(string prefabName, Vector3 position, Quaternion rotation, float buildTime, int costFood, int costWood, int costGold)
    {
        // ── SERVER AUTHORITY ──────────────────────────────────────────────
        // Cost and build time ALWAYS come from the server's own BuildMenuUI
        // entry. The costFood/costWood/costGold arguments arrive from the
        // client and are deliberately IGNORED: a modified client could call
        // this with 0/0/0 and place buildings for free.
        BuildMenuUI.BuildEntry entry = FindBuildEntry(prefabName);
        if (entry == null || entry.constructionSitePrefab == null) return;

        int   costF   = Mathf.Max(0, entry.costFood);
        int   costW   = Mathf.Max(0, entry.costWood);
        int   costG   = Mathf.Max(0, entry.costGold);
        float siteTime = entry.buildTime > 0f ? entry.buildTime : buildTime;

        if (!TrySpend(costF, costW, costG)) return;

        GameObject sitePrefab    = entry.constructionSitePrefab;
        GameObject buildingPrefab = entry.buildingPrefab;

        // Keep placements inside the map: a client must not build off-world.
        if (MapBoundary.Instance != null)
        {
            float limit = MapBoundary.Instance.CameraLimit;
            position.x = Mathf.Clamp(position.x, -limit, limit);
            position.z = Mathf.Clamp(position.z, -limit, limit);
        }

        if (!UnityEngine.AI.NavMesh.SamplePosition(position, out UnityEngine.AI.NavMeshHit navHit, 10f, UnityEngine.AI.NavMesh.AllAreas))
            position.y = 0f;
        if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit groundHit, 20f))
            position.y = groundHit.point.y;
        position.y += 1f;

        GameObject siteGO = Instantiate(sitePrefab, position, rotation);
        NetworkServer.Spawn(siteGO);
        ConstructionSite site = siteGO.GetComponent<ConstructionSite>();
        if (site != null)
        {
            site.Initialize(buildingPrefab, siteTime, costF, costW, costG);
            site.SetOwnerOnServer(playerIndex);
        }

        TargetSiteCreated(siteGO.GetComponent<NetworkIdentity>()?.netId ?? 0);
    }

    /// <summary>Server-side lookup of a buildable entry by its finished-building prefab name.</summary>
    private static BuildMenuUI.BuildEntry FindBuildEntry(string prefabName)
    {
        BuildMenuUI.BuildEntry[] all = BuildMenuUI.AllEntries;
        if (all == null || string.IsNullOrEmpty(prefabName)) return null;
        foreach (BuildMenuUI.BuildEntry entry in all)
            if (entry != null && entry.buildingPrefab != null && entry.buildingPrefab.name == prefabName)
                return entry;
        return null;
    }

    [TargetRpc]
    private void TargetSiteCreated(uint siteNetId)
    {
        if (siteNetId == 0) return;
        List<Unit> selected = SelectionManager.Instance != null
            ? SelectionManager.Instance.SelectedUnits
            : null;
        if (selected == null) return;
        for (int i = 0; i < selected.Count; i++)
            if (selected[i] is Villager v)
                CmdAssignBuilder(siteNetId, v.netId);
    }

    [Command]
    public void CmdAssignBuilder(uint siteNetId, uint villagerNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(siteNetId, out NetworkIdentity siteNi)) return;
        if (!NetworkServer.spawned.TryGetValue(villagerNetId, out NetworkIdentity vNi)) return;
        ConstructionSite site = siteNi.GetComponent<ConstructionSite>();
        Villager v = vNi.GetComponent<Villager>();
        if (site == null || v == null || v.OwnerPlayerId != playerIndex) return;
        v.BuildAt(site);
    }

    [Command]
    public void CmdSetRallyPoint(uint buildingNetId, Vector3 point)
    {
        if (!NetworkServer.spawned.TryGetValue(buildingNetId, out NetworkIdentity ni)) return;
        Building building = ni.GetComponent<Building>();
        if (building == null || building.OwnerPlayerId != playerIndex) return;
        building.SetSpawnPoint(point);
    }

    // Entering "placement mode" is purely local UI state — BuildMenuUI.AllEntries
    // is identical static inspector data on every peer. This used to be a
    // [Command] + [TargetRpc] pair, which paid a full client -> server -> client
    // network round-trip just to toggle a local ghost preview.
    [ClientRpc]
    public void RpcStartPlacing(string prefabName)
    {
        BuildMenuUI.BuildEntry entry = FindBuildEntry(prefabName);
        if (entry == null || entry.buildingPrefab == null || entry.constructionSitePrefab == null) return;

        BuildingPlacer.Instance?.StartPlacing(
            entry.buildingPrefab,
            entry.constructionSitePrefab,
            entry.buildTime,
            entry.ghostMaterial,
            entry.costFood, entry.costWood, entry.costGold);
    }

    [Command]
    public void CmdCancelBuildingSite(uint siteNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(siteNetId, out NetworkIdentity ni)) return;
        ConstructionSite site = ni.GetComponent<ConstructionSite>();
        if (site == null) return;
        site.Demolish();
    }

    [Server]
    public static void BroadcastSyncPlayerList(string[] names, int[] indices, int[] teams)
    {
        foreach (var kv in NetworkServer.connections)
        {
            NetworkedPlayer np = kv.Value?.identity?.GetComponent<NetworkedPlayer>();
            if (np != null) np.RpcSyncPlayerList(names, indices, teams);
        }
    }

    [Server]
    public static void BroadcastPlayerDisconnected(int index, string name)
    {
        foreach (var kv in NetworkServer.connections)
        {
            NetworkedPlayer np = kv.Value?.identity?.GetComponent<NetworkedPlayer>();
            if (np != null) np.RpcPlayerDisconnected(index, name);
        }
    }

    [TargetRpc]
    public void TargetReceiveChat(string message)
    {
        Debug.Log($"[Chat] {message}");
    }

    [ClientRpc]
    public void RpcDepleteResource(Vector3 position)
    {
        ResourceNode node = ResourceNode.FindByPosition(position);
        if (node != null) node.DepleteClientSide();
    }

    public static void BroadcastResourceDepleted(Vector3 pos)
    {
        if (NetworkServer.active)
        {
            foreach (var kv in NetworkServer.connections)
            {
                NetworkedPlayer np = kv.Value?.identity?.GetComponent<NetworkedPlayer>();
                if (np != null)
                    np.RpcDepleteResource(pos);
            }
        }
    }

    [ClientRpc]
    public void RpcPlayerDisconnected(int index, string name)
    {
        Debug.Log($"[Game] Player {name} (index {index}) disconnected.");
        PlayerListUI.Instance?.RemovePlayer(index);
    }

    [ClientRpc]
    public void RpcSyncPlayerList(string[] names, int[] indices, int[] teams)
    {
        PlayerListUI.Instance?.SyncPlayers(names, indices, teams);
    }
}
