# Changelog

## 2026-07-01

### [Fixed] Villagers not responding to move commands

- **File:** `Assets/Scripts/Villager.cs`
- **Change:** Wrapped the `targetNode == null` and `targetSite == null` checks in `Update()` with state guards
- **Problem:** Villagers could be selected but right-clicking the ground did nothing. Infantry moved normally.
- **Reason:** `Villager.Update()` checked `targetNode == null` every frame. When true (always the case when not gathering), it forced the state to `Idle`, which called `agent.ResetPath()` — cancelling any movement command. The fix only resets to Idle when the villager was actually in a state that depends on targetNode/targetSite (MovingToResource, Gathering, AttackingAnimal, MovingToBuild, Building).

### [Reverted] Multiple changes that caused regressions

The following changes were attempted but later reverted via `git checkout` because they caused new issues:
- FBX animation loop/root motion settings on villager & infantry clips
- Building FBX `normalImportMode: 0 → 1` (normals did not fix invisible walls)
- `agent.velocity` movement detection (caused villager to fall through ground)
- Position delta threshold reduction (0.001 → 0.0001) — also reverted to isolate the cause
