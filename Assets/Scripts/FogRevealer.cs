using UnityEngine;

/// <summary>
/// FogRevealer — attach to every unit (Villager, Infantry, Cavalry).
/// Each frame it tells FogOfWar to reveal a circle around this unit.
///
/// Setup:
///   Add this component to each unit PREFAB.
///   Set sightRadius in the Inspector (e.g. Villager=8, Infantry=10, Cavalry=12).
/// </summary>
public class FogRevealer : MonoBehaviour
{
    [Tooltip("How far this unit can see in world units.")]
    [SerializeField] private float sightRadius = 10f;

    private void Update()
    {
        Unit u = GetComponent<Unit>();
        if (u != null && u.OwnerPlayerId != PlayerColorManager.LocalPlayerIndex)
        {
            bool isAlly = NetworkedPlayer.LocalInstance != null
                && NetworkedPlayer.SameTeam(PlayerColorManager.LocalPlayerIndex, u.OwnerPlayerId);
            if (!isAlly) return;
        }
        FogOfWar.Instance?.Reveal(transform.position, sightRadius);
    }
}
