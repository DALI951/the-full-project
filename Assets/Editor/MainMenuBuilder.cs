// MainMenuBuilder — generates the full MainMenu scene per MainMenu_Architecture.md
// Run via Tools → Build MainMenu Scene in the Unity Editor.
// Prerequisites: Mirror + KCP Transport must be installed.
// After build, manually assign: Transport, playerPrefab, playerRowPrefab, serverRowPrefab.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class MainMenuBuilder
{
    // ── Color palette ──────────────────────────────────────────
    static readonly Color bg      = Hex("#0B0B17");
    static readonly Color panel   = Hex("#101428");
    static readonly Color btnBlue = Hex("#1E50A0");
    static readonly Color btnGray = Hex("#44445F");
    static readonly Color btnRed  = Hex("#E94560");
    static readonly Color txt     = Hex("#DCDCF0");
    static readonly Color muted   = Hex("#888899");
    static readonly Color inputBg = Hex("#090D20");
    static readonly Color green   = Hex("#3FC880");

    static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString(h, out var c);
        return c;
    }

    // ── Entry point ────────────────────────────────────────────
    [MenuItem("Tools/Build MainMenu Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        scene.name = "MainMenu";

        foreach (var go in scene.GetRootGameObjects())
            Object.DestroyImmediate(go);

        CreateEventSystem();
        CreateSettingsManager();
        CreateLANDiscovery();
        CreateNetworkManager();
        CreateLoadingCanvas();
        CreateMainMenuCanvas();
        CreateLobbyCanvas();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/MainMenu.unity");
        Debug.Log("[MainMenuBuilder] Scene saved to Assets/Scenes/MainMenu.unity");
    }

    // ════════════════════════════════════════════════════════════
    //  SCENE OBJECTS
    // ════════════════════════════════════════════════════════════

    static void CreateEventSystem()
    {
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    static void CreateSettingsManager()
    {
        var go = new GameObject("SettingsManager");
        go.AddComponent<SettingsManager>();
    }

    static void CreateLANDiscovery()
    {
        var go = new GameObject("LANDiscovery");
        go.AddComponent<LANDiscovery>();
    }

    static void CreateNetworkManager()
    {
        var go = new GameObject("RTSNetworkManager");
        var nm = go.AddComponent<RTSNetworkManager>();
        nm.offlineScene = "Assets/Scenes/MainMenu.unity";
        nm.onlineScene = "Assets/Scenes/GameScene.unity";
        nm.networkAddress = "localhost";
        nm.maxConnections = 8;
    }

    // ════════════════════════════════════════════════════════════
    //  LOADING CANVAS  (Sort Order 99)
    // ════════════════════════════════════════════════════════════

    static void CreateLoadingCanvas()
    {
        var canvas = CreateCanvasRoot("LoadingCanvas", 99);
        var ls = canvas.gameObject.AddComponent<LoadingScreen>();

        var loadingPanel = CreateImage("LoadingPanel", canvas.transform,
            new Color(0, 0, 0, 220f / 255f));
        Stretch(loadingPanel);
        loadingPanel.gameObject.SetActive(false);

        var msgText = CreateTmp("MessageText", loadingPanel.transform,
            "Loading...", 36, FontStyles.Bold, txt);
        SetRect(msgText.rectTransform, 700, 60, 0, 30);

        var subText = CreateTmp("SubText", loadingPanel.transform,
            "", 20, FontStyles.Normal, muted);
        SetRect(subText.rectTransform, 700, 40, 0, -30);

        var so = new SerializedObject(ls);
        so.FindProperty("panel").objectReferenceValue = loadingPanel;
        so.FindProperty("messageText").objectReferenceValue = msgText;
        so.FindProperty("subText").objectReferenceValue = subText;
        so.ApplyModifiedProperties();
    }

    // ════════════════════════════════════════════════════════════
    //  MAIN MENU CANVAS  (Sort Order 0)
    // ════════════════════════════════════════════════════════════

    static void CreateMainMenuCanvas()
    {
        var canvas = CreateCanvasRoot("MainMenuCanvas", 0);
        var mainMenu = canvas.gameObject.AddComponent<MainMenuUI>();

        // Full-screen background
        var bgImage = CreateImage("Background", canvas.transform, bg);
        Stretch(bgImage);

        // ── MainPanel ──────────────────────────────────────────
        var mainPanel = CreateImage("MainPanel", canvas.transform,
            new Color(0, 0, 0, 180f / 255f));
        Stretch(mainPanel);

        var mainBox = CreateImage("Box", mainPanel.transform, panel);
        SetRect(mainBox, 400, 510, 0, 0);

        var vlg = mainBox.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 10;
        vlg.padding = new RectOffset(26, 26, 28, 28);
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var title = CreateTmp("TitleText", mainBox.transform,
            "\u2694  RTS GAME", 46, FontStyles.Bold, txt);
        AddLayoutElement(title.gameObject, -1, 70);

        AddSpace(mainBox.transform, 18);

        var btnSingle = CreateMenuButton(mainBox.transform, "BtnSingle",
            "\u25B6  Single Player", btnBlue, new Color32(0x33, 0x73, 0xD0, 0xFF), new Color32(0x0E, 0x30, 0x70, 0xFF), 58);
        btnSingle.onClick.AddListener(mainMenu.OnStartSinglePlayer);

        var btnPlay = CreateMenuButton(mainBox.transform, "BtnPlay",
            "\u2694  Multiplayer", btnBlue, new Color32(0x33, 0x73, 0xD0, 0xFF), new Color32(0x0E, 0x30, 0x70, 0xFF), 58);
        btnPlay.onClick.AddListener(mainMenu.ShowPlay);

        AddSpace(mainBox.transform, 6);

        var btnSettings = CreateMenuButton(mainBox.transform, "BtnSettings",
            "\u2699  Settings", btnGray, Hex("#666680"), Hex("#2A2A45"), 52);
        btnSettings.onClick.AddListener(mainMenu.ShowSettings);

        var btnQuit = CreateMenuButton(mainBox.transform, "BtnQuit",
            "\u2715  Quit", btnRed, Hex("#FF6070"), Hex("#B52040"), 52);
        btnQuit.onClick.AddListener(mainMenu.QuitGame);

        // ── PlayPanel ──────────────────────────────────────────
        var playPanel = CreateImage("PlayPanel", canvas.transform,
            new Color(0, 0, 0, 180f / 255f));
        Stretch(playPanel);
        playPanel.gameObject.SetActive(false);

        var playBox = CreateImage("Box", playPanel.transform, panel);
        SetRect(playBox, 400, 360, 0, 0);
        var playVlg = playBox.gameObject.AddComponent<VerticalLayoutGroup>();
        playVlg.childAlignment = TextAnchor.UpperCenter;
        playVlg.spacing = 10;
        playVlg.padding = new RectOffset(26, 26, 28, 28);
        playVlg.childControlWidth = true;
        playVlg.childControlHeight = false;
        playVlg.childForceExpandWidth = true;
        playVlg.childForceExpandHeight = false;

        var playTitle = CreateTmp("TitleText", playBox.transform,
            "MULTIPLAYER", 30, FontStyles.Bold, txt);
        AddLayoutElement(playTitle.gameObject, -1, 55);

        AddSpace(playBox.transform, 20);

        var btnHost = CreateMenuButton(playBox.transform, "BtnHost",
            "\U0001F3E0  Host Game", btnBlue, new Color32(0x33, 0x73, 0xD0, 0xFF), new Color32(0x0E, 0x30, 0x70, 0xFF), 62);
        btnHost.onClick.AddListener(mainMenu.ShowHostPanel);

        var btnJoin = CreateMenuButton(playBox.transform, "BtnJoin",
            "\U0001F517  Join Game", btnBlue, new Color32(0x33, 0x73, 0xD0, 0xFF), new Color32(0x0E, 0x30, 0x70, 0xFF), 62);
        btnJoin.onClick.AddListener(mainMenu.ShowJoinPanel);

        AddSpace(playBox.transform, 8);

        var playBack = CreateMenuButton(playBox.transform, "BtnBack",
            "\u2190  Back", btnGray, Hex("#666680"), Hex("#2A2A45"), 48);
        playBack.onClick.AddListener(mainMenu.ShowMain);

        // ── HostPanel ──────────────────────────────────────────
        var hostPanel = CreateImage("HostPanel", canvas.transform,
            new Color(0, 0, 0, 180f / 255f));
        Stretch(hostPanel);
        hostPanel.gameObject.SetActive(false);

        var hostBox = CreateImage("Box", hostPanel.transform, panel);
        SetRect(hostBox, 520, 460, 0, 0);
        var hostVlg = hostBox.gameObject.AddComponent<VerticalLayoutGroup>();
        hostVlg.childAlignment = TextAnchor.UpperCenter;
        hostVlg.spacing = 6;
        hostVlg.padding = new RectOffset(26, 26, 22, 22);
        hostVlg.childControlWidth = true;
        hostVlg.childControlHeight = false;
        hostVlg.childForceExpandWidth = true;
        hostVlg.childForceExpandHeight = false;

        var hostTitle = CreateTmp("TitleText", hostBox.transform,
            "HOST GAME", 30, FontStyles.Bold, txt);
        AddLayoutElement(hostTitle.gameObject, -1, 55);

        AddSpace(hostBox.transform, 8);

        var noWifi = CreateTmp("NoWifiWarning", hostBox.transform,
            "\u26A0  No network connection \u2014 cannot host.", 15, FontStyles.Normal, btnRed);
        AddLayoutElement(noWifi.gameObject, -1, 34);
        noWifi.gameObject.SetActive(false);

        var ipRow = CreateRow(hostBox.transform, "IPRow", 40);
        var ipLabel = CreateTmp("L", ipRow.transform, "Your IP:", 15, FontStyles.Normal, muted);
        AddLayoutElement(ipLabel.gameObject, 150, -1);
        ipLabel.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(ipLabel.rectTransform);

        var hostIPText = CreateTmp("IPValue", ipRow.transform, "\u2014", 15, FontStyles.Bold, txt);
        AddLayoutElement(hostIPText.gameObject, 240, -1);
        hostIPText.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(hostIPText.rectTransform);

        var maxRow = CreateRow(hostBox.transform, "MaxRow", 46);
        var maxLabel = CreateTmp("L", maxRow.transform, "Max Players:", 15, FontStyles.Normal, muted);
        AddLayoutElement(maxLabel.gameObject, 150, -1);
        maxLabel.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(maxLabel.rectTransform);

        var maxDropdown = CreateDropdown("MaxDD", maxRow.transform);

        AddSpace(hostBox.transform, 10);

        var startHostBtn = CreateMenuButton(hostBox.transform, "BtnStartHost",
            "\u25B6  Host Game", btnBlue, new Color32(0x33, 0x73, 0xD0, 0xFF), new Color32(0x0E, 0x30, 0x70, 0xFF), 60);

        var hostStatus = CreateTmp("HostStatus", hostBox.transform,
            "Configure then press Host Game.", 13, FontStyles.Normal, muted);
        AddLayoutElement(hostStatus.gameObject, -1, 34);
        hostStatus.alignment = TextAlignmentOptions.Center;

        var hostBack = CreateMenuButton(hostBox.transform, "BtnBack",
            "\u2190  Back", btnGray, Hex("#666680"), Hex("#2A2A45"), 48);
        hostBack.onClick.AddListener(mainMenu.OnBackFromHost);

        // ── JoinPanel ──────────────────────────────────────────
        var joinPanel = CreateImage("JoinPanel", canvas.transform,
            new Color(0, 0, 0, 180f / 255f));
        Stretch(joinPanel);
        joinPanel.gameObject.SetActive(false);

        var joinBox = CreateImage("Box", joinPanel.transform, panel);
        SetRect(joinBox, 580, 530, 0, 0);
        var joinVlg = joinBox.gameObject.AddComponent<VerticalLayoutGroup>();
        joinVlg.childAlignment = TextAnchor.UpperCenter;
        joinVlg.spacing = 6;
        joinVlg.padding = new RectOffset(26, 26, 22, 22);
        joinVlg.childControlWidth = true;
        joinVlg.childControlHeight = false;
        joinVlg.childForceExpandWidth = true;
        joinVlg.childForceExpandHeight = false;

        var joinTitle = CreateTmp("TitleText", joinBox.transform,
            "JOIN GAME", 30, FontStyles.Bold, txt);
        AddLayoutElement(joinTitle.gameObject, -1, 55);

        AddSpace(joinBox.transform, 6);

        // Tab row
        var tabRow = CreateRect("TabRow", joinBox.transform);
        AddLayoutElement(tabRow.gameObject, -1, 48);
        var tabHLG = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabHLG.spacing = 4;
        tabHLG.childControlWidth = true;
        tabHLG.childControlHeight = true;
        tabHLG.childForceExpandWidth = true;
        tabHLG.childForceExpandHeight = true;

        var directTabBtn = CreateTabButton(tabRow.transform, "BtnDirectTab",
            "Direct IP", btnBlue);
        var browserTabBtn = CreateTabButton(tabRow.transform, "BtnBrowserTab",
            "Server Browser", btnGray);

        directTabBtn.onClick.AddListener(() => mainMenu.ShowJoinTab(true));
        browserTabBtn.onClick.AddListener(() => mainMenu.ShowJoinTab(false));

        // Direct IP tab
        var directIPTab = CreateRect("DirectIPTab", joinBox.transform);
        AddLayoutElement(directIPTab.gameObject, -1, 140);
        var dipVlg = directIPTab.gameObject.AddComponent<VerticalLayoutGroup>();
        dipVlg.spacing = 8;
        dipVlg.childControlWidth = true;
        dipVlg.childControlHeight = false;
        dipVlg.childForceExpandWidth = true;

        var dipLabel = CreateTmp("Lbl", directIPTab.transform,
            "Enter host IP address:", 14, FontStyles.Normal, muted);
        AddLayoutElement(dipLabel.gameObject, -1, 28);

        var directIPInput = CreateInputField("DirectIPInput", directIPTab.transform);
        AddLayoutElement(directIPInput.gameObject, -1, 46);

        var connectBtn = CreateMenuButton(directIPTab.transform, "BtnConnect",
            "\U0001F517  Connect", btnBlue, new Color32(0x33, 0x73, 0xD0, 0xFF), new Color32(0x0E, 0x30, 0x70, 0xFF), 54);

        // Browser tab
        var browserTab = CreateRect("BrowserTab", joinBox.transform);
        AddLayoutElement(browserTab.gameObject, -1, 140);
        var brVlg = browserTab.gameObject.AddComponent<VerticalLayoutGroup>();
        brVlg.spacing = 6;
        brVlg.childControlWidth = true;
        brVlg.childControlHeight = false;
        brVlg.childForceExpandWidth = true;
        browserTab.gameObject.SetActive(false);

        var scrollView = CreateScrollView("ServerScrollView", browserTab.transform);
        AddLayoutElement(scrollView.gameObject, -1, 80);

        var refreshBtn = CreateMenuButton(browserTab.transform, "BtnRefresh",
            "\u21BB  Refresh", btnGray, Hex("#666680"), Hex("#2A2A45"), 40);

        var noBrowserText = CreateTmp("NoBrowserText", browserTab.transform,
            "No games found on local network.", 14, FontStyles.Normal, muted);
        AddLayoutElement(noBrowserText.gameObject, -1, 28);
        noBrowserText.alignment = TextAlignmentOptions.Center;

        AddSpace(joinBox.transform, 4);

        var joinStatus = CreateTmp("JoinStatus", joinBox.transform,
            "", 13, FontStyles.Normal, muted);
        AddLayoutElement(joinStatus.gameObject, -1, 30);
        joinStatus.alignment = TextAlignmentOptions.Center;

        var joinBack = CreateMenuButton(joinBox.transform, "BtnBack",
            "\u2190  Back", btnGray, Hex("#666680"), Hex("#2A2A45"), 48);
        joinBack.onClick.AddListener(mainMenu.OnBackFromJoin);

        // ── SettingsPanel ───────────────────────────────────────
        var settingsPanel = CreateImage("SettingsPanel", canvas.transform,
            new Color(0, 0, 0, 180f / 255f));
        Stretch(settingsPanel);
        settingsPanel.gameObject.SetActive(false);

        var settingsBox = CreateImage("Box", settingsPanel.transform, panel);
        SetRect(settingsBox, 500, 420, 0, 0);
        var sVlg = settingsBox.gameObject.AddComponent<VerticalLayoutGroup>();
        sVlg.childAlignment = TextAnchor.UpperCenter;
        sVlg.spacing = 6;
        sVlg.padding = new RectOffset(26, 26, 22, 22);
        sVlg.childControlWidth = true;
        sVlg.childControlHeight = false;
        sVlg.childForceExpandWidth = true;
        sVlg.childForceExpandHeight = false;

        var settingsTitle = CreateTmp("TitleText", settingsBox.transform,
            "SETTINGS", 30, FontStyles.Bold, txt);
        AddLayoutElement(settingsTitle.gameObject, -1, 55);

        AddSpace(settingsBox.transform, 14);

        var nameRow = CreateRow(settingsBox.transform, "NameRow", 46);
        var nameLabel = CreateTmp("L", nameRow.transform, "Player Name:", 15, FontStyles.Normal, muted);
        AddLayoutElement(nameLabel.gameObject, 150, -1);
        nameLabel.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(nameLabel.rectTransform);

        var playerNameInput = CreateInputField("NameInput", nameRow.transform);

        var qualRow = CreateRow(settingsBox.transform, "QualRow", 46);
        var qualLabel = CreateTmp("L", qualRow.transform, "Graphics:", 15, FontStyles.Normal, muted);
        AddLayoutElement(qualLabel.gameObject, 150, -1);
        qualLabel.alignment = TextAlignmentOptions.MidlineLeft;
        Stretch(qualLabel.rectTransform);

        var qualityDropdown = CreateDropdown("QualityDD", qualRow.transform);

        AddSpace(settingsBox.transform, 16);

        var saveBtn = CreateMenuButton(settingsBox.transform, "BtnSave",
            "\U0001F4BE  Save Settings", btnBlue, new Color32(0x33, 0x73, 0xD0, 0xFF), new Color32(0x0E, 0x30, 0x70, 0xFF), 58);

        var savedText = CreateTmp("SavedText", settingsBox.transform,
            "\u2713 Settings saved!", 14, FontStyles.Normal, green);
        AddLayoutElement(savedText.gameObject, -1, 28);
        savedText.alignment = TextAlignmentOptions.Center;
        savedText.gameObject.SetActive(false);

        AddSpace(settingsBox.transform, 4);

        var settingsBack = CreateMenuButton(settingsBox.transform, "BtnBack",
            "\u2190  Back", btnGray, Hex("#666680"), Hex("#2A2A45"), 48);
        settingsBack.onClick.AddListener(mainMenu.ShowMain);

        // ── Wire MainMenuUI ─────────────────────────────────────
        var so = new SerializedObject(mainMenu);
        so.FindProperty("mainPanel").objectReferenceValue = mainPanel;
        so.FindProperty("playPanel").objectReferenceValue = playPanel;
        so.FindProperty("hostPanel").objectReferenceValue = hostPanel;
        so.FindProperty("joinPanel").objectReferenceValue = joinPanel;
        so.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;

        so.FindProperty("hostIPText").objectReferenceValue = hostIPText;
        so.FindProperty("maxPlayersDropdown").objectReferenceValue = maxDropdown;
        so.FindProperty("startHostButton").objectReferenceValue = startHostBtn;
        so.FindProperty("hostStatusText").objectReferenceValue = hostStatus;
        so.FindProperty("noWifiWarning").objectReferenceValue = noWifi.gameObject;

        so.FindProperty("directTabButton").objectReferenceValue = directTabBtn;
        so.FindProperty("browserTabButton").objectReferenceValue = browserTabBtn;
        so.FindProperty("directIPTab").objectReferenceValue = directIPTab.gameObject;
        so.FindProperty("browserTab").objectReferenceValue = browserTab.gameObject;
        so.FindProperty("directIPInput").objectReferenceValue = directIPInput;
        so.FindProperty("connectDirectButton").objectReferenceValue = connectBtn;
        so.FindProperty("serverListContainer").objectReferenceValue = GetScrollContent(scrollView);
        so.FindProperty("refreshBrowserButton").objectReferenceValue = refreshBtn;
        so.FindProperty("noBrowserServersText").objectReferenceValue = noBrowserText;
        so.FindProperty("joinStatusText").objectReferenceValue = joinStatus;

        so.FindProperty("playerNameInput").objectReferenceValue = playerNameInput;
        so.FindProperty("qualityDropdown").objectReferenceValue = qualityDropdown;
        so.FindProperty("saveSettingsButton").objectReferenceValue = saveBtn;
        so.FindProperty("settingsSavedText").objectReferenceValue = savedText;
        so.ApplyModifiedProperties();
    }

    // ════════════════════════════════════════════════════════════
    //  LOBBY CANVAS  (Sort Order 1)
    // ════════════════════════════════════════════════════════════

    static void CreateLobbyCanvas()
    {
        var canvas = CreateCanvasRoot("LobbyCanvas", 1);
        var lobbyUI = canvas.gameObject.AddComponent<LobbyUI>();

        var bgImage = CreateImage("Background", canvas.transform, bg);
        Stretch(bgImage);

        var lobbyPanel = CreateRect("LobbyPanel", canvas.transform);
        Stretch(lobbyPanel);
        lobbyPanel.gameObject.SetActive(false);

        // Header
        var header = CreateImage("Header", lobbyPanel.transform, panel);
        SetRect(header, 0, 90, 0, 0, TextAnchor.UpperCenter);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(0, -90);
        header.offsetMax = new Vector2(0, 0);

        var headerTitle = CreateTmp("Title", header.transform,
            "\u2694  LOBBY", 30, FontStyles.Bold, txt);
        Stretch(headerTitle.rectTransform);

        // Player list scroll view
        var scrollView = CreateScrollView("PlayerListScrollView", lobbyPanel.transform);
        scrollView.anchorMin = new Vector2(0.01f, 0.12f);
        scrollView.anchorMax = new Vector2(0.99f, 0.92f);
        scrollView.offsetMin = Vector2.zero;
        scrollView.offsetMax = Vector2.zero;

        var content = GetScrollContent(scrollView);
        content.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 4;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Color picker
        var colorPickerPanel = CreateImage("ColorPickerPanel", lobbyPanel.transform, panel);
        SetRect(colorPickerPanel, 440, 78, 0, 80);
        var cpHlg = colorPickerPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
        cpHlg.childAlignment = TextAnchor.MiddleCenter;
        cpHlg.spacing = 8;
        cpHlg.padding = new RectOffset(10, 10, 10, 10);
        cpHlg.childControlWidth = false;
        cpHlg.childControlHeight = true;
        cpHlg.childForceExpandHeight = true;
        colorPickerPanel.gameObject.SetActive(false);

        Color[] chipColors = {
            Hex("#00FFFF"), Hex("#FF0000"), Hex("#FF8000"), Hex("#00FF00"),
            Hex("#FF00FF"), Hex("#FFFF00"), Hex("#8000FF"), Hex("#FFFFFF")
        };
        var colorPickerBtns = new Button[8];
        for (int i = 0; i < 8; i++)
            colorPickerBtns[i] = CreateColorChip($"ColorButton{i}", colorPickerPanel.transform, chipColors[i]);

        // Countdown group
        var countdownGroup = CreateImage("CountdownGroup", lobbyPanel.transform,
            new Color(0, 0, 0, 224f / 255f));
        SetRect(countdownGroup, 320, 230, 0, 0);
        var cdVlg = countdownGroup.gameObject.AddComponent<VerticalLayoutGroup>();
        cdVlg.childAlignment = TextAnchor.MiddleCenter;
        cdVlg.spacing = 12;
        cdVlg.padding = new RectOffset(20, 20, 20, 20);
        cdVlg.childControlWidth = true;
        cdVlg.childControlHeight = false;
        cdVlg.childForceExpandWidth = true;
        cdVlg.childForceExpandHeight = false;
        countdownGroup.gameObject.SetActive(false);

        var cdLabel = CreateTmp("CountdownLabel", countdownGroup.transform,
            "Match starting in", 17, FontStyles.Normal, muted);
        AddLayoutElement(cdLabel.gameObject, -1, 28);
        cdLabel.alignment = TextAlignmentOptions.Center;

        var cdText = CreateTmp("CountdownText", countdownGroup.transform,
            "5", 80, FontStyles.Bold, btnRed);
        AddLayoutElement(cdText.gameObject, -1, 100);
        cdText.alignment = TextAlignmentOptions.Center;

        var cancelBtn = CreateMenuButton(countdownGroup.transform, "CancelCountdownButton",
            "Cancel Countdown", btnRed, Hex("#FF6070"), Hex("#B52040"), 46);

        // Bottom bar
        var bottomBar = CreateImage("BottomBar", lobbyPanel.transform, panel);
        bottomBar.anchorMin = new Vector2(0, 0);
        bottomBar.anchorMax = new Vector2(1, 0);
        bottomBar.pivot = new Vector2(0.5f, 0);
        bottomBar.offsetMin = new Vector2(0, 0);
        bottomBar.offsetMax = new Vector2(0, 90);

        var bbHlg = bottomBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        bbHlg.childAlignment = TextAnchor.MiddleCenter;
        bbHlg.spacing = 16;
        bbHlg.padding = new RectOffset(20, 20, 8, 8);
        bbHlg.childControlHeight = true;
        bbHlg.childControlWidth = false;
        bbHlg.childForceExpandHeight = true;

        var statusText = CreateTmp("StatusText", bottomBar.transform,
            "Waiting for players...", 16, FontStyles.Normal, muted);
        AddLayoutElement(statusText.gameObject, 500, -1, 1);
        statusText.alignment = TextAlignmentOptions.MidlineLeft;

        var leaveBtn = CreateMenuButton(bottomBar.transform, "LeaveButton",
            "Leave Lobby", btnRed, Hex("#FF6070"), Hex("#B52040"), 0);
        AddLayoutElement(leaveBtn.gameObject, 180, -1);

        // ── Wire LobbyUI ────────────────────────────────────────
        var so = new SerializedObject(lobbyUI);
        so.FindProperty("lobbyPanel").objectReferenceValue = lobbyPanel.gameObject;
        so.FindProperty("playerListContainer").objectReferenceValue = content;
        so.FindProperty("countdownGroup").objectReferenceValue = countdownGroup.gameObject;
        so.FindProperty("countdownText").objectReferenceValue = cdText;
        so.FindProperty("cancelCountdownButton").objectReferenceValue = cancelBtn;
        so.FindProperty("statusText").objectReferenceValue = statusText;
        so.FindProperty("leaveButton").objectReferenceValue = leaveBtn;
        so.FindProperty("colorPickerPanel").objectReferenceValue = colorPickerPanel.gameObject;

        var cbArray = so.FindProperty("colorButtons");
        cbArray.ClearArray();
        cbArray.arraySize = 8;
        for (int i = 0; i < 8; i++)
            cbArray.GetArrayElementAtIndex(i).objectReferenceValue = colorPickerBtns[i];
        so.ApplyModifiedProperties();
    }

    // ════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════

    static GameObject CreateCanvasRoot(string name, int sortOrder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static RectTransform CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return go.GetComponent<RectTransform>();
    }

    static TextMeshProUGUI CreateTmp(string name, Transform parent,
        string text, float fontSize, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    static void SetRect(RectTransform rt, float w, float h, float x, float y)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector3(x, y);
    }

    static void SetRect(RectTransform rt, float w, float h, float x, float y, TextAnchor anchor)
    {
        float hx = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.LowerLeft ? 0f :
                   anchor == TextAnchor.UpperRight || anchor == TextAnchor.MiddleRight || anchor == TextAnchor.LowerRight ? 1f : 0.5f;
        float hy = anchor == TextAnchor.UpperLeft || anchor == TextAnchor.UpperCenter || anchor == TextAnchor.UpperRight ? 1f :
                   anchor == TextAnchor.LowerLeft || anchor == TextAnchor.LowerCenter || anchor == TextAnchor.LowerRight ? 0f : 0.5f;
        rt.anchorMin = new Vector2(hx, hy);
        rt.anchorMax = new Vector2(hx, hy);
        rt.pivot = new Vector2(hx, hy);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector3(x, y);
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void AddSpace(Transform parent, float height)
    {
        var go = new GameObject("Space", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
    }

    static void AddLayoutElement(GameObject go, float prefWidth, float prefHeight, float flexWidth = -1)
    {
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = prefWidth >= 0 ? prefWidth : -1;
        le.preferredHeight = prefHeight >= 0 ? prefHeight : -1;
        if (flexWidth >= 0) le.flexibleWidth = flexWidth;
    }

    static Button CreateMenuButton(Transform parent, string name,
        string label, Color normal, Color highlighted, Color pressed, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.type = Image.Type.Sliced;
        img.color = Color.white;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = normal;
        colors.highlightedColor = highlighted;
        colors.pressedColor = pressed;
        btn.colors = colors;

        if (height > 0)
            AddLayoutElement(go, -1, height);

        var tmp = CreateTmp("Label", go.transform, label, 18, FontStyles.Bold, txt);
        Stretch(tmp.rectTransform);

        return btn;
    }

    static Button CreateTabButton(Transform parent, string name,
        string label, Color normal)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.type = Image.Type.Sliced;
        img.color = Color.white;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = normal;
        colors.highlightedColor = normal * 1.2f;
        colors.pressedColor = normal * 0.7f;
        btn.colors = colors;

        var tmp = CreateTmp("Label", go.transform, label, 16, FontStyles.Bold, txt);
        Stretch(tmp.rectTransform);

        return btn;
    }

    static RectTransform CreateRow(Transform parent, string name, float height)
    {
        var rt = CreateRect(name, parent);
        AddLayoutElement(rt.gameObject, -1, height);
        var hlg = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.childControlHeight = true;
        hlg.childControlWidth = false;
        hlg.childForceExpandHeight = true;
        return rt;
    }

    static TMP_Dropdown CreateDropdown(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.type = Image.Type.Sliced;
        img.color = inputBg;

        var dd = go.AddComponent<TMP_Dropdown>();
        dd.captionText = CreateDropdownLabel(go.transform);

        var template = CreateDropdownTemplate(go.transform);
        dd.template = template;
        dd.itemText = CreateDropdownItemLabel(template);

        return dd;
    }

    static TextMeshProUGUI CreateDropdownLabel(Transform parent)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 14;
        tmp.color = txt;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8, 0);
        rt.offsetMax = new Vector2(0, 0);
        return tmp;
    }

    static RectTransform CreateDropdownTemplate(Transform parent)
    {
        var go = new GameObject("Template", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        go.SetActive(false);

        var img = go.AddComponent<Image>();
        img.type = Image.Type.Sliced;
        img.color = panel;

        var sr = go.AddComponent<ScrollRect>();

        var vp = new GameObject("Viewport", typeof(RectTransform));
        vp.layer = 5;
        vp.transform.SetParent(go.transform, false);
        vp.AddComponent<Image>();
        vp.AddComponent<Mask>().showMaskGraphic = true;
        var vpRt = vp.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = Vector2.zero;
        sr.viewport = vpRt;

        var content = new GameObject("Content", typeof(RectTransform));
        content.layer = 5;
        content.transform.SetParent(vp.transform, false);
        content.AddComponent<VerticalLayoutGroup>();
        var cRt = content.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0, 1);
        cRt.anchorMax = Vector2.one;
        cRt.pivot = new Vector2(0.5f, 1);
        cRt.sizeDelta = new Vector2(0, 28);
        sr.content = cRt;

        var item = CreateDropdownItem(content.transform);

        var sa = new GameObject("Sliding Area", typeof(RectTransform));
        sa.layer = 5;
        sa.transform.SetParent(go.transform, false);
        var saRt = sa.GetComponent<RectTransform>();
        saRt.anchorMin = new Vector2(1, 0);
        saRt.anchorMax = Vector2.one;
        saRt.pivot = Vector2.one;
        saRt.sizeDelta = new Vector2(20, 0);

        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.layer = 5;
        handle.transform.SetParent(sa.transform, false);
        handle.AddComponent<Image>();
        var hRt = handle.GetComponent<RectTransform>();
        hRt.anchorMin = new Vector2(0, 0);
        hRt.anchorMax = new Vector2(1, 0.2f);
        hRt.sizeDelta = new Vector2(20, 20);

        return go.GetComponent<RectTransform>();
    }

    static GameObject CreateDropdownItem(Transform content)
    {
        var go = new GameObject("Item", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(content, false);

        var bg = new GameObject("Item Background", typeof(RectTransform));
        bg.layer = 5;
        bg.transform.SetParent(go.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.96f, 0.96f, 0.96f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;

        var check = new GameObject("Item Checkmark", typeof(RectTransform));
        check.layer = 5;
        check.transform.SetParent(go.transform, false);
        check.AddComponent<Image>();
        var checkRt = check.GetComponent<RectTransform>();
        checkRt.anchorMin = new Vector2(0, 0.5f);
        checkRt.anchorMax = new Vector2(0, 0.5f);
        checkRt.pivot = new Vector2(0.5f, 0.5f);
        checkRt.anchoredPosition = new Vector2(10, 0);
        checkRt.sizeDelta = new Vector2(20, 20);

        var label = new GameObject("Item Label", typeof(RectTransform));
        label.layer = 5;
        label.transform.SetParent(go.transform, false);
        var labelTmp = label.AddComponent<TextMeshProUGUI>();
        labelTmp.fontSize = 14;
        labelTmp.color = txt;
        var labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(30, 0);
        labelRt.offsetMax = Vector2.zero;

        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = bgImg;
        toggle.graphic = check.GetComponent<Image>();
        toggle.isOn = true;

        return go;
    }

    static TextMeshProUGUI CreateDropdownItemLabel(RectTransform template)
    {
        var content = template.Find("Viewport/Content");
        if (content == null) return null;
        var item = content.GetChild(0);
        if (item == null) return null;
        return item.Find("Item Label")?.GetComponent<TextMeshProUGUI>();
    }

    static TMP_InputField CreateInputField(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.type = Image.Type.Sliced;
        img.color = inputBg;

        var input = go.AddComponent<TMP_InputField>();
        input.textComponent = CreateInputText(go.transform);
        input.placeholder = CreateInputPlaceholder(go.transform);

        return input;
    }

    static TextMeshProUGUI CreateInputText(Transform parent)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 16;
        tmp.color = txt;
        Stretch(go.GetComponent<RectTransform>());
        return tmp;
    }

    static TextMeshProUGUI CreateInputPlaceholder(Transform parent)
    {
        var go = new GameObject("Placeholder", typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "Enter text...";
        tmp.fontSize = 14;
        tmp.fontStyle = FontStyles.Italic;
        tmp.color = muted;
        Stretch(go.GetComponent<RectTransform>());
        return tmp;
    }

    static Button CreateColorChip(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.type = Image.Type.Sliced;
        img.color = Color.white;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = color;
        colors.highlightedColor = color * 1.2f;
        colors.pressedColor = color * 0.7f;
        btn.colors = colors;

        AddLayoutElement(go, 40, -1);
        return btn;
    }

    static RectTransform CreateScrollView(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.1f);

        var sr = go.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;

        var vp = new GameObject("Viewport", typeof(RectTransform));
        vp.layer = 5;
        vp.transform.SetParent(go.transform, false);
        vp.AddComponent<Image>().color = Color.clear;
        vp.AddComponent<RectMask2D>();
        var vpRt = vp.GetComponent<RectTransform>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        vpRt.offsetMin = Vector2.zero;
        vpRt.offsetMax = new Vector2(-20, 0);
        sr.viewport = vpRt;

        var content = new GameObject("Content", typeof(RectTransform));
        content.layer = 5;
        content.transform.SetParent(vp.transform, false);
        var cRt = content.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0, 1);
        cRt.anchorMax = Vector2.one;
        cRt.pivot = new Vector2(0.5f, 1);
        cRt.sizeDelta = new Vector2(0, 0);
        sr.content = cRt;

        var scrollbarGo = new GameObject("Scrollbar Vertical", typeof(RectTransform));
        scrollbarGo.layer = 5;
        scrollbarGo.transform.SetParent(go.transform, false);
        var sbRt = scrollbarGo.GetComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(1, 0);
        sbRt.anchorMax = Vector2.one;
        sbRt.pivot = Vector2.one;
        sbRt.sizeDelta = new Vector2(20, 0);

        var sbImg = scrollbarGo.AddComponent<Image>();
        sbImg.color = new Color(0, 0, 0, 0.3f);

        var sb = scrollbarGo.AddComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;
        var sbColors = sb.colors;
        sbColors.normalColor = btnGray;
        sb.colors = sbColors;

        var slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
        slidingArea.layer = 5;
        slidingArea.transform.SetParent(scrollbarGo.transform, false);
        var saRt = slidingArea.GetComponent<RectTransform>();
        saRt.anchorMin = Vector2.zero;
        saRt.anchorMax = Vector2.one;
        saRt.sizeDelta = new Vector2(-20, -20);

        var handle = new GameObject("Handle", typeof(RectTransform));
        handle.layer = 5;
        handle.transform.SetParent(slidingArea.transform, false);
        var hImg = handle.AddComponent<Image>();
        hImg.color = btnGray;
        var hRt = handle.GetComponent<RectTransform>();
        hRt.anchorMin = Vector2.zero;
        hRt.anchorMax = Vector2.one;
        hRt.sizeDelta = Vector2.zero;
        sb.targetGraphic = hImg;
        sb.handleRect = hRt;

        sr.verticalScrollbar = sb;

        return go.GetComponent<RectTransform>();
    }

    static Transform GetScrollContent(RectTransform scrollView)
    {
        var vp = scrollView.Find("Viewport");
        return vp?.Find("Content");
    }
}
