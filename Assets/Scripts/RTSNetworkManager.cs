using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.SceneManagement;

[System.Serializable]
public class AIPlayerInfo
{
    public string name;
    public Color color;
    public int teamIndex;
    public int playerIndex;
    public bool isReady;
}

[System.Serializable]
public class BotListWrapper { public AIPlayerInfo[] bots; }

public class RTSNetworkManager : NetworkManager
{
    public static RTSNetworkManager Instance => singleton as RTSNetworkManager;

    public static bool s_returnToJoinPanel = false;

    [Header("RTS Settings")]
    [SerializeField] private string gameSceneName   = "GameScene";
    [SerializeField] public  int    requiredPlayers = 2;
    [SerializeField] private GameObject botPrefab;

    [System.NonSerialized] public List<AIPlayerInfo> aiPlayers = new List<AIPlayerInfo>();

    private bool      _gameHasStarted      = false;
    private bool      _wasConnected        = false;
    private Coroutine _countdownCoroutine  = null;
    public  bool      skipLobbyAndCountdown = false;

    public override void Awake()
    {
        base.Awake();
        onlineScene = "";
    }

    public override void OnStartHost()
    {
        base.OnStartHost();
        _gameHasStarted = false;
        aiPlayers.Clear();

        if (skipLobbyAndCountdown)
        {
            skipLobbyAndCountdown = false;
            StartCoroutine(StartSinglePlayerMatch());
        }
        else
        {
            StartCoroutine(ShowLobbyAfterDelay());
        }
    }

    private IEnumerator StartSinglePlayerMatch()
    {
        yield return null;
        StartMatch();
    }

    private IEnumerator ShowLobbyAfterDelay()
    {
        yield return new WaitForSeconds(0.3f);
        string ip = GetLocalIP();
        if (MainMenuUI.Instance != null)
            MainMenuUI.Instance.OnHostStarted(ip);
        else
            LoadingScreen.Instance?.Hide();

        if (LobbyUI.Instance != null)
            LobbyUI.Instance.ShowLobby();
        else
        {
            LobbyUI found = FindObjectOfType<LobbyUI>();
            if (found != null)
                found.ShowLobby();
            else
                Debug.LogError("[RTSNetworkManager] LobbyUI.Instance is null — lobby cannot be shown");
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Debug.Log("[RTSNetworkManager] Client starting...");
    }

    public override void OnClientConnect()
    {
        _wasConnected = true;
        base.OnClientConnect();
        LoadingScreen.Instance?.Hide();
        if (!NetworkServer.active)
            LobbyUI.Instance?.ShowLobby();
    }

    public override void OnClientDisconnect()
    {
        bool wasConn  = _wasConnected;
        _wasConnected = false;

        base.OnClientDisconnect();

        if (!_gameHasStarted)
        {
            LoadingScreen.Instance?.Hide();
            if (!wasConn)
            {
                MainMenuUI.Instance?.OnConnectFailed(
                    "Could not reach the host. Check the IP and try again.");
            }
            else
            {
                s_returnToJoinPanel = true;
                SceneManager.LoadScene(offlineScene);
            }
        }
    }

    public override void OnClientError(TransportError error, string reason)
    {
        base.OnClientError(error, reason);
        if (!_gameHasStarted)
        {
            _wasConnected = false;
            LoadingScreen.Instance?.Hide();
            MainMenuUI.Instance?.OnConnectFailed($"{error}: {reason}");
        }
    }

    public override void OnServerSceneChanged(string newScene)
    {
        base.OnServerSceneChanged(newScene);
        if (newScene == gameSceneName)
        {
            _gameHasStarted = true;
            StartCoroutine(SpawnAllPlayersAndSync());
        }
    }

    private IEnumerator SpawnAllPlayersAndSync()
    {
        yield return new WaitForSeconds(0.5f);

        List<string> names = new List<string>();
        List<int> indices = new List<int>();
        List<int> teams = new List<int>();

        foreach (var kv in NetworkServer.connections)
        {
            NetworkConnectionToClient conn = kv.Value;
            if (conn?.identity == null) continue;
            if (!conn.identity.TryGetComponent(out LobbyPlayer lp)) continue;

            NetworkedPlayer np = conn.identity.GetComponent<NetworkedPlayer>();
            if (np != null)
            {
                np.ServerSetup(lp.playerIndex, lp.displayName, lp.teamIndex);
                np.TargetSetLocalPlayerIndex(lp.playerIndex);
                names.Add(lp.displayName);
                indices.Add(lp.playerIndex);
                teams.Add(lp.teamIndex);
            }
        }

        if (botPrefab != null)
        {
            foreach (AIPlayerInfo bot in aiPlayers)
            {
                GameObject botGO = Instantiate(botPrefab, Vector3.zero, Quaternion.identity);
                NetworkedPlayer np = botGO.GetComponent<NetworkedPlayer>();
                if (np != null)
                {
                    np.ServerSetup(bot.playerIndex, bot.name, bot.teamIndex);
                    names.Add(bot.name);
                    indices.Add(bot.playerIndex);
                    teams.Add(bot.teamIndex);
                }
                NetworkServer.Spawn(botGO);
            }
        }

        if (names.Count > 0)
        {
            NetworkedPlayer.BroadcastSyncPlayerList(names.ToArray(), indices.ToArray(), teams.ToArray());

            BuildingSpawner bs = FindObjectOfType<BuildingSpawner>();
            if (bs != null) bs.SpawnForAllPlayers();
        }
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (conn == null) return;

        if (_gameHasStarted && conn.identity != null)
        {
            if (conn.identity.TryGetComponent(out LobbyPlayer lp))
            {
                int idx = lp.playerIndex;
                string name = lp.displayName;
                CleanupPlayerObjects(idx);
                NetworkedPlayer.BroadcastPlayerDisconnected(idx, name);
                UpdatePlayerList();
            }
        }

        base.OnServerDisconnect(conn);

        if (!_gameHasStarted)
        {
            ReassignBotIndices();
            SyncBotListToClients();
            LobbyUI.Instance?.RefreshPlayerList();
            LANDiscovery.Instance?.UpdatePlayerCount(Mathf.Max(0, numPlayers - 1));
        }
    }

    [Server]
    private void CleanupPlayerObjects(int playerIndex)
    {
        List<Building> buildingsToRemove = new List<Building>();
        foreach (Building b in Building.AllBuildings)
            if (b != null && b.OwnerPlayerId == playerIndex)
                buildingsToRemove.Add(b);
        foreach (Building b in buildingsToRemove)
            if (b != null) NetworkServer.Destroy(b.gameObject);

        if (UnitSelectionManager.Instance != null)
        {
            List<Unit> unitsToRemove = new List<Unit>();
            foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
                if (u != null && u.OwnerPlayerId == playerIndex)
                    unitsToRemove.Add(u);
            foreach (Unit u in unitsToRemove)
                if (u != null) NetworkServer.Destroy(u.gameObject);
        }
    }

    [Server]
    private void UpdatePlayerList()
    {
        List<string> names = new List<string>();
        List<int> indices = new List<int>();
        List<int> teams = new List<int>();
        foreach (var kv in NetworkServer.connections)
        {
            NetworkConnectionToClient conn = kv.Value;
            if (conn?.identity == null) continue;
            if (!conn.identity.TryGetComponent(out LobbyPlayer lp)) continue;
            names.Add(lp.displayName);
            indices.Add(lp.playerIndex);
            teams.Add(lp.teamIndex);
        }
        foreach (AIPlayerInfo bot in aiPlayers)
        {
            names.Add(bot.name);
            indices.Add(bot.playerIndex);
            teams.Add(bot.teamIndex);
        }
        NetworkedPlayer.BroadcastSyncPlayerList(names.ToArray(), indices.ToArray(), teams.ToArray());
    }

    public override void OnClientSceneChanged()
    {
        base.OnClientSceneChanged();
        if (SceneManager.GetActiveScene().name == gameSceneName)
            _gameHasStarted = true;
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (numPlayers >= maxConnections)
        {
            Debug.LogWarning($"[RTSNetworkManager] Lobby full ({numPlayers}/{maxConnections}). Rejecting {conn}.");
            conn.Disconnect();
            return;
        }

        base.OnServerAddPlayer(conn);
        int idx = numPlayers - 1;

        if (conn.identity.TryGetComponent(out LobbyPlayer lp))
            lp.ServerSetup(idx);

        // Bump any bot occupying this slot
        for (int i = aiPlayers.Count - 1; i >= 0; i--)
        {
            if (aiPlayers[i].playerIndex == idx)
            {
                aiPlayers.RemoveAt(i);
                break;
            }
        }

        Debug.Log($"[RTSNetworkManager] Player {idx} added. Total: {numPlayers}");
        SyncBotListToClients();
        LobbyUI.Instance?.RefreshPlayerList();
        LANDiscovery.Instance?.UpdatePlayerCount(numPlayers);
    }

    public int GetTotalPlayerCount()
    {
        int count = 0;
        if (NetworkServer.active)
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn?.identity == null) continue;
                if (conn.identity.TryGetComponent(out LobbyPlayer _)) count++;
            }
        }
        return count + aiPlayers.Count;
    }

    public int GetReadyCount()
    {
        int ready = 0;
        if (NetworkServer.active)
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn?.identity == null) continue;
                if (conn.identity.TryGetComponent(out LobbyPlayer lp) && lp.isReady) ready++;
            }
        }
        return ready + aiPlayers.FindAll(b => b.isReady).Count;
    }

    [Server]
    public void CheckAllReady()
    {
        LobbyUI.Instance?.RefreshPlayerList();

        if (HasDuplicateColors())
        {
            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null;
                RpcCancelCountdown();
            }
            foreach (var conn in NetworkServer.connections.Values)
                if (conn?.identity?.TryGetComponent(out LobbyPlayer lp) ?? false)
                    { lp.RpcShowColorWarning(); break; }
            return;
        }

        int totalCount = GetTotalPlayerCount();
        int readyCount = GetReadyCount();

        bool allReady = totalCount >= requiredPlayers
                     && readyCount == totalCount
                     && totalCount > 0;

        if (allReady)
        {
            if (_countdownCoroutine == null)
                _countdownCoroutine = StartCoroutine(CountdownRoutine());
        }
        else
        {
            if (_countdownCoroutine != null)
            {
                StopCoroutine(_countdownCoroutine);
                _countdownCoroutine = null;
                RpcCancelCountdown();
            }
        }
    }

    [Server]
    private bool HasDuplicateColors()
    {
        var colors = new HashSet<Color>();
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (conn.identity.TryGetComponent(out LobbyPlayer lp))
            {
                if (colors.Contains(lp.playerColor)) return true;
                colors.Add(lp.playerColor);
            }
        }
        foreach (var bot in aiPlayers)
        {
            if (colors.Contains(bot.color)) return true;
            colors.Add(bot.color);
        }
        return false;
    }

    [Server]
    public void CancelCountdown()
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }

        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (conn.identity.TryGetComponent(out LobbyPlayer lp))
                lp.isReady = false;
        }

        RpcCancelCountdown();
        LobbyUI.Instance?.RefreshPlayerList();
    }

    [Server]
    public int AddBot()
    {
        int realCount = 0;
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (conn.identity.TryGetComponent(out LobbyPlayer _)) realCount++;
        }
        int botIndex = realCount + aiPlayers.Count;
        if (botIndex >= maxConnections) return -1;

        var bot = new AIPlayerInfo
        {
            name = $"Bot {aiPlayers.Count + 1}",
            color = LobbyPlayer.AvailableColors[botIndex % LobbyPlayer.AvailableColors.Length],
            teamIndex = botIndex % 4,
            playerIndex = botIndex,
            isReady = false
        };
        aiPlayers.Add(bot);
        SyncBotListToClients();
        CheckAllReady();
        return botIndex;
    }

    [Server]
    public void RemoveBot(int slotIndex)
    {
        for (int i = 0; i < aiPlayers.Count; i++)
        {
            if (aiPlayers[i].playerIndex == slotIndex)
            {
                aiPlayers.RemoveAt(i);
                break;
            }
        }
        SyncBotListToClients();
        LobbyUI.Instance?.RefreshPlayerList();
    }

    [Server]
    public void SetBotReady(int slotIndex, bool ready)
    {
        for (int i = 0; i < aiPlayers.Count; i++)
        {
            if (aiPlayers[i].playerIndex == slotIndex)
            {
                aiPlayers[i].isReady = ready;
                break;
            }
        }
        SyncBotListToClients();
        CheckAllReady();
    }

    [Server]
    public void SetBotColor(int slotIndex, Color color)
    {
        for (int i = 0; i < aiPlayers.Count; i++)
        {
            if (aiPlayers[i].playerIndex == slotIndex)
            {
                aiPlayers[i].color = color;
                break;
            }
        }
        SyncBotListToClients();
        LobbyUI.Instance?.RefreshPlayerList();
    }

    [Server]
    public void SetBotTeam(int slotIndex, int team)
    {
        for (int i = 0; i < aiPlayers.Count; i++)
        {
            if (aiPlayers[i].playerIndex == slotIndex)
            {
                aiPlayers[i].teamIndex = team;
                break;
            }
        }
        SyncBotListToClients();
        LobbyUI.Instance?.RefreshPlayerList();
    }

    [Server]
    private void ReassignBotIndices()
    {
        int totalReal = 0;
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (conn.identity.TryGetComponent(out LobbyPlayer _)) totalReal++;
        }
        for (int i = 0; i < aiPlayers.Count; i++)
            aiPlayers[i].playerIndex = totalReal + i;
    }

    [Server]
    private void SyncBotListToClients()
    {
        if (!NetworkServer.active) return;
        string json = JsonUtility.ToJson(new BotListWrapper { bots = aiPlayers.ToArray() });
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (conn.identity.TryGetComponent(out LobbyPlayer lp))
            {
                lp.RpcSyncBotList(json);
                break;
            }
        }
    }

    private IEnumerator CountdownRoutine()
    {
        RpcStartCountdown();
        yield return new WaitForSeconds(5f);
        _countdownCoroutine = null;
        StartMatch();
    }

    private void RpcStartCountdown()
    {
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (conn.identity.TryGetComponent(out LobbyPlayer lp))
                lp.RpcStartCountdown();
        }
    }

    private void RpcCancelCountdown()
    {
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (conn.identity.TryGetComponent(out LobbyPlayer lp))
                lp.RpcHideCountdown();
        }
    }

    [Server]
    public void StartMatch()
    {
        Debug.Log($"[RTSNetworkManager] Starting match → {gameSceneName}");
        ServerChangeScene(gameSceneName);
    }

    [Server]
    public void KickPlayer(LobbyPlayer player)
    {
        player?.connectionToClient?.Disconnect();
    }

    public string GetLocalIP()
    {
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(
                System.Net.Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }
}
