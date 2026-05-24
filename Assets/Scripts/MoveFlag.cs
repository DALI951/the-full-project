using UnityEngine;
using System.Collections.Generic;

public class MoveFlag : MonoBehaviour
{
    public static MoveFlag Instance { get; private set; }

    [SerializeField] private GameObject flagPrefab;

    private List<GameObject> activeFlags = new List<GameObject>();
    private GameObject rallyFlag;
    [SerializeField] private float flagAutoDestroyTime = 12f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Show a single flag (replaces all previous). Call when unit is selected and ordered.</summary>
    public void ShowFlag(Vector3 position, bool addToPath = false)
    {
        if (flagPrefab == null)
        {
            Debug.LogWarning("[MoveFlag] No flag prefab assigned!");
            return;
        }

        if (!addToPath)
        {
            // Clear old flags
            foreach (var f in activeFlags)
                if (f != null) Destroy(f);
            activeFlags.Clear();
        }

        GameObject flag = Instantiate(flagPrefab, position + Vector3.up * 0.3f, Quaternion.identity);
        if (flagAutoDestroyTime > 0f) Destroy(flag, flagAutoDestroyTime);
        activeFlags.Add(flag);
    }

    /// <summary>Show rally point flag for buildings.</summary>
    public void ShowSpawnFlag(Vector3 position)
    {
        ClearRallyFlag();
        if (flagPrefab == null) return;
        rallyFlag = Instantiate(flagPrefab, position + Vector3.up * 0.3f, Quaternion.identity);
    }

    public void ClearRallyFlag()
    {
        if (rallyFlag != null) { Destroy(rallyFlag); rallyFlag = null; }
    }

    public void ClearAllFlags()
    {
        foreach (var f in activeFlags)
            if (f != null) Destroy(f);
        activeFlags.Clear();
    }
}