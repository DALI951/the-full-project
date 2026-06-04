using System;
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

    static AIBridgeServer()
    {
        _routes = new Dictionary<string, Func<HttpListenerRequest, string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "GET:/capture/game", req => WrapPath(SceneCapture.CaptureGameView()) },
            { "GET:/capture/scene", req => WrapPath(SceneCapture.CaptureSceneView()) },
            { "GET:/scene/hierarchy", req => SceneDataExporter.GetHierarchy() },
            { "GET:/scene/all", req => SceneDataExporter.GetAllSceneObjects() },
            { "POST:/object/get", req => HandleObjectGet(req) },
            { "POST:/object/set", req => HandleObjectSet(req) },
            { "POST:/object/animator", req => HandleAnimator(req) },
            { "POST:/playmode", req => HandlePlayMode(req) },
            { "GET:/playmode", req => RuntimeControl.GetPlayModeState() },
            { "GET:/health", req => "{\"status\":\"ok\",\"unity\":\"editor\"}" }
        };

        EditorApplication.update += OnEditorUpdate;
        StartServer();
    }

    private static void OnEditorUpdate()
    {
        if (!_running && !_startFailed)
            StartServer();
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
                var context = _listener.GetContext();
                ProcessRequest(context);
            }
            catch (HttpListenerException) { break; }
            catch (Exception e)
            {
                Debug.LogError($"[AIBridge] Server error: {e.Message}");
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
        return SceneDataExporter.GetObjectData(id);
    }

    private static string HandleObjectSet(HttpListenerRequest req)
    {
        var data = ReadBody(req);
        int id = ExtractInt(data, "id");
        if (id == 0) return JsonUtils.ToJson(new { error = "Missing 'id' field" });

        string action = ExtractString(data, "action");

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
    }

    private static string HandleAnimator(HttpListenerRequest req)
    {
        var data = ReadBody(req);
        int id = ExtractInt(data, "id");
        if (id == 0) return JsonUtils.ToJson(new { error = "Missing 'id' field" });

        string action = ExtractString(data, "action");

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
    }

    private static string HandlePlayMode(HttpListenerRequest req)
    {
        var data = ReadBody(req);
        string command = ExtractString(data, "command");
        return RuntimeControl.PlayModeControl(command);
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
