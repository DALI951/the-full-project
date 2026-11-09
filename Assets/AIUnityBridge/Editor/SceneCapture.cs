using UnityEditor;
using UnityEngine;
using System.IO;

public static class SceneCapture
{
    private static string ScreenshotDir
    {
        get
        {
            string dir = Path.Combine(Application.dataPath, "..", "AIBridge_Captures");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string CaptureGameView()
    {
        string path = Path.Combine(ScreenshotDir, "game_view.png");

        int w = 1280;
        int h = 720;
        var rt = RenderTexture.GetTemporary(w, h, 24);
        Camera cam = null;
        foreach (var c in Camera.allCameras)
        {
            if (c != null && c.targetTexture == null && c.name.IndexOf("MiniMap", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                cam = c;
                break;
            }
        }
        if (cam == null) cam = Camera.main;
        if (cam == null) return null;

        var prevRT = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        cam.targetTexture = prevRT;
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        return path;
    }

    public static string CaptureSceneView()
    {
        string path = Path.Combine(ScreenshotDir, "scene_view.png");

        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null) return null;

        var cam = sceneView.camera;
        if (cam == null) return null;

        int w = Mathf.Max(640, (int)sceneView.position.width);
        int h = Mathf.Max(360, (int)sceneView.position.height);

        var rt = RenderTexture.GetTemporary(w, h, 24);
        var prevRT = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        cam.targetTexture = prevRT;
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        return path;
    }

    public static string CaptureObjectPreview(GameObject obj)
    {
        if (obj == null) return null;
        string path = Path.Combine(ScreenshotDir, "object_preview.png");

        var rt = RenderTexture.GetTemporary(512, 512, 24);
        var previewCam = new GameObject("_PreviewCam", typeof(Camera)).GetComponent<Camera>();
        previewCam.transform.position = obj.transform.position + Vector3.one * 5f;
        previewCam.transform.LookAt(obj.transform);
        previewCam.targetTexture = rt;
        previewCam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
        tex.Apply();

        previewCam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(previewCam.gameObject);
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        return path;
    }
}
