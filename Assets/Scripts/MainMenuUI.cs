using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// MainMenuUI v2 — full redesign.
///
/// Panels to create in Inspector:
///   MainPanel     — title, Play button, Settings button, Singleplayer button, Quit button
///   PlayPanel     — Host Game button, Join Game button, Back button
///   HostPanel     — IP label, max-players dropdown (1-8), Host Game button, status text, no-wifi warning
///   JoinPanel     — Direct-IP tab and Server-Browser tab
///   SettingsPanel — player name input, graphics quality dropdown, Save button
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    public static MainMenuUI Instance { get; private set; }

    // ── Panels ────────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject playPanel;
    [SerializeField] private GameObject hostPanel;
    [SerializeField] private GameObject joinPanel;
    [SerializeField] private GameObject settingsPanel;

    // ── Host Panel ────────────────────────────────────────────────────
    [Header("Host Panel")]
    [SerializeField] private TMP_Text     hostIPText;
    [SerializeField] private TMP_Dropdown maxPlayersDropdown;
    [SerializeField] private Button       startHostButton;
    [SerializeField] private TMP_Text     hostStatusText;
    [SerializeField] private GameObject   noWifiWarning;

    // ── Join Panel – Tabs ─────────────────────────────────────────────
    [Header("Join Panel — Tabs")]
    [SerializeField] private GameObject directIPTab;
    [SerializeField] private GameObject browserTab;

    // ── Join Panel – Direct IP ────────────────────────────────────────
    [Header("Join Panel — Direct IP")]
    [SerializeField] private TMP_InputField directIPInput;
    [SerializeField] private Button         connectDirectButton;

    // ── Join Panel – Server Browser ───────────────────────────────────
    [Header("Join Panel — Server Browser")]
    [SerializeField] private Transform  serverListContainer;
    [SerializeField] private GameObject serverRowPrefab;
    [SerializeField] private Button     refreshBrowserButton;
    [SerializeField] private TMP_Text   noBrowserServersText;

    [Header("Join Panel — Shared")]
    [SerializeField] private TMP_Text joinStatusText;

    // ── Settings Panel ────────────────────────────────────────────────
    [Header("Settings Panel")]
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private TMP_Dropdown   qualityDropdown;
    [SerializeField] private TMP_Dropdown   resolutionDropdown;
    [SerializeField] private Toggle        fullscreenToggle;
    [SerializeField] private Button         saveSettingsButton;
    [SerializeField] private TMP_Text       settingsSavedText;

    // ── Runtime ───────────────────────────────────────────────────────
    private float settingsSavedTimer = 0f;
    private const float SAVED_DURATION = 2f;

    // ── Lifecycle ─────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        SetupMaxPlayersDropdown();
        SetupQualityDropdown();
        SetupResolutionDropdown();
        WireButtons();

        if (LANDiscovery.Instance != null)
            LANDiscovery.Instance.OnServersChanged += RefreshServerList;

        if (settingsSavedText) settingsSavedText.gameObject.SetActive(false);

        if (RTSNetworkManager.s_returnToJoinPanel)
        {
            RTSNetworkManager.s_returnToJoinPanel = false;
            ShowJoinPanel();
        }
        else
        {
            ShowMain();
        }
    }

    private void OnDestroy()
    {
        if (LANDiscovery.Instance != null)
            LANDiscovery.Instance.OnServersChanged -= RefreshServerList;
    }

    private void Update()
    {
        if (settingsSavedTimer > 0f)
        {
            settingsSavedTimer -= Time.unscaledDeltaTime;
            if (settingsSavedTimer <= 0f && settingsSavedText)
                settingsSavedText.gameObject.SetActive(false);
        }
    }

    private void WireButtons()
    {
        startHostButton?.onClick.AddListener(OnStartHost);
        connectDirectButton?.onClick.AddListener(OnConnectDirect);
        refreshBrowserButton?.onClick.AddListener(OnRefreshBrowser);
        saveSettingsButton?.onClick.AddListener(OnSaveSettings);
    }

    // ── Panel Navigation ──────────────────────────────────────────────

    public void ShowMain()
    {
        HideAll();
        mainPanel?.SetActive(true);
    }

    public void ShowPlay()
    {
        HideAll();
        playPanel?.SetActive(true);
    }

    public void ShowHostPanel()
    {
        HideAll();
        hostPanel?.SetActive(true);

        bool hasNet = HasNetworkConnection();
        if (startHostButton) startHostButton.interactable = true;
        if (noWifiWarning)   noWifiWarning.SetActive(!hasNet);

        if (hostIPText) hostIPText.text = $"Your IP:  {LANDiscovery.GetLocalIP()}";

        SetHostStatus(hasNet
            ? "Configure options then press Host Game."
            : "No WiFi detected. You may still be able to host a local server.");
    }

    public void ShowJoinPanel()
    {
        HideAll();
        joinPanel?.SetActive(true);
        directIPTab?.SetActive(true);
        browserTab?.SetActive(false);
        SetJoinStatus("");
        RefreshServerList();
        LANDiscovery.Instance?.StartBrowsing();
    }

    public void ShowSettings()
    {
        HideAll();
        settingsPanel?.SetActive(true);
        LoadSettingsIntoUI();
    }

    /// <summary>Called by LobbyUI when it takes over the screen.</summary>
    public void HideAllPanels() => HideAll();

    private void HideAll()
    {
        mainPanel?.SetActive(false);
        playPanel?.SetActive(false);
        hostPanel?.SetActive(false);
        joinPanel?.SetActive(false);
        settingsPanel?.SetActive(false);
        LANDiscovery.Instance?.StopBrowsing();
    }

    // ── Host ─────────────────────────────────────────────────────────

    public void OnStartHost()
    {
        if (RTSNetworkManager.Instance == null)
        {
            SetHostStatus("❌ NetworkManager not found in scene!");
            return;
        }

        int maxPlayers = (maxPlayersDropdown != null) ? maxPlayersDropdown.value + 1 : 2;
        RTSNetworkManager.Instance.maxConnections  = maxPlayers;
        RTSNetworkManager.Instance.requiredPlayers = maxPlayers > 1 ? 2 : 1;

        LoadingScreen.Instance?.Show("Starting server...", "Opening lobby on local network");
        StartCoroutine(HideLoadingAfterTimeout(5f));

        if (startHostButton) startHostButton.interactable = false;
        SetHostStatus("Starting lobby...");

        try
        {
            RTSNetworkManager.Instance.StartHost();
        }
        catch (System.Exception e)
        {
            LoadingScreen.Instance?.Hide();
            if (startHostButton) startHostButton.interactable = true;
            SetHostStatus($"❌ Host failed: {e.Message}");
            return;
        }

        string hostName = SettingsManager.Instance?.PlayerName ?? "Host";
        try
        {
            LANDiscovery.Instance?.StartAdvertising(hostName, 1, maxPlayers);
        }
        catch { }
    }

    private IEnumerator HideLoadingAfterTimeout(float timeout)
    {
        yield return new WaitForSecondsRealtime(timeout);
        LoadingScreen.Instance?.Hide();
    }

    /// <summary>Called by RTSNetworkManager once the host is confirmed running.</summary>
    public void OnHostStarted(string ip)
    {
        LoadingScreen.Instance?.Hide();
        if (hostIPText) hostIPText.text = $"Your IP:  {ip}";
        SetHostStatus("✅ Lobby running. Share your IP with friends.");
    }

    /// <summary>Called by RTSNetworkManager if hosting fails.</summary>
    public void OnHostFailed(string reason)
    {
        LoadingScreen.Instance?.Hide();
        if (startHostButton) startHostButton.interactable = true;
        SetHostStatus($"❌ Host failed: {reason}");
        LANDiscovery.Instance?.StopAdvertising();
    }

    public void OnBackFromHost()
    {
        if (Mirror.NetworkServer.active)
        {
            RTSNetworkManager.Instance?.StopHost();
            LANDiscovery.Instance?.StopAdvertising();
        }
        if (startHostButton) startHostButton.interactable = true;
        ShowPlay();
    }

    // ── Join ─────────────────────────────────────────────────────────

    public void OnConnectDirect()
    {
        string ip = directIPInput != null ? directIPInput.text.Trim() : "";
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
        AttemptConnection(ip);
    }

    private void AttemptConnection(string ip)
    {
        if (RTSNetworkManager.Instance == null)
        {
            SetJoinStatus("❌ NetworkManager not found in scene!");
            return;
        }

        LoadingScreen.Instance?.Show("Connecting...", $"Attempting to join {ip}");
        SetJoinStatus($"Connecting to {ip}...");

        if (connectDirectButton) connectDirectButton.interactable = false;

        RTSNetworkManager.Instance.networkAddress = ip;
        RTSNetworkManager.Instance.StartClient();
    }

    /// <summary>Called by RTSNetworkManager when a connection attempt fails.</summary>
    public void OnConnectFailed(string reason)
    {
        LoadingScreen.Instance?.Hide();
        SetJoinStatus($"❌ {reason}");
        if (connectDirectButton) connectDirectButton.interactable = true;
    }

    public void OnBackFromJoin()
    {
        if (Mirror.NetworkClient.active)
            RTSNetworkManager.Instance?.StopClient();
        LANDiscovery.Instance?.StopBrowsing();
        if (connectDirectButton) connectDirectButton.interactable = true;
        ShowPlay();
    }

    // ── Server Browser ────────────────────────────────────────────────

    public void ShowJoinTab(bool showDirect)
    {
        directIPTab?.SetActive(showDirect);
        browserTab?.SetActive(!showDirect);
        RefreshServerList();
    }

    public void OnRefreshBrowser()
    {
        LANDiscovery.Instance?.StopBrowsing();
        LANDiscovery.Instance?.StartBrowsing();
        RefreshServerList();
    }

    private void RefreshServerList()
    {
        if (serverListContainer == null || serverRowPrefab == null) return;

        foreach (Transform child in serverListContainer)
            Destroy(child.gameObject);

        var  servers = LANDiscovery.Instance?.Servers;
        bool empty   = servers == null || servers.Count == 0;

        if (noBrowserServersText)
            noBrowserServersText.gameObject.SetActive(empty);

        if (empty) return;

        for (int i = 0; i < servers.Count; i++)
        {
            ServerEntry entry  = servers[i];
            string      joinIP = entry.ip;

            GameObject row = Instantiate(serverRowPrefab, serverListContainer);

            var nameT = row.transform.Find("NameText")?.GetComponent<TMP_Text>();
            var playT = row.transform.Find("PlayersText")?.GetComponent<TMP_Text>();
            if (nameT) nameT.text = entry.hostName;
            if (playT) playT.text = $"{entry.currentPlayers}/{entry.maxPlayers}";

            var btn = row.GetComponent<Button>();
            btn?.onClick.AddListener(() => AttemptConnection(joinIP));
        }
    }

    // ── Settings ─────────────────────────────────────────────────────

    private void SetupMaxPlayersDropdown()
    {
        if (maxPlayersDropdown == null) return;
        maxPlayersDropdown.ClearOptions();
        var opts = new List<string>();
        for (int i = 1; i <= 8; i++) opts.Add($"{i} Player{(i > 1 ? "s" : "")}");
        maxPlayersDropdown.AddOptions(opts);
        maxPlayersDropdown.value = 1; // default: 2 Players
    }

    private void SetupQualityDropdown()
    {
        if (qualityDropdown == null) return;
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(new List<string>(QualitySettings.names));
    }

    private void SetupResolutionDropdown()
    {
        if (resolutionDropdown == null) return;
        resolutionDropdown.ClearOptions();

        var unique = new List<Resolution>();
        var opts = new List<string>();
        Resolution[] all = Screen.resolutions;

        for (int i = all.Length - 1; i >= 0; i--)
        {
            bool dup = false;
            foreach (var u in unique)
                if (u.width == all[i].width && u.height == all[i].height)
                { dup = true; break; }
            if (!dup)
            {
                unique.Add(all[i]);
                opts.Add($"{all[i].width}x{all[i].height}");
            }
        }

        resolutionDropdown.AddOptions(opts);

        string saved = SettingsManager.Instance?.ResolutionString ?? "";
        int match = -1;
        if (!string.IsNullOrEmpty(saved))
        {
            for (int i = 0; i < opts.Count; i++)
            {
                if (opts[i] == saved)
                { match = i; break; }
            }
        }
        if (match < 0)
        {
            Resolution cur = Screen.currentResolution;
            for (int i = 0; i < unique.Count; i++)
            {
                if (unique[i].width == cur.width && unique[i].height == cur.height)
                { match = i; break; }
            }
        }
        resolutionDropdown.value = match >= 0 ? match : 0;
        resolutionDropdown.RefreshShownValue();
    }

    private void LoadSettingsIntoUI()
    {
        if (playerNameInput) playerNameInput.text = SettingsManager.Instance?.PlayerName ?? "Player";
        if (qualityDropdown) qualityDropdown.value = SettingsManager.Instance?.GraphicsQuality ?? 2;
        if (fullscreenToggle) fullscreenToggle.isOn = SettingsManager.Instance?.IsFullscreen ?? Screen.fullScreen;
        if (resolutionDropdown && SettingsManager.Instance != null && !string.IsNullOrEmpty(SettingsManager.Instance.ResolutionString))
        {
            string target = SettingsManager.Instance.ResolutionString;
            for (int i = 0; i < resolutionDropdown.options.Count; i++)
            {
                if (resolutionDropdown.options[i].text == target)
                { resolutionDropdown.value = i; break; }
            }
        }
    }

    public void OnSaveSettings()
    {
        if (SettingsManager.Instance == null) return;
        if (playerNameInput) SettingsManager.Instance.SavePlayerName(playerNameInput.text);
        if (qualityDropdown) SettingsManager.Instance.SaveGraphicsQuality(qualityDropdown.value);
        if (resolutionDropdown)
        {
            string label = resolutionDropdown.options[resolutionDropdown.value].text;
            SettingsManager.Instance.SaveResolution(label);
        }
        if (fullscreenToggle) SettingsManager.Instance.SaveFullscreen(fullscreenToggle.isOn);

        if (settingsSavedText)
        {
            settingsSavedText.gameObject.SetActive(true);
            settingsSavedText.text = "Settings saved!";
            settingsSavedTimer = SAVED_DURATION;
        }
    }

    // ── Single Player ─────────────────────────────────────────────────

    public void OnStartSinglePlayer()
    {
        if (RTSNetworkManager.Instance == null)
        {
            Debug.LogError("[MainMenuUI] RTSNetworkManager not found in scene!");
            return;
        }

        GameModeManager.FindOrCreate().currentMode = GameModeManager.GameMode.SinglePlayer;
        QualitySettings.shadowDistance = 80f;
        RTSNetworkManager.Instance.maxConnections  = 1;
        RTSNetworkManager.Instance.requiredPlayers = 1;
        RTSNetworkManager.Instance.skipLobbyAndCountdown = true;
        RTSNetworkManager.Instance.StartHost();
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void SetHostStatus(string msg) { if (hostStatusText) hostStatusText.text = msg; }
    private void SetJoinStatus(string msg) { if (joinStatusText) joinStatusText.text = msg; }

    /// <summary>Returns true if any non-loopback network interface is up (WiFi or Ethernet).</summary>
    private bool HasNetworkConnection()
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                return true;
            }
        }
        catch { }
        return false;
    }
    public void QuitGame()
    {
        Application.Quit();
    #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
    #endif
    }
}
