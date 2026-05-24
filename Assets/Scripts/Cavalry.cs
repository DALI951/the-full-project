using UnityEngine;

public class Cavalry : Unit
{
    [SerializeField] private float speedMultiplier = 1.8f;

    protected override void Awake()
    {
        unitName        = "Cavalry";
        unitType        = "Cavalry";    // distinct — double-click selects only Cavalry
        unitDescription = "Fast mounted unit. Excels at flanking.";
        maxHealth       = 150;
        baseSpeed       = 3.5f;
        attackDamage    = 25;
        attackRange     = 1.1f;
        attackCooldown  = 1.5f;
        base.Awake();
    }

    protected override void Start()
    {
        base.Start();
        if (agent != null) agent.speed = baseSpeed * speedMultiplier;
    }

    public override void RestoreSpeed()
    {
        if (agent != null) agent.speed = baseSpeed * speedMultiplier;
    }

}
