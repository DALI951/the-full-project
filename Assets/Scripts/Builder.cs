using UnityEngine;
using System.Collections;

/// <summary>
/// Builder — constructs buildings, cannot gather resources.
/// Undying: at 1 HP goes immobile, heals 1 HP/s, revives when an ally gets close.
/// Cannot be selected by the player.
/// </summary>
public class Builder : Villager
{
    [Header("Undead")]
    [SerializeField] private float revivalRange = 3f;

    private bool      _isDown       = false;
    private Coroutine _healRoutine;

    protected override void Awake()
    {
        unitName        = "Builder";
        unitType        = "Builder";
        unitDescription = "Constructs buildings. Cannot gather. Undying.";
        maxHealth       = 70;
        baseSpeed       = 2.8f;
        attackDamage    = 5;
        attackRange     = 1.5f;
        attackCooldown  = 2.5f;
        base.Awake();
    }

    // ── Unselectable ──────────────────────────────────────────────────
    public override bool IsSelectable => false;

    // ── Cannot gather ─────────────────────────────────────────────────
    public override void GatherFrom(ResourceNode node) { }

    // ── Undead TakeDamage ─────────────────────────────────────────────
    public override void TakeDamage(int amount, Unit damageSource = null)
    {
        if (_isDown) return;                        // immune while down
        int projected = currentHealth - amount;
        if (projected <= 1)
        {
            currentHealth = 1;
            if (!_isDown) GoDown();
        }
        else
        {
            base.TakeDamage(amount, damageSource);
        }
    }

    // ── Override Die — never truly dies ───────────────────────────────
    protected override void Die()
    {
        if (!_isDown) GoDown();
        // intentionally no base.Die()
    }

    // ── Down state ────────────────────────────────────────────────────
    private void GoDown()
    {
        _isDown = true;
        ClearWaypoints();
        if (agent != null) { agent.isStopped = true; agent.ResetPath(); }
        if (_healRoutine != null) StopCoroutine(_healRoutine);
        _healRoutine = StartCoroutine(HealAndReviveLoop());
    }

    private IEnumerator HealAndReviveLoop()
    {
        while (_isDown)
        {
            yield return new WaitForSeconds(1f);
            if (!_isDown) yield break;

            if (currentHealth < maxHealth) currentHealth++;

            if (HasNearbyAlly()) { Revive(); yield break; }
        }
    }

    private bool HasNearbyAlly()
    {
        if (UnitSelectionManager.Instance == null) return false;
        foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
        {
            if (u == null || u == this) continue;
            if (u.OwnerPlayerId != OwnerPlayerId) continue;
            if (Vector3.Distance(transform.position, u.transform.position) <= revivalRange)
                return true;
        }
        return false;
    }

    private void Revive()
    {
        _isDown = false;
        if (_healRoutine != null) { StopCoroutine(_healRoutine); _healRoutine = null; }
        if (agent != null) { agent.isStopped = false; }
        currentHealth = Mathf.Max(currentHealth, maxHealth / 4);
    }

    protected override void OnBeforeDestroy() { }
}