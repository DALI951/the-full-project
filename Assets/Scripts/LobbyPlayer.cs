using UnityEngine;
using Mirror;
using TMPro;

public class LobbyPlayer : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "Player";

    [SyncVar(hook = nameof(OnColorChanged))]
    public Color playerColor = Color.white;

    [SyncVar]
    public int playerIndex = 0;

    [SyncVar(hook = nameof(OnReadyChanged))]
    public bool isReady = false;

    [SyncVar(hook = nameof(OnTeamChanged))]
    public int teamIndex = 0;

    [SyncVar]
    public string displayName = "";

    public static readonly Color[] AvailableColors = new Color[]
    {
        Color.cyan, Color.red, new Color(1f, 0.5f, 0f), Color.green,
        Color.magenta, Color.yellow, new Color(0.5f, 0f, 1f), Color.white,
    };

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (LobbyUI.Instance != null)
            LobbyUI.Instance.RegisterPlayer(this);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        LobbyUI.Instance?.UnregisterPlayer(this);
    }

    [Server]
    public void ServerSetup(int index)
    {
        playerIndex = index;
        playerColor = AvailableColors[index % AvailableColors.Length];
        playerName  = $"Player {index + 1}";
        displayName = playerName;
        teamIndex   = index % 2;
    }

    [Command] public void CmdSetColor(Color color)   => playerColor = color;
    [Command] public void CmdSetTeam(int team)        => teamIndex   = team;

    [Command]
    public void CmdSetReady(bool ready)
    {
        isReady = ready;
        RTSNetworkManager.Instance?.CheckAllReady();
    }

    [Command]
    public void CmdSetName(string name)
    {
        playerName  = name.Length > 0 ? name : $"Player {playerIndex + 1}";
        displayName = playerName;
    }

    /// <summary>Any player can cancel the countdown. Routes to the server.</summary>
    [Command]
    public void CmdCancelCountdown() => RTSNetworkManager.Instance?.CancelCountdown();

    // ── Countdown RPCs (called by RTSNetworkManager) ─
    [ClientRpc] public void RpcStartCountdown() => LobbyUI.Instance?.StartLocalCountdown();
    [ClientRpc] public void RpcHideCountdown()  => LobbyUI.Instance?.HideCountdown();

    void OnNameChanged(string oldVal, string newVal)  => LobbyUI.Instance?.RefreshPlayerList();
    void OnColorChanged(Color oldVal, Color newVal)   => LobbyUI.Instance?.RefreshPlayerList();
    void OnReadyChanged(bool oldVal, bool newVal)     => LobbyUI.Instance?.RefreshPlayerList();
    void OnTeamChanged(int oldVal, int newVal)        => LobbyUI.Instance?.RefreshPlayerList();
}
