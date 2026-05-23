using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UnitSelectionManager — keeps a live list of every unit in the scene.
/// Units register themselves on Awake and unregister on Die().
/// UnitSelectionBox uses this list for drag-select.
/// Attach to GameManager.
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }

    /// <summary>Every unit currently alive in the scene.</summary>
    public List<Unit> allUnitsList = new List<Unit>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Register(Unit u)
    {
        if (u != null && !allUnitsList.Contains(u))
            allUnitsList.Add(u);
    }

    public void Unregister(Unit u)
    {
        allUnitsList.Remove(u);
    }
}
