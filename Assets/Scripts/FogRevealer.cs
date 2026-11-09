using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// FogRevealer — attach to units (Villager, Infantry, Cavalry) and buildings (HomeSite, Tower, Barracks, etc.).
/// Each frame it tells FogOfWar to reveal a circle around this object if owned by the local player or an ally.
/// </summary>
public class FogRevealer : MonoBehaviour
{
    [Tooltip("How far this unit or building can see in world units.")]
    [FormerlySerializedAs("sightRadius")]
    [SerializeField] private float visionRadius = 14f;

    public float VisionRadius
    {
        get => visionRadius;
        set => visionRadius = Mathf.Max(value, 14f);
    }

    public float SightRadius => visionRadius;

    private Unit     cachedUnit;
    private Building cachedBuilding;

    private void Awake()
    {
        cachedUnit     = GetComponent<Unit>();
        cachedBuilding = GetComponent<Building>();

        if (visionRadius < 14f)
            visionRadius = 14f;
    }

    private void Update()
    {
        int ownerId = -1;

        if (cachedUnit != null)
        {
            ownerId = cachedUnit.OwnerPlayerId;
        }
        else if (cachedBuilding != null)
        {
            ownerId = cachedBuilding.OwnerPlayerId;
        }
        else
        {
            // Fallback in case components were added dynamically
            cachedUnit = GetComponent<Unit>();
            if (cachedUnit != null) ownerId = cachedUnit.OwnerPlayerId;
            else
            {
                cachedBuilding = GetComponent<Building>();
                if (cachedBuilding != null) ownerId = cachedBuilding.OwnerPlayerId;
            }
        }

        // Ownership & alliance check
        if (Mirror.NetworkClient.active)
        {
            if (ownerId != -1 && ownerId != PlayerColorManager.LocalPlayerIndex)
            {
                bool isAlly = NetworkedPlayer.LocalInstance != null
                    && NetworkedPlayer.SameTeam(PlayerColorManager.LocalPlayerIndex, ownerId);
                if (!isAlly) return;
            }
        }
        else
        {
            // Offline single player: only reveal if owned by local player (or neutral)
            if (ownerId != -1 && ownerId != PlayerColorManager.LocalPlayerIndex)
            {
                return;
            }
        }

        FogOfWar.Instance?.Reveal(transform.position, visionRadius);
    }
}
