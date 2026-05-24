using UnityEngine;
using System;
using System.IO;
using System.Xml.Serialization;

[System.Serializable]
public class SettingsData
{
    public string playerName       = "Player";
    public int    graphicsQuality  = 2;
    public string resolutionString = "";
    public bool   isFullscreen     = true;
}

/// <summary>
/// SettingsManager — persists settings to an XML file in Documents/mini-age-settings.xml.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     "mini-age-settings.xml");

    public string PlayerName        { get; private set; } = "Player";
    public int    GraphicsQuality   { get; private set; } = 2;
    public string ResolutionString  { get; private set; } = "";
    public bool   IsFullscreen      { get; private set; } = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var serializer = new XmlSerializer(typeof(SettingsData));
            using (var reader = new StreamReader(FilePath))
            {
                var data = (SettingsData)serializer.Deserialize(reader);
                PlayerName       = data.playerName       ?? "Player";
                GraphicsQuality  = Mathf.Clamp(data.graphicsQuality, 0, QualitySettings.names.Length - 1);
                ResolutionString = data.resolutionString ?? "";
                IsFullscreen     = data.isFullscreen;
            }
        }
        catch { }

        ApplyQuality();
        ApplyResolution();
    }

    public void Save()
    {
        try
        {
            var data = new SettingsData
            {
                playerName       = PlayerName,
                graphicsQuality  = GraphicsQuality,
                resolutionString = ResolutionString,
                isFullscreen     = IsFullscreen,
            };
            var serializer = new XmlSerializer(typeof(SettingsData));
            using (var writer = new StreamWriter(FilePath))
            {
                serializer.Serialize(writer, data);
            }
        }
        catch { }
    }

    public void SavePlayerName(string name)
    {
        name = name.Trim();
        if (name.Length == 0) name = "Player";
        PlayerName = name;
        Save();
    }

    public void SaveGraphicsQuality(int level)
    {
        GraphicsQuality = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
        Save();
        ApplyQuality();
    }

    public void SaveResolution(string resString)
    {
        ResolutionString = resString;
        Save();
        ApplyResolution();
    }

    public void SaveFullscreen(bool fullscreen)
    {
        IsFullscreen = fullscreen;
        Save();
        Screen.fullScreen = fullscreen;
    }

    private void ApplyQuality() => QualitySettings.SetQualityLevel(GraphicsQuality, true);

    private void ApplyResolution()
    {
        if (string.IsNullOrEmpty(ResolutionString)) return;
        string[] parts = ResolutionString.Split('x');
        if (parts.Length != 2) return;
        if (!int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h)) return;

        foreach (Resolution r in Screen.resolutions)
        {
            if (r.width == w && r.height == h)
            {
                Screen.SetResolution(r.width, r.height, IsFullscreen, r.refreshRate);
                return;
            }
        }
    }
}
