using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public static class RuntimeControl
{
    public static string SetPosition(int instanceId, float x, float y, float z)
    {
        var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return JsonError("Object not found");

        Undo.RecordObject(go.transform, "AI Set Position");
        go.transform.position = new Vector3(x, y, z);
        EditorUtility.SetDirty(go.transform);
        return JsonOk($"Position set to ({x}, {y}, {z})");
    }

    public static string SetRotation(int instanceId, float x, float y, float z)
    {
        var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return JsonError("Object not found");

        Undo.RecordObject(go.transform, "AI Set Rotation");
        go.transform.eulerAngles = new Vector3(x, y, z);
        EditorUtility.SetDirty(go.transform);
        return JsonOk($"Rotation set to ({x}, {y}, {z})");
    }

    public static string SetScale(int instanceId, float x, float y, float z)
    {
        var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return JsonError("Object not found");

        Undo.RecordObject(go.transform, "AI Set Scale");
        go.transform.localScale = new Vector3(x, y, z);
        EditorUtility.SetDirty(go.transform);
        return JsonOk($"Scale set to ({x}, {y}, {z})");
    }

    public static string EnableComponent(int instanceId, string componentType, bool enabled)
    {
        var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return JsonError("Object not found");

        var comp = go.GetComponent(componentType) as Behaviour;
        if (comp == null) return JsonError($"Component '{componentType}' not found on object");

        Undo.RecordObject(comp, "AI Toggle Component");
        comp.enabled = enabled;
        EditorUtility.SetDirty(comp);
        return JsonOk($"Component '{componentType}' {(enabled ? "enabled" : "disabled")}");
    }

    public static string SetAnimatorTrigger(int instanceId, string triggerName)
    {
        var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return JsonError("Object not found");

        var animator = go.GetComponent<Animator>();
        if (animator == null) return JsonError("No Animator component");

        animator.SetTrigger(triggerName);
        EditorUtility.SetDirty(animator);
        return JsonOk($"Trigger '{triggerName}' set");
    }

    public static string SetAnimatorBool(int instanceId, string paramName, bool value)
    {
        var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return JsonError("Object not found");

        var animator = go.GetComponent<Animator>();
        if (animator == null) return JsonError("No Animator component");

        animator.SetBool(paramName, value);
        EditorUtility.SetDirty(animator);
        return JsonOk($"Bool '{paramName}' set to {value}");
    }

    public static string SetAnimatorFloat(int instanceId, string paramName, float value)
    {
        var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return JsonError("Object not found");

        var animator = go.GetComponent<Animator>();
        if (animator == null) return JsonError("No Animator component");

        animator.SetFloat(paramName, value);
        EditorUtility.SetDirty(animator);
        return JsonOk($"Float '{paramName}' set to {value}");
    }

    public static string SetAnimatorSpeed(int instanceId, float speed)
    {
        var go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        if (go == null) return JsonError("Object not found");

        var animator = go.GetComponent<Animator>();
        if (animator == null) return JsonError("No Animator component");

        animator.speed = speed;
        EditorUtility.SetDirty(animator);
        return JsonOk($"Animator speed set to {speed}");
    }

    public static string PlayModeControl(string command)
    {
        switch (command.ToLower())
        {
            case "play":
                if (!EditorApplication.isPlaying)
                    EditorApplication.isPlaying = true;
                return JsonOk("Entered Play Mode");
            case "pause":
                EditorApplication.isPaused = true;
                return JsonOk("Paused");
            case "unpause":
                EditorApplication.isPaused = false;
                return JsonOk("Unpaused");
            case "stop":
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
                return JsonOk("Stopped Play Mode");
            default:
                return JsonError($"Unknown command: {command}");
        }
    }

    public static string GetPlayModeState()
    {
        string state = "edit";
        if (EditorApplication.isPlaying) state = "play";
        if (EditorApplication.isPaused) state = "paused";
        return JsonUtils.ToJson(new { playModeState = state });
    }

    private static string JsonError(string msg) =>
        JsonUtils.ToJson(new { success = false, error = msg });

    private static string JsonOk(string msg) =>
        JsonUtils.ToJson(new { success = true, message = msg });

    private static class JsonUtils
    {
        public static string ToJson(object obj) =>
            JsonConvert.SerializeObject(obj, Formatting.Indented);
    }
}
