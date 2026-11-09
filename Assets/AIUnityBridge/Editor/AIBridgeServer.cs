using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[InitializeOnLoad]
public static class AIBridgeServer
{
    private static HttpListener _listener;
    private static Thread _serverThread;
    private static int _port = 9876;
    private static bool _running;
    private static bool _startFailed;

    private static readonly Dictionary<string, Func<HttpListenerRequest, string>> _routes;
    private static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
    private static readonly List<object> _recentLogs = new List<object>();
    private static readonly object _logLock = new object();

    static AIBridgeServer()
    {
        Application.logMessageReceivedThreaded += OnLogMessageReceived;

        _routes = new Dictionary<string, Func<HttpListenerRequest, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "GET:/capture/game", req => RunOnMainThread(() => WrapCaptureFile(SceneCapture.CaptureGameView())) },
            { "GET:/capture/scene", req => RunOnMainThread(() => WrapCaptureFile(SceneCapture.CaptureSceneView())) },
            { "GET:/scene/hierarchy", req => RunOnMainThread(() => SceneDataExporter.GetHierarchy()) },
            { "GET:/scene/all", req => RunOnMainThread(() => SceneDataExporter.GetAllSceneObjects()) },
            { "GET:/scene/current", req => RunOnMainThread(() => JsonUtils.ToJson(new { scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path })) },
            { "POST:/scene/open", req => HandleOpenScene(req) },
            { "GET:/scene/open", req => HandleOpenScene(req) },
            { "POST:/object/get", req => HandleObjectGet(req) },
            { "POST:/object/set", req => HandleObjectSet(req) },
            { "POST:/object/animator", req => HandleAnimator(req) },
            { "POST:/playmode", req => HandlePlayMode(req) },
            { "GET:/playmode", req => !string.IsNullOrEmpty(req.QueryString["command"]) ? HandlePlayMode(req) : RunOnMainThread(() => RuntimeControl.GetPlayModeState()) },
            { "GET:/refresh", req => RunOnMainThread(() => { AssetDatabase.Refresh(); return JsonUtils.ToJson(new { status = "refreshed", compileFailed = EditorUtility.scriptCompilationFailed }); }) },
            { "GET:/fixall", req => RunOnMainThread(() => { ProjectAutoFixer.FixAll(); return JsonUtils.ToJson(new { status = "fixed" }); }) },
            { "GET:/logs", req => GetLogsJson() },
            { "GET:/logs/clear", req => { lock (_logLock) { _recentLogs.Clear(); } return "{\"status\":\"cleared\"}"; } },
            { "POST:/logs/clear", req => { lock (_logLock) { _recentLogs.Clear(); } return "{\"status\":\"cleared\"}"; } },
            { "GET:/debug/summon", req => RunOnMainThread(() => UnitSpawnDiagnostic.RunDiagnosis()) },
            { "GET:/debug/showcase", req => RunOnMainThread(() => UnitSpawnDiagnostic.SpawnShowcase()) },
            { "GET:/health", req => RunOnMainThread(() => JsonUtils.ToJson(new { status = "ok", unity = "editor", isCompiling = EditorApplication.isCompiling, compileFailed = EditorUtility.scriptCompilationFailed, isPlaying = EditorApplication.isPlaying })) }
        };

        EditorApplication.update += OnEditorUpdate;
        StartServer();
    }

    private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        lock (_logLock)
        {
            if (_recentLogs.Count >= 200)
                _recentLogs.RemoveAt(0);
            _recentLogs.Add(new
            {
                time = DateTime.Now.ToString("HH:mm:ss"),
                type = type.ToString(),
                message = condition,
                stackTrace = (type == LogType.Error || type == LogType.Exception) ? stackTrace : ""
            });
        }
    }

    private static string GetLogsJson()
    {
        lock (_logLock)
        {
            return JsonUtils.ToJson(new
            {
                count = _recentLogs.Count,
                logs = new List<object>(_recentLogs)
            });
        }
    }

    private static string RunOnMainThread(Func<string> func)
    {
        string res = null;
        Exception err = null;
        using (var evt = new ManualResetEventSlim(false))
        {
            _mainThreadQueue.Enqueue(() =>
            {
                try { res = func(); }
                catch (Exception ex) { err = ex; }
                finally { evt.Set(); }
            });
            if (!evt.Wait(10000))
                return JsonUtils.ToJson(new { error = "Main thread timeout (Unity may be compiling or unfocused)" });
        }
        if (err != null)
            return JsonUtils.ToJson(new { error = err.Message });
        return res;
    }

    private static void OnEditorUpdate()
    {
        if (!_running && !_startFailed)
            StartServer();

        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Debug.LogError($"[AIBridge] MainThread action error: {e.Message}"); }
        }
    }

    public static void StartServer()
    {
        if (_running) return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _running = true;
            _startFailed = false;

            _serverThread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = "AIBridgeServer"
            };
            _serverThread.Start();

            Debug.Log($"[AIBridge] HTTP Server started on http://localhost:{_port}/");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AIBridge] Failed to start server: {e.Message}");
            _running = false;
            _listener = null;
            _startFailed = true;
        }
    }

    public static void StopServer()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        Debug.Log("[AIBridge] HTTP Server stopped");
    }

    private static void ServerLoop()
    {
        while (_running)
        {
            try
            {
                if (_listener == null || !_listener.IsListening) break;
                var context = _listener.GetContext();
                ProcessRequest(context);
            }
            catch (HttpListenerException) { break; }
            catch (System.Threading.ThreadAbortException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception)
            {
                if (!_running) break;
            }
        }
    }

    private static void ProcessRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        string result = null;
        int statusCode = 200;
        string contentType = "application/json";

        try
        {
            string method = req.HttpMethod.ToUpper();
            string path = req.Url.AbsolutePath;
            string routeKey = $"{method}:{path}";

            if (_routes.TryGetValue(routeKey, out var handler))
            {
                result = handler(req);
            }
            else
            {
                statusCode = 404;
                result = JsonUtils.ToJson(new { error = $"Route not found: {method} {path}" });
            }
        }
        catch (Exception e)
        {
            statusCode = 500;
            result = JsonUtils.ToJson(new { error = e.Message });
        }

        if (result == null)
        {
            statusCode = 500;
            result = JsonUtils.ToJson(new { error = "Handler returned null" });
        }

        if (result != null && result.StartsWith("FILE:"))
        {
            var parts = result.Split(new[] { ':' }, 3);
            if (parts.Length == 3 && File.Exists(parts[2]))
            {
                byte[] fileBytes = File.ReadAllBytes(parts[2]);
                resp.StatusCode = 200;
                resp.ContentType = parts[1];
                resp.ContentLength64 = fileBytes.Length;
                resp.OutputStream.Write(fileBytes, 0, fileBytes.Length);
                resp.OutputStream.Close();
                return;
            }
        }

        byte[] buffer = Encoding.UTF8.GetBytes(result);
        resp.StatusCode = statusCode;
        resp.ContentType = contentType;
        resp.ContentLength64 = buffer.Length;
        resp.OutputStream.Write(buffer, 0, buffer.Length);
        resp.OutputStream.Close();
    }

    private static string HandleObjectGet(HttpListenerRequest req)
    {
        var data = ReadBody(req);
        int id = ExtractInt(data, "id");
        if (id == 0) return JsonUtils.ToJson(new { error = "Missing 'id' field" });
        return RunOnMainThread(() => SceneDataExporter.GetObjectData(id));
    }

    private static string HandleObjectSet(HttpListenerRequest req)
    {
        var data = ReadBody(req);
        int id = ExtractInt(data, "id");
        if (id == 0) return JsonUtils.ToJson(new { error = "Missing 'id' field" });

        string action = ExtractString(data, "action");

        return RunOnMainThread(() =>
        {
            switch (action)
            {
                case "position":
                {
                    float x = ExtractFloat(data, "x");
                    float y = ExtractFloat(data, "y");
                    float z = ExtractFloat(data, "z");
                    return RuntimeControl.SetPosition(id, x, y, z);
                }
                case "rotation":
                {
                    float x = ExtractFloat(data, "x");
                    float y = ExtractFloat(data, "y");
                    float z = ExtractFloat(data, "z");
                    return RuntimeControl.SetRotation(id, x, y, z);
                }
                case "scale":
                {
                    float x = ExtractFloat(data, "x");
                    float y = ExtractFloat(data, "y");
                    float z = ExtractFloat(data, "z");
                    return RuntimeControl.SetScale(id, x, y, z);
                }
                case "enable_component":
                {
                    string comp = ExtractString(data, "component");
                    bool enabled = ExtractBool(data, "enabled");
                    return RuntimeControl.EnableComponent(id, comp, enabled);
                }
                default:
                    return JsonUtils.ToJson(new { error = $"Unknown action: {action}" });
            }
        });
    }

    private static string HandleAnimator(HttpListenerRequest req)
    {
        var data = ReadBody(req);
        int id = ExtractInt(data, "id");
        if (id == 0) return JsonUtils.ToJson(new { error = "Missing 'id' field" });

        string action = ExtractString(data, "action");

        return RunOnMainThread(() =>
        {
            switch (action)
            {
                case "trigger":
                    return RuntimeControl.SetAnimatorTrigger(id, ExtractString(data, "triggerName"));
                case "bool":
                    return RuntimeControl.SetAnimatorBool(id, ExtractString(data, "paramName"), ExtractBool(data, "value"));
                case "float":
                    return RuntimeControl.SetAnimatorFloat(id, ExtractString(data, "paramName"), ExtractFloat(data, "value"));
                case "speed":
                    return RuntimeControl.SetAnimatorSpeed(id, ExtractFloat(data, "speed"));
                case "state":
                    return SceneDataExporter.GetAnimatorState(id);
                default:
                    return JsonUtils.ToJson(new { error = $"Unknown animator action: {action}" });
            }
        });
    }

    private static string HandlePlayMode(HttpListenerRequest req)
    {
        string command = req.QueryString["command"];
        if (string.IsNullOrEmpty(command))
        {
            var data = ReadBody(req);
            command = ExtractString(data, "command");
            if (string.IsNullOrEmpty(command) && !string.IsNullOrEmpty(data))
            {
                string trimmed = data.Trim().Trim('"', '\'');
                if (trimmed.Equals("play", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("stop", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("pause", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("unpause", StringComparison.OrdinalIgnoreCase))
                {
                    command = trimmed;
                }
            }
        }
        return RunOnMainThread(() => RuntimeControl.PlayModeControl(command ?? ""));
    }

    private static string WrapCaptureFile(string path)
    {
        if (path == null || !File.Exists(path))
            return JsonUtils.ToJson(new { error = "Capture failed", path = (string)null });
        return $"FILE:image/png:{path}";
    }

    private static string HandleOpenScene(HttpListenerRequest req)
    {
        string scenePath = null;
        if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            var data = ReadBody(req);
            scenePath = ExtractString(data, "path");
        }
        if (string.IsNullOrEmpty(scenePath))
        {
            scenePath = req.QueryString["path"];
        }
        if (string.IsNullOrEmpty(scenePath))
            scenePath = "Assets/Scenes/GameScene.unity";

        return RunOnMainThread(() =>
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
            return JsonUtils.ToJson(new { success = true, scene = scene.path });
        });
    }

    private static string WrapPath(string path)
    {
        if (path == null)
            return JsonUtils.ToJson(new { error = "Capture failed", path = (string)null });
        return JsonUtils.ToJson(new { success = true, path });
    }

    private static string ReadBody(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        return reader.ReadToEnd();
    }

    private static string ExtractString(string json, string key)
    {
        try
        {
            var obj = JObject.Parse(json);
            return obj[key]?.Value<string>();
        }
        catch { return null; }
    }

    private static int ExtractInt(string json, string key)
    {
        try
        {
            var obj = JObject.Parse(json);
            return obj[key]?.Value<int>() ?? 0;
        }
        catch { return 0; }
    }

    private static float ExtractFloat(string json, string key)
    {
        try
        {
            var obj = JObject.Parse(json);
            return obj[key]?.Value<float>() ?? 0f;
        }
        catch { return 0f; }
    }

    private static bool ExtractBool(string json, string key)
    {
        try
        {
            var obj = JObject.Parse(json);
            return obj[key]?.Value<bool>() ?? false;
        }
        catch { return false; }
    }

    private static class JsonUtils
    {
        public static string ToJson(object obj) =>
            JsonConvert.SerializeObject(obj, Formatting.Indented);
    }
}
