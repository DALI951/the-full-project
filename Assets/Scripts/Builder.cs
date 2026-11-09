using UnityEngine;

/// <summary>
/// Builder — constructs buildings, cannot gather resources.
/// Can be selected and commanded by the player.
/// </summary>
public class Builder : Villager
{
    protected override void Awake()
    {
        unitName        = "Builder";
        unitType        = "Builder";
        unitDescription = "Constructs buildings. Cannot gather.";
        maxHealth       = 70;
        baseSpeed       = 2.8f;
        attackDamage    = 5;
        attackRange     = 1.5f;
        attackCooldown  = 2.5f;
        base.Awake();

        unitName        = "Builder";
        unitType        = "Builder";
    }

    // ── Selectable ──────────────────────────────────────────────────
    public override bool IsSelectable => true;

    // ── Cannot gather ─────────────────────────────────────────────────
    public override void GatherFrom(ResourceNode node) { }
}
