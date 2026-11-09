using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class UnitSpawnDiagnostic
{
    public static string SpawnShowcase()
    {
        if (!EditorApplication.isPlaying)
        {
            return "{\"error\":\"Must be in Play Mode to spawn showcase\"}";
        }

        // Clean any previous showcase objects
        GameObject oldHolder = GameObject.Find("SHOWCASE_VILLAGE");
        if (oldHolder != null) Object.Destroy(oldHolder);

        GameObject holder = new GameObject("SHOWCASE_VILLAGE");

        // Find existing player TownCenter or default to (0,0,0)
        Vector3 center = Vector3.zero;
        GameObject existingTC = GameObject.Find("HomeSite(Clone)");
        if (existingTC != null)
        {
            center = existingTC.transform.position;
            // Reposition or hide the duplicate initial TC so our showcase TC takes center stage
            existingTC.SetActive(false);
        }

        // Reveal fog of war generously around showcase center
        if (FogOfWar.Instance != null)
        {
            FogOfWar.Instance.Reveal(center, 50f);
        }

        // Building positions relative to center
        var buildingList = new (string prefab, Vector3 relPos, float rotY)[]
        {
            ("Assets/Prefabs/HomeSite.prefab",   new Vector3(0f, 0f, 0f), 0f),
            ("Assets/Prefabs/Barracks.prefab",   new Vector3(-6.2f, 0f, 1.2f), 15f),
            ("Assets/Prefabs/Market.prefab",     new Vector3(6.2f, 0f, 1.2f), -15f),
            ("Assets/Prefabs/Farm.prefab",       new Vector3(-5.0f, 0f, 6.8f), 0f),
            ("Assets/Prefabs/LumberMill.prefab", new Vector3(5.0f, 0f, 6.8f), 0f),
            ("Assets/Prefabs/Tower.prefab",      new Vector3(-3.4f, 0f, -4.8f), 10f),
            ("Assets/Prefabs/Wall.prefab",       new Vector3(-1.1f, 0f, -4.8f), 0f),
            ("Assets/Prefabs/Wall.prefab",       new Vector3(1.1f, 0f, -4.8f), 0f),
            ("Assets/Prefabs/Tower.prefab",      new Vector3(3.4f, 0f, -4.8f), -10f),
            ("Assets/Prefabs/GoldMine.prefab",   new Vector3(0f, 0f, 8.5f), 0f)
        };

        foreach (var (pPath, relPos, rotY) in buildingList)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pPath);
            if (prefab == null) continue;
            Vector3 pos = center + relPos;
            pos.y = 0f;
            GameObject inst = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, rotY, 0f), holder.transform);
            inst.name = $"SHOWCASE_{prefab.name}";
            Building b = inst.GetComponent<Building>();
            if (b != null)
            {
                b.SetOwner(0);
            }
        }

        // Unit positions relative to center
        var unitList = new (string prefab, Vector3 relPos, float rotY)[]
        {
            ("Assets/Prefabs/Villager.prefab", new Vector3(-1.2f, 0f, -2.2f), 140f),
            ("Assets/Prefabs/Villager.prefab", new Vector3(1.0f, 0f, -2.4f), 160f),
            ("Assets/Prefabs/Villager.prefab", new Vector3(-0.4f, 0f, 6.8f), 30f),
            ("Assets/Prefabs/Infantry.prefab", new Vector3(-3.8f, 0f, -3.2f), 180f),
            ("Assets/Prefabs/Infantry.prefab", new Vector3(-2.6f, 0f, -3.4f), 170f),
            ("Assets/Prefabs/Cavarly.prefab",  new Vector3(3.0f, 0f, -2.8f), 190f),
            ("Assets/Prefabs/Cavarly.prefab",  new Vector3(4.6f, 0f, -2.2f), 175f)
        };

        foreach (var (pPath, relPos, rotY) in unitList)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pPath);
            if (prefab == null) continue;
            Vector3 pos = center + relPos;
            pos.y = 0f;
            GameObject inst = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, rotY, 0f), holder.transform);
            inst.name = $"SHOWCASE_{prefab.name}";
            Unit u = inst.GetComponent<Unit>();
            if (u != null)
            {
                u.SetOwner(0);
                u.AttachMedievalGear();
            }
        }

        // Tree positions relative to center (standing near the Lumber Mill)
        var treeList = new (string prefab, Vector3 relPos, float rotY)[]
        {
            ("Assets/Prefabs/Tree.prefab", new Vector3(7.2f, 0f, 6.2f), 15f),
            ("Assets/Prefabs/Tree.prefab", new Vector3(7.8f, 0f, 4.2f), 45f)
        };

        foreach (var (pPath, relPos, rotY) in treeList)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pPath);
            if (prefab == null) continue;
            Vector3 pos = center + relPos;
            pos.y = 0f;
            GameObject inst = Object.Instantiate(prefab, pos, Quaternion.Euler(0f, rotY, 0f), holder.transform);
            inst.name = $"SHOWCASE_{prefab.name}";
        }

        // Camera setup for optimal isometric overview
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

        if (cam != null)
        {
            cam.transform.position = center + new Vector3(0f, 10.5f, -12.5f);
            cam.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
        }

        return Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            success = true,
            center = center.ToString("F2"),
            buildingsSpawned = buildingList.Length,
            unitsSpawned = unitList.Length
        });
    }

    public static string InspectTree()
    {
        GameObject treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tree.prefab");
        GameObject treeFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ressources/tree/tree.fbx");
        var sb = new StringBuilder();

        sb.AppendLine("=== TREE FBX ===");
        if (treeFbx != null)
        {
            foreach (var r in treeFbx.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                sb.AppendLine($"FBX Child: {r.name}, pos: {r.transform.localPosition}, rot: {r.transform.localEulerAngles}, bounds: {r.bounds.size}, meshBounds: {(mf != null ? mf.sharedMesh.bounds.ToString() : "no mf")}");
            }
        }

        sb.AppendLine("=== TREE PREFAB ===");
        if (treePrefab != null)
        {
            foreach (var r in treePrefab.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                sb.AppendLine($"Prefab Child: {r.name}, pos: {r.transform.localPosition}, rot: {r.transform.localEulerAngles}, scale: {r.transform.localScale}, bounds: {r.bounds.size}, meshBounds: {(mf != null ? mf.sharedMesh.bounds.ToString() : "no mf")}");
            }
        }

        return sb.ToString();
    }

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
