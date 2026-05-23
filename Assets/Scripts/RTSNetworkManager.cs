using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.SceneManagement;

public class RTSNetworkManager : NetworkManager
{
    public static RTSNetworkManager Instance => singleton as RTSNetworkManager;

    public static bool s_returnToJoinPanel = false;

    [Header("RTS Settings")]
    [SerializeField] private string gameSceneName   = "GameScene";
    [SerializeField] public  int    requiredPlayers = 2;

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

        Debug.Log($"[RTSNetworkManager] Player {idx} added. Total: {numPlayers}");
        LobbyUI.Instance?.RefreshPlayerList();
        LANDiscovery.Instance?.UpdatePlayerCount(numPlayers);
    }

    [Server]
    public void CheckAllReady()
    {
        LobbyUI.Instance?.RefreshPlayerList();

        int readyCount = 0, totalCount = 0;
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (!conn.identity.TryGetComponent(out LobbyPlayer lp)) continue;
            totalCount++;
            if (lp.isReady) readyCount++;
        }

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
            {
                lp.RpcStartCountdown();
                return;
            }
        }
        Debug.LogError("[RTSNetworkManager] No LobbyPlayer found to broadcast countdown start!");
    }

    private void RpcCancelCountdown()
    {
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            if (conn.identity.TryGetComponent(out LobbyPlayer lp))
            {
                lp.RpcHideCountdown();
                return;
            }
        }
        Debug.LogError("[RTSNetworkManager] No LobbyPlayer found to broadcast countdown cancel!");
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
