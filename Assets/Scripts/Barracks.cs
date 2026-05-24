using UnityEngine;

/// <summary>
/// Barracks building — pre-placed in the scene at game start.
/// Can spawn Infantry and Cavalry units.
/// Inherits all spawning logic from Building.
/// </summary>
public class Barracks : Building
{
    public override int TrainingPriority => 10;
    protected override void Start()
    {
        buildingName = "Barracks";
        buildingDescription = "Trains Infantry and Cavalry. Military units attack enemies and defend territory.";
        base.Start();
    }

}
