using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class UnitSpawnDiagnostic
{
    public static string RunDiagnosis()
    {
        if (!EditorApplication.isPlaying)
        {
            return "{\"error\":\"Must be in Play Mode to run spawn diagnosis\"}";
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            foreach (var c in Camera.allCameras)
            {
                if (c != null && c.name.IndexOf("MiniMap", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    cam = c;
                    break;
                }
            }
        }

        Vector3 basePos = Vector3.zero;
        Vector3 fwd = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cam != null)
        {
            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
            {
                basePos = ray.GetPoint(enter);
            }
            else
            {
                basePos = cam.transform.position + cam.transform.forward * 12f;
                basePos.y = 0f;
            }
            fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            right = Vector3.Cross(Vector3.up, fwd).normalized;
        }

        string[] prefabPaths = new string[]
        {
            "Assets/Prefabs/Villager.prefab",
            "Assets/Prefabs/Infantry.prefab",
            "Assets/Prefabs/Cavarly.prefab"
        };

        var results = new List<object>();
        float spacing = 3.5f;

        for (int i = 0; i < prefabPaths.Length; i++)
        {
            string pPath = prefabPaths[i];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pPath);
            if (prefab == null) continue;

            Vector3 spawnPos = basePos + right * ((i - 1) * spacing);
            spawnPos.y = 0f;

            GameObject instance = Object.Instantiate(prefab, spawnPos, Quaternion.LookRotation(-fwd, Vector3.up));
            instance.name = $"DEBUG_SPAWNED_{prefab.name}";

            // Trigger Start and AttachMedievalGear
            Unit u = instance.GetComponent<Unit>();
            if (u != null)
            {
                u.SetOwner(0);
                u.AttachMedievalGear();
            }

            var rendererInfo = new List<object>();
            Bounds totalBounds = new Bounds();
            bool hasTotalBounds = false;

            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {

                if (r.name.Equals("SelectionCircle", System.StringComparison.OrdinalIgnoreCase)) continue;

                if (!hasTotalBounds)
                {
                    totalBounds = r.bounds;
                    hasTotalBounds = true;
                }
                else
                {
                    totalBounds.Encapsulate(r.bounds);
                }

                rendererInfo.Add(new
                {
                    name = r.gameObject.name,
                    parent = r.transform.parent != null ? r.transform.parent.name : "null",
                    localScale = r.transform.localScale.ToString("F3"),
                    lossyScale = r.transform.lossyScale.ToString("F3"),
                    boundsSize = r.bounds.size.ToString("F3"),
                    boundsCenter = r.bounds.center.ToString("F3")
                });
            }

            results.Add(new
            {
                unitName = prefab.name,
                rootLossyScale = instance.transform.lossyScale.ToString("F3"),
                totalWorldBoundsSize = hasTotalBounds ? totalBounds.size.ToString("F3") : "None",
                totalWorldBoundsHeight = hasTotalBounds ? totalBounds.size.y : 0f,
                renderers = rendererInfo
            });
        }

        // Capture screenshot
        string brainDir = @"C:\Users\Dali\.gemini\antigravity\brain\ebcbb35f-e44d-4ad4-a850-92b76c447506";
        if (!Directory.Exists(brainDir)) Directory.CreateDirectory(brainDir);
        string capturePath = Path.Combine(brainDir, "summoned_units_debug.png");

        if (cam != null)
        {
            int w = 1280;
            int h = 720;
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

            File.WriteAllBytes(capturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        return Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            success = true,
            screenshot = capturePath,
            units = results
        }, Newtonsoft.Json.Formatting.Indented);
    }
}
