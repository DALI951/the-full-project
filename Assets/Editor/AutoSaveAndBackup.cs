using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AutoSaveAndBackup
{
    private const double SaveIntervalSeconds = 120.0; // change if you want
    private const string BackupFolderName = "_SceneBackups";

    private static double _nextSaveTime;
    private static bool _isBusy;

    static AutoSaveAndBackup()
    {
        _nextSaveTime = EditorApplication.timeSinceStartup + SaveIntervalSeconds;
        EditorApplication.update += OnUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnUpdate()
    {
        if (_isBusy)
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (EditorApplication.timeSinceStartup < _nextSaveTime)
            return;

        _nextSaveTime = EditorApplication.timeSinceStartup + SaveIntervalSeconds;
        AutoSave("timer");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        if (_isBusy)
            return;

        AutoSave("before play mode");
    }

    private static void AutoSave(string reason)
    {
        try
        {
            _isBusy = true;

            BackupOpenScenes(reason);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Auto-save complete ({reason}).");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Auto-save failed ({reason}): {ex.Message}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private static void BackupOpenScenes(string reason)
    {
        var count = SceneManager.sceneCount;
        if (count == 0)
            return;

        string projectPath = Directory.GetParent(Application.dataPath)!.FullName;
        string backupRoot = Path.Combine(projectPath, "SceneBackups");
        Directory.CreateDirectory(backupRoot);

        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        for (int i = 0; i < count; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || string.IsNullOrEmpty(scene.path))
                continue; // unsaved scene has no file path yet

            string sceneFileName = Path.GetFileNameWithoutExtension(scene.path);
            string backupName = $"{sceneFileName}_{stamp}_{reason}.unity";
            string backupPath = Path.Combine(backupRoot, backupName);

            try
            {
                File.Copy(scene.path, backupPath, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not back up scene '{scene.name}': {ex.Message}");
            }
        }
    }
}