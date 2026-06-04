using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Newtonsoft.Json;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

public static class SceneDataExporter
{
    public static string GetHierarchy()
    {
        var roots = SceneManager.sceneCount > 0
            ? Enumerable.Range(0, SceneManager.sceneCount)
                .SelectMany(i => SceneManager.GetSceneAt(i).GetRootGameObjects())
            : Object.FindObjectsOfType<Transform>(true)
                .Where(t => t.parent == null)
                .Select(t => t.gameObject);

        var list = new List<object>();
        foreach (var root in roots)
            list.Add(SerializeNode(root));

        return ToJson(new { hierarchy = list });
    }

    public static string GetAllSceneObjects()
    {
        var all = Object.FindObjectsOfType<GameObject>(true);
        var list = all.Select(go => new
        {
            id = go.GetInstanceID(),
            name = go.name,
            active = go.activeInHierarchy,
            tag = go.tag,
            layer = go.layer,
            path = GetGameObjectPath(go.transform),
            transform = new
            {
                position = Vec3(go.transform.position),
                rotation = Vec3(go.transform.eulerAngles),
                scale = Vec3(go.transform.localScale)
            }
        }).ToList();

        return ToJson(new { objects = list, count = list.Count });
    }

    public static string GetObjectData(int instanceId)
    {
        var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (obj == null) return ToJson(new { error = "Object not found" });

        var comps = obj.GetComponents<Component>();
        return ToJson(new
        {
            id = obj.GetInstanceID(),
            name = obj.name,
            active = obj.activeInHierarchy,
            tag = obj.tag,
            layer = obj.layer,
            path = GetGameObjectPath(obj.transform),
            transform = new
            {
                position = Vec3(obj.transform.position),
                rotation = Vec3(obj.transform.eulerAngles),
                scale = Vec3(obj.transform.localScale),
                localPosition = Vec3(obj.transform.localPosition),
                localRotation = Vec3(obj.transform.localEulerAngles),
                localScale = Vec3(obj.transform.localScale)
            },
            components = comps.Select(c => new
            {
                type = c != null ? c.GetType().Name : "Missing",
                enabled = c is Behaviour b ? b.enabled : (c is Collider ? true : (bool?)null),
                properties = c != null ? GetComponentProperties(c) : null
            })
        });
    }

    public static string GetAnimatorState(int instanceId)
    {
        var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (obj == null) return ToJson(new { error = "Object not found" });

        var animator = obj.GetComponent<Animator>();
        if (animator == null) return ToJson(new { error = "No Animator component" });

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        var clips = animator.GetCurrentAnimatorClipInfo(0);

        return ToJson(new
        {
            hasAnimator = true,
            isPlaying = animator.isActiveAndEnabled,
            layerCount = animator.layerCount,
            currentState = new
            {
                fullPathHash = stateInfo.fullPathHash,
                shortNameHash = stateInfo.shortNameHash,
                normalizedTime = stateInfo.normalizedTime,
                length = stateInfo.length,
                speed = stateInfo.speed,
                speedMultiplier = stateInfo.speedMultiplier,
                loop = stateInfo.loop
            },
            currentClips = clips.Select(c => new
            {
                clipName = c.clip != null ? c.clip.name : null,
                weight = c.weight
            }),
            parameters = Enumerable.Range(0, animator.parameters.Length)
                .Select(i =>
                {
                    var p = animator.parameters[i];
                    object val = null;
                    if (p.type == AnimatorControllerParameterType.Float) val = animator.GetFloat(i);
                    else if (p.type == AnimatorControllerParameterType.Int) val = animator.GetInteger(i);
                    else if (p.type == AnimatorControllerParameterType.Bool) val = animator.GetBool(i);
                    else if (p.type == AnimatorControllerParameterType.Trigger) val = false;
                    return new { name = p.name, type = p.type.ToString(), value = val };
                })
        });
    }

    private static object SerializeNode(GameObject go)
    {
        var comps = go.GetComponents<Component>();
        return new
        {
            id = go.GetInstanceID(),
            name = go.name,
            active = go.activeInHierarchy,
            tag = go.tag,
            layer = go.layer,
            components = comps.Where(c => c != null).Select(c => c.GetType().Name).Distinct(),
            childCount = go.transform.childCount,
            children = Enumerable.Range(0, go.transform.childCount)
                .Select(i => SerializeNode(go.transform.GetChild(i).gameObject))
        };
    }

    private static string GetGameObjectPath(Transform t)
    {
        var sb = new StringBuilder();
        while (t != null)
        {
            if (sb.Length > 0) sb.Insert(0, "/");
            sb.Insert(0, t.name);
            t = t.parent;
        }
        return sb.ToString();
    }

    private static object GetComponentProperties(Component c)
    {
        var props = new Dictionary<string, object>();
        var type = c.GetType();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            {
                var val = field.GetValue(c);
                props[field.Name] = SimplifyValue(val);
            }
            catch { }
        }

        return props;
    }

    private static object SimplifyValue(object val)
    {
        if (val == null) return null;
        if (val is Vector3 v) return Vec3(v);
        if (val is Vector2 v2) return new { x = v2.x, y = v2.y };
        if (val is Quaternion q) return new { x = q.x, y = q.y, z = q.z, w = q.w };
        if (val is Color col) return new { r = col.r, g = col.g, b = col.b, a = col.a };
        if (val is Transform || val is GameObject || val is Component)
            return val.GetType().Name + ":" + (val as Object).name;
        return val;
    }

    private static object Vec3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

    private static string ToJson(object obj) =>
        JsonConvert.SerializeObject(obj, Formatting.Indented);
}
