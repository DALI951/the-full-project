using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

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
    [SerializeField] private TMP_Dropdown maxPlayersDropdown;

    [Header("Root Panel")]
    [SerializeField] private GameObject lobbyPanel;

    [Header("Config Panel (Right)")]
    [SerializeField] private GameObject configPanel;

    [Header("Chat")]
    [SerializeField] private TMP_Text       chatDisplay;
    [SerializeField] private TMP_InputField chatInput;
    [SerializeField] private Button         chatSendButton;
    [SerializeField] private ScrollRect     chatScrollRect;

    [Header("Player List")]
    [SerializeField] private Transform  playerListContainer;
    [SerializeField] private GameObject playerRowPrefab;

    [Header("Color Popup")]
    [SerializeField] private GameObject colorPopupPrefab;

    [Header("Team Popup")]
    [SerializeField] private GameObject teamPopupPrefab;

    [Header("Countdown")]
    [SerializeField] private GameObject countdownGroup;      // contains text + cancel button
    [SerializeField] private TMP_Text   countdownText;
    [SerializeField] private Button     cancelCountdownButton;

    [Header("Status & Controls")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button   leaveButton;

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

        if (configPanel == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "ConfigPanel") { configPanel = t.gameObject; break; }
        }

        if (chatDisplay == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "ChatDisplay") { chatDisplay = t.GetComponent<TMP_Text>(); break; }
        }

        if (chatInput == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "ChatInput") { chatInput = t.GetComponent<TMP_InputField>(); break; }
        }

        if (chatSendButton == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "ChatSendButton") { chatSendButton = t.GetComponent<Button>(); break; }
        }

        if (chatScrollRect == null)
        {
            foreach (var t in allTransforms)
                if (t.name == "ChatScrollView") { chatScrollRect = t.GetComponent<ScrollRect>(); break; }
        }
    }

    private void Start()
    {
        AutoFindReferences();
        leaveButton?.onClick.AddListener(OnLeave);
        cancelCountdownButton?.onClick.AddListener(OnCancelCountdown);
        chatSendButton?.onClick.AddListener(SendChat);
        if (chatInput != null)
            chatInput.onSubmit.AddListener(_ => SendChat());
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
        countdownGroup?.SetActive(false);
        countingDown = false;
        if (localCountdownCoroutine != null)
        {
            StopCoroutine(localCountdownCoroutine);
            localCountdownCoroutine = null;
        }
        SetStatus("Waiting for players...");
        SetupMaxPlayersDropdown();
        SetupLobbyLayout();
        UpdateLobbyInfo();
    }

    public void HideLobby()
    {
        lobbyPanel?.SetActive(false);
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

        var netMan = RTSNetworkManager.Instance;
        int maxSlots = netMan != null ? Mathf.Clamp(netMan.maxConnections, 1, 8) : 4;
        var bots = netMan?.aiPlayers ?? new List<AIPlayerInfo>();

        // Slot-index lookups
        var playerAtSlot = new Dictionary<int, LobbyPlayer>();
        foreach (var p in players)
            if (p != null && !playerAtSlot.ContainsKey(p.playerIndex))
                playerAtSlot[p.playerIndex] = p;

        var botAtSlot = new Dictionary<int, AIPlayerInfo>();
        foreach (var b in bots)
            if (!botAtSlot.ContainsKey(b.playerIndex))
                botAtSlot[b.playerIndex] = b;

        if (playerListContainer == null) return;

        for (int i = 0; i < maxSlots; i++)
        {
            if (playerRowPrefab == null) continue;

            GameObject row = Instantiate(playerRowPrefab, playerListContainer);
            RectTransform rt = row.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
            }
            playerRows.Add(row);

            if (playerAtSlot.TryGetValue(i, out LobbyPlayer lp))
                BuildPlayerRow(row, lp);
            else if (botAtSlot.TryGetValue(i, out AIPlayerInfo bot))
                BuildBotRow(row, bot, i);
            else
                BuildEmptyRow(row, i);
        }

        UpdateStatusText();
        UpdateLobbyInfo();
    }

    private void EnsurePlayerListLayout()
    {
        if (playerListContainer == null) return;

        var rt = playerListContainer as RectTransform;
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(0, -90);
        }

        var vlg = playerListContainer.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = playerListContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }
        vlg.childAlignment = TextAnchor.UpperLeft;

        if (playerListContainer.GetComponent<ContentSizeFitter>() == null)
        {
            var csf = playerListContainer.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // If configPanel exists, restrict the ScrollRect width to avoid overlap
        if (configPanel != null)
        {
            var scrollRect = playerListContainer.GetComponentInParent<ScrollRect>();
            var configRt   = configPanel.transform as RectTransform;
            if (scrollRect != null && configRt != null)
            {
                var scrollRt = scrollRect.transform as RectTransform;
                if (scrollRt != null)
                {
                    float totalWidth = scrollRt.parent != null ? (scrollRt.parent as RectTransform).rect.width : Screen.width;
                    float configWidth = configRt.rect.width;
                    float fraction = 1f - (configWidth / totalWidth) - 0.02f;
                    scrollRt.anchorMin = new Vector2(0, 0);
                    scrollRt.anchorMax = new Vector2(Mathf.Clamp(fraction, 0.4f, 0.75f), 1);
                    scrollRt.offsetMin = Vector2.zero;
                    scrollRt.offsetMax = Vector2.zero;
                }
            }
        }
    }

    public void SetupLobbyLayout()
    {
        var allTransforms = GetComponentsInChildren<Transform>(true);

        // ── Panel background colors for visual identification ──
        void SetPanelColor(string name, Color color)
        {
            foreach (var t in allTransforms)
            {
                if (t.name != name || t == null) continue;
                var img = t.GetComponent<Image>();
                if (img == null) img = t.gameObject.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
                break;
            }
        }

        SetPanelColor("LobbyPanel", new Color(0.1f, 0.1f, 0.12f, 0.95f));

        // Player list background
        foreach (var t in allTransforms)
        {
            if (t == null) continue;
            if (t.name == "Viewport" && playerListContainer != null && t == playerListContainer.parent)
            {
                var img = t.GetComponent<Image>();
                if (img == null) img = t.gameObject.AddComponent<Image>();
                img.color = new Color(0.15f, 0.15f, 0.18f, 0.9f);
                img.raycastTarget = false;
                break;
            }
        }

        SetPanelColor("ConfigPanel", new Color(0.12f, 0.12f, 0.15f, 0.95f));
        SetPanelColor("ChatScrollView", new Color(0.13f, 0.13f, 0.16f, 0.9f));
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

        // ── Color button (shows player color, opens popup on click) ──
        var colorBtn = row.transform.Find("ColorBtn")?.GetComponent<Button>();
        if (colorBtn)
        {
            colorBtn.interactable = isLocal && !locked;
            colorBtn.image.color = Color.white;
            var cb = colorBtn.colors;
            cb.normalColor = p.playerColor;
            cb.highlightedColor = p.playerColor * 1.25f;
            cb.pressedColor = p.playerColor * 0.75f;
            cb.selectedColor = p.playerColor;
            cb.disabledColor = p.playerColor * 0.5f;
            colorBtn.colors = cb;
            if (isLocal)
            {
                LobbyPlayer cap = p;
                colorBtn.onClick.AddListener(() => ShowColorPopup(colorBtn, cap));
            }
        }

        // ── Team display (clickable to open team popup) ──────────────
        var teamText = row.transform.Find("TeamText")?.GetComponent<TMP_Text>();
        Button teamBtn = null;
        if (teamText)
        {
            teamText.text  = $"Team {p.teamIndex + 1}";
            teamText.color = GetTeamColor(p.teamIndex);
            teamBtn = teamText.GetComponent<Button>();
            if (teamBtn == null) teamBtn = teamText.gameObject.AddComponent<Button>();
            teamBtn.interactable = isLocal && !locked;
            if (isLocal)
            {
                LobbyPlayer cap = p;
                teamBtn.onClick.AddListener(() => ShowTeamPopup(teamBtn, cap));
            }
        }

        // Hide external team buttons (not needed — text is the trigger)
        var teamUp   = row.transform.Find("TeamUpBtn");
        var teamDown = row.transform.Find("TeamDownBtn");
        if (teamUp)   teamUp.gameObject.SetActive(false);
        if (teamDown) teamDown.gameObject.SetActive(false);

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

    // ── Private: Bot Row ─────────────────────────────────────────────

    private void BuildBotRow(GameObject row, AIPlayerInfo bot, int slot)
    {
        bool isHost = Mirror.NetworkServer.active;

        var nameText = row.transform.Find("NameText")?.GetComponent<TMP_Text>();
        if (nameText)
        {
            nameText.text = $"{bot.name} [AI]";
            nameText.color = new Color(0.6f, 1f, 0.6f);
            nameText.fontStyle = FontStyles.Italic;
        }

        var colorBtn = row.transform.Find("ColorBtn")?.GetComponent<Button>();
        if (colorBtn)
        {
            colorBtn.image.color = bot.color;
            var cb = colorBtn.colors;
            cb.normalColor = bot.color;
            colorBtn.colors = cb;
            colorBtn.interactable = isHost;
            if (isHost)
            {
                int s = slot;
                colorBtn.onClick.RemoveAllListeners();
                colorBtn.onClick.AddListener(() =>
                    ShowColorPopup(colorBtn, null, (Color c) =>
                        RTSNetworkManager.Instance?.SetBotColor(s, c)));
            }
        }

        var teamText = row.transform.Find("TeamText")?.GetComponent<TMP_Text>();
        if (teamText)
        {
            teamText.text  = $"Team {bot.teamIndex + 1}";
            teamText.color = GetTeamColor(bot.teamIndex);
            var teamBtn = teamText.GetComponent<Button>();
            if (teamBtn == null) teamBtn = teamText.gameObject.AddComponent<Button>();
            teamBtn.interactable = isHost;
            if (isHost)
            {
                int s = slot;
                teamBtn.onClick.RemoveAllListeners();
                teamBtn.onClick.AddListener(() =>
                    ShowTeamPopup(teamBtn, null, (int t) =>
                        RTSNetworkManager.Instance?.SetBotTeam(s, t)));
            }
        }

        var readyText = row.transform.Find("ReadyText")?.GetComponent<TMP_Text>();
        if (readyText)
        {
            readyText.text  = bot.isReady ? "✓ Ready" : "Not Ready";
            readyText.color = bot.isReady ? Color.green : Color.gray;
        }

        var readyBtn = row.transform.Find("ReadyButton");
        if (readyBtn)
        {
            readyBtn.gameObject.SetActive(isHost);
            if (isHost)
            {
                var btnComponent = readyBtn.GetComponent<Button>();
                if (btnComponent)
                {
                    int s = slot;
                    btnComponent.onClick.RemoveAllListeners();
                    btnComponent.onClick.AddListener(() =>
                    {
                        bool newReady = !bot.isReady;
                        readyText.text  = newReady ? "✓ Ready" : "Not Ready";
                        readyText.color = newReady ? Color.green : Color.gray;
                        RTSNetworkManager.Instance?.SetBotReady(s, newReady);
                    });
                }
            }
        }

        var teamUp   = row.transform.Find("TeamUpBtn");
        var teamDown = row.transform.Find("TeamDownBtn");
        if (teamUp)   teamUp.gameObject.SetActive(false);
        if (teamDown) teamDown.gameObject.SetActive(false);

        var kickBtn = row.transform.Find("KickButton")?.GetComponent<Button>();
        if (kickBtn)
        {
            kickBtn.gameObject.SetActive(isHost);
            if (isHost)
            {
                var kickText = kickBtn.GetComponentInChildren<TMP_Text>();
                if (kickText) kickText.text = "Remove";
                int s = slot;
                kickBtn.onClick.AddListener(() => RTSNetworkManager.Instance?.RemoveBot(s));
            }
        }
    }

    // ── Private: Empty Slot Row ───────────────────────────────────────

    private void BuildEmptyRow(GameObject row, int slot)
    {
        bool isHost = Mirror.NetworkServer.active;

        var nameText = row.transform.Find("NameText")?.GetComponent<TMP_Text>();
        if (nameText)
        {
            nameText.text = "Waiting for join...";
            nameText.color = new Color(0.5f, 0.5f, 0.5f);
            nameText.fontStyle = FontStyles.Italic;
        }

        var colorBtn = row.transform.Find("ColorBtn");
        if (colorBtn) colorBtn.gameObject.SetActive(false);

        var teamText = row.transform.Find("TeamText");
        if (teamText) teamText.gameObject.SetActive(false);

        var teamUp   = row.transform.Find("TeamUpBtn");
        var teamDown = row.transform.Find("TeamDownBtn");
        if (teamUp)   teamUp.gameObject.SetActive(false);
        if (teamDown) teamDown.gameObject.SetActive(false);

        var readyText = row.transform.Find("ReadyText");
        if (readyText) readyText.gameObject.SetActive(false);

        var readyBtn = row.transform.Find("ReadyButton");
        if (readyBtn) readyBtn.gameObject.SetActive(false);

        var kickBtn = row.transform.Find("KickButton")?.GetComponent<Button>();
        if (kickBtn)
        {
            kickBtn.gameObject.SetActive(isHost);
            if (isHost)
            {
                var kickText = kickBtn.GetComponentInChildren<TMP_Text>();
                if (kickText) kickText.text = "Add Bot";
                kickBtn.onClick.AddListener(() => RTSNetworkManager.Instance?.AddBot());
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
            var netMan = RTSNetworkManager.Instance;
            int maxConn = netMan != null ? netMan.maxConnections : 4;
            int botCount = netMan?.aiPlayers?.Count ?? 0;
            int total = players.Count + botCount;
            playerCountText.text = $"Players: {total}/{maxConn}";
        }
        if (maxPlayersDropdown)
        {
            var netMan = RTSNetworkManager.Instance;
            if (netMan != null)
                maxPlayersDropdown.SetValueWithoutNotify(Mathf.Clamp(netMan.maxConnections - 1, 0, 7));
            maxPlayersDropdown.interactable = NetworkServer.active;
        }
    }

    private void SetupMaxPlayersDropdown()
    {
        if (maxPlayersDropdown == null) return;

        maxPlayersDropdown.ClearOptions();
        var opts = new List<string>();
        for (int i = 1; i <= 8; i++)
            opts.Add($"{i} Player{(i > 1 ? "s" : "")}");
        maxPlayersDropdown.AddOptions(opts);

        var netMan = RTSNetworkManager.Instance;
        if (netMan != null)
            maxPlayersDropdown.SetValueWithoutNotify(Mathf.Clamp(netMan.maxConnections - 1, 0, 7));
        maxPlayersDropdown.interactable = NetworkServer.active;

        maxPlayersDropdown.onValueChanged.RemoveAllListeners();
        maxPlayersDropdown.onValueChanged.AddListener(OnMaxPlayersChanged);
    }

    private void OnMaxPlayersChanged(int value)
    {
        var netMan = RTSNetworkManager.Instance;
        if (netMan == null) return;
        netMan.maxConnections = value + 1;
        RefreshPlayerList();
    }

    // ── Private: Status Text ──────────────────────────────────────────

    private void UpdateStatusText()
    {
        if (countingDown) return;

        var netMan = RTSNetworkManager.Instance;
        int required = netMan != null ? Mathf.Max(1, netMan.requiredPlayers) : 2;
        int botCount = netMan?.aiPlayers?.Count ?? 0;
        int total    = players.Count + botCount;
        int ready    = 0;
        foreach (var p in players)
            if (p != null && p.isReady) ready++;
        if (netMan != null)
            foreach (var bot in netMan.aiPlayers)
                if (bot.isReady) ready++;

        if (total < required)
            SetStatus($"Waiting for players... ({total}/{required} connected)");
        else if (ready < total)
            SetStatus($"Waiting for everyone to ready up... ({ready}/{total} ready)");
        else if (total > 0)
            SetStatus("All ready! Starting countdown...");
        else
            SetStatus("Waiting for players...");
    }

    // ── Private: Color Popup ───────────────────────────────────────────

    private GameObject _activePopup;
    private GameObject _activeBlocker;
    private LobbyPlayer _popupOwner;
    private Canvas _rootCanvas;

    private void ShowColorPopup(Button origin, LobbyPlayer player, System.Action<Color> onColorPicked = null)
    {
        if (_activePopup != null) { HideColorPopup(); return; }

        if (_rootCanvas == null)
            _rootCanvas = GetComponentInParent<Canvas>() ?? lobbyPanel.GetComponentInParent<Canvas>();

        RectTransform btnRT = origin.GetComponent<RectTransform>();
        float btnW = btnRT.rect.width;
        int colorCount = LobbyPlayer.AvailableColors.Length;

        // Blocker
        GameObject blocker = new GameObject("ColorBlocker", typeof(RectTransform));
        blocker.transform.SetParent(_rootCanvas.transform, false);
        RectTransform blockerRT = blocker.GetComponent<RectTransform>();
        blockerRT.anchorMin = Vector2.zero;
        blockerRT.anchorMax = Vector2.one;
        blockerRT.sizeDelta = Vector2.zero;
        Image blockerImg = blocker.AddComponent<Image>();
        blockerImg.color = Color.clear;
        blockerImg.raycastTarget = true;
        Button blockerBtn = blocker.AddComponent<Button>();
        blockerBtn.targetGraphic = blockerImg;
        blockerBtn.onClick.AddListener(HideColorPopup);
        blocker.transform.SetAsFirstSibling();

        // Popup root
        GameObject popup;
        RectTransform popupRT;

        if (colorPopupPrefab != null)
        {
            popup = Instantiate(colorPopupPrefab, _rootCanvas.transform, false);
            popupRT = popup.GetComponent<RectTransform>();
            popupRT.pivot = new Vector2(0.5f, 1);
            popupRT.sizeDelta = new Vector2(btnW, popupRT.sizeDelta.y);

            // Find and clone the ColorOption template for each color
            Transform optionTemplate = popup.transform.Find("Viewport/Content/ColorOption") ?? popup.transform.Find("ColorOption");
            if (optionTemplate != null)
            {
                optionTemplate.gameObject.SetActive(false);
                int templateIndex = optionTemplate.GetSiblingIndex();

                for (int i = 0; i < colorCount; i++)
                {
                    int idx = i;
                    GameObject clone = Instantiate(optionTemplate.gameObject, optionTemplate.parent);
                    clone.name = $"ColorOption_{i}";
                    clone.SetActive(true);
                    clone.transform.SetSiblingIndex(templateIndex + i);

                    Image fill = clone.GetComponent<Image>();
                    if (fill != null) fill.color = LobbyPlayer.AvailableColors[i];

                    Button btn = clone.GetComponent<Button>();
                    if (btn != null)
                    {
                        var ocb = btn.colors;
                        ocb.highlightedColor = LobbyPlayer.AvailableColors[i] * 1.35f;
                        ocb.pressedColor = LobbyPlayer.AvailableColors[i] * 0.65f;
                        btn.colors = ocb;
                        btn.onClick.AddListener(() =>
                        {
                            if (onColorPicked != null)
                                onColorPicked(LobbyPlayer.AvailableColors[idx]);
                            else
                                player.CmdSetColor(LobbyPlayer.AvailableColors[idx]);
                            HideColorPopup();
                        });
                    }
                }

                optionTemplate.gameObject.SetActive(false);
            }
        }
        else
        {
            // Fallback: build from scratch
            popup = new GameObject("ColorPopup", typeof(RectTransform));
            popup.transform.SetParent(_rootCanvas.transform, false);
            popupRT = popup.GetComponent<RectTransform>();
            popupRT.pivot = new Vector2(0.5f, 1);
            popupRT.sizeDelta = new Vector2(btnW, colorCount * 26 + 6);

            Image bg = popup.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.12f, 0.12f);

            var vlg = popup.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 1;
            vlg.padding = new RectOffset(3, 3, 3, 3);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            for (int i = 0; i < colorCount; i++)
            {
                int idx = i;
                var opt = new GameObject("Opt", typeof(RectTransform));
                opt.transform.SetParent(popup.transform, false);
                var optRT = opt.GetComponent<RectTransform>();
                optRT.sizeDelta = new Vector2(0, 22);

                var optImg = opt.AddComponent<Image>();
                optImg.color = LobbyPlayer.AvailableColors[i];

                var optBtn = opt.AddComponent<Button>();
                optBtn.targetGraphic = optImg;
                var ocb = optBtn.colors;
                ocb.highlightedColor = LobbyPlayer.AvailableColors[i] * 1.35f;
                ocb.pressedColor = LobbyPlayer.AvailableColors[i] * 0.65f;
                optBtn.colors = ocb;

                optBtn.onClick.AddListener(() =>
                {
                    if (onColorPicked != null)
                        onColorPicked(LobbyPlayer.AvailableColors[idx]);
                    else
                        player.CmdSetColor(LobbyPlayer.AvailableColors[idx]);
                    HideColorPopup();
                });
            }
        }

        popup.transform.SetAsLastSibling();

        Vector3[] corners = new Vector3[4];
        btnRT.GetWorldCorners(corners);
        Vector3 worldBottomCenter = (corners[0] + corners[3]) / 2;
        popup.transform.position = new Vector3(worldBottomCenter.x, worldBottomCenter.y - 2, worldBottomCenter.z);

        _activePopup = popup;
        _activeBlocker = blocker;
        _popupOwner = player;
    }

    private void HideColorPopup()
    {
        if (_activePopup != null)
        {
            Destroy(_activePopup);
            _activePopup = null;
        }
        if (_activeBlocker != null)
        {
            Destroy(_activeBlocker);
            _activeBlocker = null;
        }
        _popupOwner = null;
    }

    // ── Private: Team Popup ────────────────────────────────────────────

    private void ShowTeamPopup(Button origin, LobbyPlayer player, System.Action<int> onTeamPicked = null)
    {
        if (_activePopup != null) { HideColorPopup(); return; }

        if (_rootCanvas == null)
            _rootCanvas = GetComponentInParent<Canvas>() ?? lobbyPanel.GetComponentInParent<Canvas>();

        RectTransform btnRT = origin.GetComponent<RectTransform>();
        float btnW = Mathf.Max(btnRT.rect.width, 120);
        int teamCount = 4;
        const float optionHeight = 40;
        const float popupPadding = 6;
        const float optionSpacing = 2;

        // Blocker
        GameObject blocker = new GameObject("TeamBlocker", typeof(RectTransform));
        blocker.transform.SetParent(_rootCanvas.transform, false);
        RectTransform blockerRT = blocker.GetComponent<RectTransform>();
        blockerRT.anchorMin = Vector2.zero;
        blockerRT.anchorMax = Vector2.one;
        blockerRT.sizeDelta = Vector2.zero;
        Image blockerImg = blocker.AddComponent<Image>();
        blockerImg.color = Color.clear;
        blockerImg.raycastTarget = true;
        Button blockerBtn = blocker.AddComponent<Button>();
        blockerBtn.targetGraphic = blockerImg;
        blockerBtn.onClick.AddListener(HideColorPopup);
        blocker.transform.SetAsFirstSibling();

        // Popup root
        GameObject popup;
        RectTransform popupRT;

        GameObject prefabToUse = teamPopupPrefab ?? colorPopupPrefab;

        if (prefabToUse != null)
        {
            popup = Instantiate(prefabToUse, _rootCanvas.transform, false);
            popupRT = popup.GetComponent<RectTransform>();
            popupRT.pivot = new Vector2(0.5f, 1);
            popupRT.sizeDelta = new Vector2(btnW, teamCount * optionHeight + (teamCount - 1) * optionSpacing + popupPadding * 2);

            Transform optionTemplate = popup.transform.Find("Viewport/Content/ColorOption") ?? popup.transform.Find("ColorOption");
            if (optionTemplate != null)
            {
                optionTemplate.gameObject.SetActive(false);
                int templateIndex = optionTemplate.GetSiblingIndex();

                for (int i = 0; i < teamCount; i++)
                {
                    int idx = i;
                    GameObject clone = Instantiate(optionTemplate.gameObject, optionTemplate.parent);
                    clone.name = $"TeamOption_{i}";
                    clone.SetActive(true);
                    clone.transform.SetSiblingIndex(templateIndex + i);

                    var cloneRT = clone.GetComponent<RectTransform>();
                    if (cloneRT != null) cloneRT.sizeDelta = new Vector2(0, optionHeight);

                    Image fill = clone.GetComponent<Image>();
                    Color teamCol = GetTeamColor(i);
                    if (fill != null)
                    {
                        fill.color = teamCol;
                        fill.type = Image.Type.Sliced;
                    }

                    var label = clone.GetComponentInChildren<TMP_Text>();
                    if (label == null)
                    {
                        var labelGO = new GameObject("Label", typeof(RectTransform));
                        labelGO.transform.SetParent(clone.transform, false);
                        label = labelGO.AddComponent<TextMeshProUGUI>();
                        var labelRT = labelGO.GetComponent<RectTransform>();
                        labelRT.anchorMin = Vector2.zero;
                        labelRT.anchorMax = Vector2.one;
                        labelRT.sizeDelta = Vector2.zero;
                        labelRT.offsetMin = new Vector2(10, 2);
                        labelRT.offsetMax = new Vector2(-10, -2);
                    }
                    label.text = $"Team {i + 1}";
                    label.color = Color.white;
                    label.fontSize = 18;
                    label.alignment = TextAlignmentOptions.Center;
                    label.fontStyle = FontStyles.Bold;

                    Button btn = clone.GetComponent<Button>();
                    if (btn != null)
                    {
                        var cb = btn.colors;
                        cb.highlightedColor = teamCol * 1.3f;
                        cb.pressedColor = teamCol * 0.65f;
                        btn.colors = cb;
                        btn.onClick.AddListener(() =>
                        {
                            if (onTeamPicked != null)
                                onTeamPicked(idx);
                            else
                                player.CmdSetTeam(idx);
                            HideColorPopup();
                        });
                    }
                }
                optionTemplate.gameObject.SetActive(false);
            }
        }
        else
        {
            // Fallback: build from scratch
            float totalH = teamCount * optionHeight + (teamCount - 1) * optionSpacing + popupPadding * 2;
            popup = new GameObject("TeamPopup", typeof(RectTransform));
            popup.transform.SetParent(_rootCanvas.transform, false);
            popupRT = popup.GetComponent<RectTransform>();
            popupRT.pivot = new Vector2(0.5f, 1);
            popupRT.sizeDelta = new Vector2(btnW, totalH);

            Image bg = popup.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.97f);

            var vlg = popup.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = optionSpacing;
            vlg.padding = new RectOffset((int)popupPadding, (int)popupPadding, (int)popupPadding, (int)popupPadding);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            for (int i = 0; i < teamCount; i++)
            {
                int idx = i;
                Color teamCol = GetTeamColor(i);

                var opt = new GameObject("Opt", typeof(RectTransform));
                opt.transform.SetParent(popup.transform, false);
                var optRT = opt.GetComponent<RectTransform>();
                optRT.sizeDelta = new Vector2(0, optionHeight);

                var optImg = opt.AddComponent<Image>();
                optImg.color = teamCol;
                optImg.type = Image.Type.Sliced;

                var outline = new GameObject("Outline", typeof(RectTransform));
                outline.transform.SetParent(opt.transform, false);
                var outlineRT = outline.GetComponent<RectTransform>();
                outlineRT.anchorMin = Vector2.zero;
                outlineRT.anchorMax = Vector2.one;
                outlineRT.sizeDelta = new Vector2(2, 2);
                var outlineImg = outline.AddComponent<Image>();
                outlineImg.color = Color.white * 0.3f;
                outlineImg.raycastTarget = false;
                outline.transform.SetAsFirstSibling();

                var labelGO = new GameObject("Label", typeof(RectTransform));
                labelGO.transform.SetParent(opt.transform, false);
                var label = labelGO.AddComponent<TextMeshProUGUI>();
                var labelRT = labelGO.GetComponent<RectTransform>();
                labelRT.anchorMin = Vector2.zero;
                labelRT.anchorMax = Vector2.one;
                labelRT.sizeDelta = Vector2.zero;
                labelRT.offsetMin = new Vector2(10, 2);
                labelRT.offsetMax = new Vector2(-10, -2);
                label.text = $"Team {i + 1}";
                label.fontSize = 18;
                label.fontStyle = FontStyles.Bold;
                label.color = Color.white;
                label.alignment = TextAlignmentOptions.Center;

                var optBtn = opt.AddComponent<Button>();
                optBtn.targetGraphic = optImg;
                var cb = optBtn.colors;
                cb.highlightedColor = teamCol * 1.3f;
                cb.pressedColor = teamCol * 0.65f;
                optBtn.colors = cb;
                optBtn.onClick.AddListener(() =>
                {
                    if (onTeamPicked != null)
                        onTeamPicked(idx);
                    else
                        player.CmdSetTeam(idx);
                    HideColorPopup();
                });
            }
        }

        popup.transform.SetAsLastSibling();

        Vector3[] corners = new Vector3[4];
        btnRT.GetWorldCorners(corners);
        Vector3 worldBottomCenter = (corners[0] + corners[3]) / 2;
        popup.transform.position = new Vector3(worldBottomCenter.x, worldBottomCenter.y - 2, worldBottomCenter.z);

        _activePopup = popup;
        _activeBlocker = blocker;
        _popupOwner = player;
    }

    // ── Private: Actions ──────────────────────────────────────────────

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

    public void SetStatus(string msg) { if (statusText) statusText.text = msg; }

    // ── Chat ───────────────────────────────────────────────────────────

    private void SendChat()
    {
        if (chatInput == null || localPlayer == null) return;
        string msg = chatInput.text.Trim();
        if (string.IsNullOrWhiteSpace(msg)) return;
        localPlayer.CmdSendChat(msg);
        chatInput.text = "";
        chatInput.ActivateInputField();
    }

    public void OnChatMessage(string senderName, Color senderColor, string message)
    {
        if (chatDisplay == null) return;
        string colorHex = ColorUtility.ToHtmlStringRGB(senderColor);
        chatDisplay.text += $"\n<color=#{colorHex}>{senderName}</color>: {message}";
        if (chatScrollRect != null)
            Canvas.ForceUpdateCanvases();
        if (chatScrollRect != null)
            chatScrollRect.verticalNormalizedPosition = 0f;
    }

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
