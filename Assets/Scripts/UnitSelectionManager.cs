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

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// A destroyed MonoBehaviour is NOT a real CLR null (Unity fake-null only affects
    /// == and !=). Any "Instance?.Foo()" call site elsewhere would therefore still
    /// dispatch into this dead object and throw MissingReferenceException the next
    /// time the scene reloads. Clearing the field makes it a true null.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
