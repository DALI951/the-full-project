using UnityEngine;

/// <summary>
/// HomeSite building — pre-placed in the scene at game start.
/// Can spawn Villager units.
/// Inherits all spawning logic from Building.
/// Add HomeSite-specific logic here (e.g., population cap, upgrades).
/// </summary>
public class HomeSite : Building
{
    protected override void Start()
    {
        buildingName = "Home Site";
        buildingDescription = "Trains Villagers. Villagers gather resources and construct buildings.";
        base.Start();
    }
}
