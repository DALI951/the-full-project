using UnityEditor;
using UnityEngine;

public class AIBridgeWindow : EditorWindow
{
    private bool _serverRunning = true;
    private Vector2 _scrollPos;

    [MenuItem("Tools/AI Bridge/Control Panel")]
    public static void ShowWindow()
    {
        GetWindow<AIBridgeWindow>("AI Bridge Control");
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        GUILayout.Label("AI Bridge Server", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "This server allows an AI agent to view and modify the Unity scene.\n" +
            "The server runs on http://localhost:9876/",
            MessageType.Info);

        EditorGUILayout.Space();

        if (_serverRunning)
            EditorGUILayout.HelpBox("Server is RUNNING", MessageType.Info);
        else
            EditorGUILayout.HelpBox("Server is STOPPED", MessageType.Warning);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Restart Server"))
        {
            AIBridgeServer.StartServer();
            _serverRunning = true;
        }
        if (GUILayout.Button("Stop Server"))
        {
            AIBridgeServer.StopServer();
            _serverRunning = false;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        GUILayout.Label("Quick Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Capture Game View"))
        {
            string path = SceneCapture.CaptureGameView();
            Debug.Log($"[AIBridge] Game view captured: {path}");
        }

        if (GUILayout.Button("Capture Scene View"))
        {
            string path = SceneCapture.CaptureSceneView();
            Debug.Log($"[AIBridge] Scene view captured: {path}");
        }

        if (GUILayout.Button("Export Hierarchy"))
        {
            string json = SceneDataExporter.GetHierarchy();
            Debug.Log($"[AIBridge] Hierarchy exported ({json.Length} chars)");
        }

        if (GUILayout.Button("Log All Objects"))
        {
            string json = SceneDataExporter.GetAllSceneObjects();
            Debug.Log($"[AIBridge] All objects exported ({json.Length} chars)");
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Enter Play Mode"))
        {
            RuntimeControl.PlayModeControl("play");
        }

        if (GUILayout.Button("Stop Play Mode"))
        {
            RuntimeControl.PlayModeControl("stop");
        }

        EditorGUILayout.EndScrollView();
    }

    private void OnEnable()
    {
        _serverRunning = true;
    }
}
