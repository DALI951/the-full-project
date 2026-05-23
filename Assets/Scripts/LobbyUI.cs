using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// LobbyUI v2 — complete redesign.
///
/// Player row prefab must have these named children:
///   NameText       (TMP_Text)
///   ColorImage     (Image)       — shows chosen color swatch
///   ColorBtn       (Button)      — opens color picker (local only)
///   TeamText       (TMP_Text)
///   TeamUpBtn      (Button)
///   TeamDownBtn    (Button)
///   ReadyButton    (Button)      — toggles ready state (local only)
///   ReadyText      (TMP_Text)    — "✓ Ready" / "Not Ready"
///   KickButton     (Button)      — host only, non-self
///
/// When a player is ready their controls lock (color + team disabled)
/// until they unready. The countdown is server-driven; anyone may cancel it.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────
    [Header("Lobby Info")]
    [SerializeField] private TMP_Text ipAddressText;
    [SerializeField] private TMP_Text playerCountText;

    [Header("Root Panel")]
    [SerializeField] private GameObject lobbyPanel;

    [Header("Player List")]
    [SerializeField] private Transform  playerListContainer;
    [SerializeField] private GameObject playerRowPrefab;

    [Header("Countdown")]
    [SerializeField] private GameObject countdownGroup;      // contains text + cancel button
    [SerializeField] private TMP_Text   countdownText;
    [SerializeField] private Button     cancelCountdownButton;

    [Header("Status & Controls")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button   leaveButton;

    [Header("Color Picker")]
    [SerializeField] private GameObject colorPickerPanel;
    [SerializeField] private Button[]   colorButtons;

    [Header("Team Colors (for TeamText display)")]
    [SerializeField] private Color team1Color = new Color(0.2f, 0.4f, 1f);
    [SerializeField] private Color team2Color = new Color(1f, 0.3f, 0.3f);
    [SerializeField] private Color team3Color = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color team4Color = new Color(0.8f, 0.8f, 0.2f);

    // ── Runtime ───────────────────────────────────────────────────────
    private readonly List<LobbyPlayer> players    = new List<LobbyPlayer>();
    private readonly List<GameObject>  playerRows = new List<GameObject>();
    private LobbyPlayer                localPlayer;
    private bool                       countingDown = false;
    private Coroutine                  localCountdownCoroutine;

    // ── Lifecycle ─────────────────────────────────────────────────────

    private void Awake()
    {
        // Handle scene reloads — prefer the instance that has a valid lobbyPanel
        if (Instance != null && Instance != this)
        {
            if (Instance.lobbyPanel == null && lobbyPanel != null) Instance = this;
            else { Destroy(this); return; }
        }
        else
        {
            Instance = this;
        }

        if (lobbyPanel) lobbyPanel.SetActive(false);
        if (colorPickerPanel) colorPickerPanel.SetActive(false);
        if (countdownGroup) countdownGroup.SetActive(false);
    }

    private void AutoFindReferences()
    {
        if (lobbyPanel == null)
        {
            var found = GameObject.Find("LobbyPanel");
            if (found != null) lobbyPanel = found;
        }

        var allTransforms = GetComponentsInChildren<Transform>(true);

        if (lobbyPanel == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "LobbyPanel") { lobbyPanel = t.gameObject; break; }
        }

        if (playerListContainer == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "Content" && t.parent?.name == "Viewport")
                { playerListContainer = t; break; }
        }

        if (statusText == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "StatusText") { statusText = t.GetComponent<TMP_Text>(); break; }
        }

        if (leaveButton == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "LeaveButton") { leaveButton = t.GetComponent<Button>(); break; }
        }

        if (colorPickerPanel == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "ColorPickerPanel") { colorPickerPanel = t.gameObject; break; }
        }

        if (countdownGroup == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "CountdownGroup") { countdownGroup = t.gameObject; break; }
        }

        if (countdownText == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "CountdownText") { countdownText = t.GetComponent<TMP_Text>(); break; }
        }

        if (cancelCountdownButton == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "CancelCountdownButton") { cancelCountdownButton = t.GetComponent<Button>(); break; }
        }

        if (ipAddressText == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "IPAddressText") { ipAddressText = t.GetComponent<TMP_Text>(); break; }
        }

        if (playerCountText == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "PlayerCountText") { playerCountText = t.GetComponent<TMP_Text>(); break; }
        }
    }

    private void Start()
    {
        AutoFindReferences();

        // Wire color picker buttons
        for (int i = 0; i < colorButtons.Length; i++)
        {
            int idx = i;
            if (colorButtons[i] == null) continue;

            if (idx < LobbyPlayer.AvailableColors.Length)
            {
                var cb                 = colorButtons[idx].colors;
                cb.normalColor         = LobbyPlayer.AvailableColors[idx];
                cb.highlightedColor    = LobbyPlayer.AvailableColors[idx] * 1.25f;
                colorButtons[idx].colors = cb;

                var txt = colorButtons[idx].GetComponentInChildren<TMP_Text>();
                if (txt) txt.text = "";
            }

            colorButtons[i].onClick.AddListener(() => OnColorPicked(idx));
        }

        leaveButton?.onClick.AddListener(OnLeave);
        cancelCountdownButton?.onClick.AddListener(OnCancelCountdown);
    }

    // ── Public API ────────────────────────────────────────────────────

    public void ShowLobby()
    {
        if (lobbyPanel == null)
        {
            var found = GameObject.Find("LobbyPanel");
            if (found != null) lobbyPanel = found;
            else
            {
                Debug.LogError("[LobbyUI] lobbyPanel not assigned!");
                return;
            }
        }

        MainMenuUI.Instance?.HideAllPanels();
        LoadingScreen.Instance?.Hide();
        lobbyPanel.SetActive(true);
        colorPickerPanel?.SetActive(false);
        countdownGroup?.SetActive(false);
        countingDown = false;
        if (localCountdownCoroutine != null)
        {
            StopCoroutine(localCountdownCoroutine);
            localCountdownCoroutine = null;
        }
        SetStatus("Waiting for players...");
        UpdateLobbyInfo();
    }

    public void HideLobby()
    {
        lobbyPanel?.SetActive(false);
        colorPickerPanel?.SetActive(false);
    }

    public void RegisterPlayer(LobbyPlayer player)
    {
        if (player == null || players.Contains(player)) return;
        players.Add(player);
        if (player.isLocalPlayer) localPlayer = player;

        // Apply saved display name to our LobbyPlayer the first time
        if (player.isLocalPlayer)
        {
            string savedName = SettingsManager.Instance?.PlayerName;
            if (!string.IsNullOrEmpty(savedName))
                player.CmdSetName(savedName);
        }

        RefreshPlayerList();
        LANDiscovery.Instance?.UpdatePlayerCount(players.Count);
    }

    public void UnregisterPlayer(LobbyPlayer player)
    {
        players.Remove(player);
        if (localPlayer == player) localPlayer = null;
        RefreshPlayerList();
        LANDiscovery.Instance?.UpdatePlayerCount(players.Count);
    }

    public void RefreshPlayerList()
    {
        foreach (var row in playerRows) if (row) Destroy(row);
        playerRows.Clear();

        EnsurePlayerListLayout();

        foreach (LobbyPlayer p in players)
        {
            if (p == null || playerRowPrefab == null || playerListContainer == null) continue;

            GameObject row = Instantiate(playerRowPrefab, playerListContainer);
            RectTransform rt = row.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(0.5f, 1);
            }
            playerRows.Add(row);
            BuildPlayerRow(row, p);
        }

        UpdateStatusText();
        UpdateLobbyInfo();
    }

    private void EnsurePlayerListLayout()
    {
        if (playerListContainer == null) return;

        if (playerListContainer.GetComponent<VerticalLayoutGroup>() == null)
        {
            var vlg = playerListContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        if (playerListContainer.GetComponent<ContentSizeFitter>() == null)
        {
            var csf = playerListContainer.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    // ── Countdown (server sends start, each client runs local timer) ──

    public void StartLocalCountdown()
    {
        AutoFindReferences();
        if (countdownGroup == null || countdownText == null)
        {
            Debug.LogWarning("[LobbyUI] Cannot start countdown — countdown references missing in scene");
            return;
        }
        if (localCountdownCoroutine != null)
            StopCoroutine(localCountdownCoroutine);
        localCountdownCoroutine = StartCoroutine(LocalCountdownRoutine());
    }

    private IEnumerator LocalCountdownRoutine()
    {
        countingDown = true;
        countdownGroup?.SetActive(true);

        for (int i = 5; i >= 1; i--)
        {
            if (countdownText) countdownText.text = i.ToString();
            SetStatus("All ready! Match starting...");
            yield return new WaitForSecondsRealtime(1f);
        }

        countdownGroup?.SetActive(false);
        countingDown = false;
        localCountdownCoroutine = null;
    }

    public void HideCountdown()
    {
        countingDown = false;
        if (localCountdownCoroutine != null)
        {
            StopCoroutine(localCountdownCoroutine);
            localCountdownCoroutine = null;
        }
        countdownGroup?.SetActive(false);
        SetStatus("Countdown cancelled. Waiting for players...");
        RefreshPlayerList();
    }

    // ── Private: Row Builder ──────────────────────────────────────────

    private void BuildPlayerRow(GameObject row, LobbyPlayer p)
    {
        bool isLocal = p.isLocalPlayer;
        bool locked  = p.isReady;   // when ready, options are locked until unready

        // ── Name ─────────────────────────────────────────────────────
        var nameText = row.transform.Find("NameText")?.GetComponent<TMP_Text>();
        if (nameText)
        {
            string name = !string.IsNullOrEmpty(p.displayName) ? p.displayName : p.playerName;
            nameText.text = string.IsNullOrEmpty(name) ? $"Player {p.playerIndex + 1}" : name;
        }

        // ── Color swatch ─────────────────────────────────────────────
        var colorImg = row.transform.Find("ColorImage")?.GetComponent<Image>();
        if (colorImg) colorImg.color = p.playerColor;

        // ── Color picker button (local only) ─────────────────────────
        var colorBtn = row.transform.Find("ColorBtn")?.GetComponent<Button>();
        if (colorBtn)
        {
            colorBtn.gameObject.SetActive(isLocal);
            colorBtn.interactable = !locked;
            if (isLocal)
                colorBtn.onClick.AddListener(ToggleColorPicker);
        }

        // ── Team display ─────────────────────────────────────────────
        var teamText = row.transform.Find("TeamText")?.GetComponent<TMP_Text>();
        if (teamText)
        {
            teamText.text  = $"Team {p.teamIndex + 1}";
            teamText.color = GetTeamColor(p.teamIndex);
        }

        // ── Team up / down (local only) ───────────────────────────────
        var teamUp   = row.transform.Find("TeamUpBtn")?.GetComponent<Button>();
        var teamDown = row.transform.Find("TeamDownBtn")?.GetComponent<Button>();

        if (teamUp)
        {
            teamUp.gameObject.SetActive(isLocal);
            teamUp.interactable = !locked;
            if (isLocal)
            {
                LobbyPlayer cap = p;
                teamUp.onClick.AddListener(() => cap.CmdSetTeam((cap.teamIndex + 1) % 4));
            }
        }
        if (teamDown)
        {
            teamDown.gameObject.SetActive(isLocal);
            teamDown.interactable = !locked;
            if (isLocal)
            {
                LobbyPlayer cap = p;
                teamDown.onClick.AddListener(() => cap.CmdSetTeam((cap.teamIndex + 3) % 4));
            }
        }

        // ── Ready status text ─────────────────────────────────────────
        var readyText = row.transform.Find("ReadyText")?.GetComponent<TMP_Text>();
        if (readyText)
        {
            readyText.text  = p.isReady ? "✓ Ready" : "Not Ready";
            readyText.color = p.isReady ? Color.green : Color.gray;
        }

        // ── Ready toggle button (local only) ─────────────────────────
        var readyBtn = row.transform.Find("ReadyButton")?.GetComponent<Button>();
        if (readyBtn)
        {
            readyBtn.gameObject.SetActive(isLocal);
            if (isLocal)
            {
                LobbyPlayer cap = p;
                readyBtn.onClick.AddListener(() => cap.CmdSetReady(!cap.isReady));

                var btnText = readyBtn.GetComponentInChildren<TMP_Text>();
                if (btnText) btnText.text = p.isReady ? "Unready" : "Ready";
            }
        }

        // ── Kick (host only, not self) ────────────────────────────────
        var kickBtn = row.transform.Find("KickButton")?.GetComponent<Button>();
        if (kickBtn)
        {
            bool canKick = Mirror.NetworkServer.active && !p.isLocalPlayer;
            kickBtn.gameObject.SetActive(canKick);
            if (canKick)
            {
                LobbyPlayer cap = p;
                kickBtn.onClick.AddListener(() => RTSNetworkManager.Instance?.KickPlayer(cap));
            }
        }
    }

    // ── Private: Lobby Info ──────────────────────────────────────────

    private void UpdateLobbyInfo()
    {
        if (ipAddressText)
        {
            string ip = RTSNetworkManager.Instance?.GetLocalIP() ?? "127.0.0.1";
            ipAddressText.text = $"Host: {ip}";
        }
        if (playerCountText)
        {
            int maxConn = RTSNetworkManager.Instance != null
                ? RTSNetworkManager.Instance.maxConnections : 4;
            playerCountText.text = $"Players: {players.Count}/{maxConn}";
        }
    }

    // ── Private: Status Text ──────────────────────────────────────────

    private void UpdateStatusText()
    {
        if (countingDown) return;

        int required = RTSNetworkManager.Instance != null
            ? Mathf.Max(1, RTSNetworkManager.Instance.requiredPlayers) : 2;
        int total    = players.Count;
        int ready    = 0;

        foreach (var p in players)
            if (p != null && p.isReady) ready++;

        if (total < required)
            SetStatus($"Waiting for players... ({total}/{required} connected)");
        else if (ready < total)
            SetStatus($"Waiting for everyone to ready up... ({ready}/{total} ready)");
        else if (total > 0)
            SetStatus("All ready! Starting countdown...");
        else
            SetStatus("Waiting for players...");
    }

    // ── Private: Actions ──────────────────────────────────────────────

    private void ToggleColorPicker()
    {
        if (colorPickerPanel == null) return;
        colorPickerPanel.SetActive(!colorPickerPanel.activeSelf);
    }

    private void OnColorPicked(int index)
    {
        if (localPlayer == null || index >= LobbyPlayer.AvailableColors.Length) return;
        localPlayer.CmdSetColor(LobbyPlayer.AvailableColors[index]);
        colorPickerPanel?.SetActive(false);
    }

    private void OnCancelCountdown()
    {
        // Any player can issue a cancel — the Command routes through the server
        localPlayer?.CmdCancelCountdown();
    }

    private void OnLeave()
    {
        LoadingScreen.Instance?.Show("Leaving lobby...");

        RTSNetworkManager.s_returnToJoinPanel = true;

        if (Mirror.NetworkServer.active)
        {
            RTSNetworkManager.Instance?.StopHost();
            LANDiscovery.Instance?.StopAdvertising();
        }
        else
        {
            RTSNetworkManager.Instance?.StopClient();
        }

        players.Clear();
        localPlayer = null;
        HideLobby();
        LoadingScreen.Instance?.Hide();
        MainMenuUI.Instance?.ShowJoinPanel();
    }

    private void SetStatus(string msg) { if (statusText) statusText.text = msg; }

    private Color GetTeamColor(int idx)
    {
        switch (idx)
        {
            case 0: return team1Color;
            case 1: return team2Color;
            case 2: return team3Color;
            case 3: return team4Color;
            default: return Color.white;
        }
    }
}
