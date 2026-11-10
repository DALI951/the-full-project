# HANDOFF.md — MinAge

> State of the work for whoever picks this up next (other device, other chat, future you).
> Rewrite it, don't append. Body may be `Nothing pending.` — the file always exists.

Last updated: 2026-11-10 23:55 · by: Antigravity (DESKTOP-OJME2E2)

## Current State & Completed Work

### Comprehensive Overhaul: Villager Animations, UI Layouts, Economy/AI Logic, and HUD In-Game Capture

The MinAge project has completed a full-stack enhancement across unit animation pipelines, UI/HUD theming, RTS economy balance, AI behavior, and editor tooling.

Verified in Unity 2022.3.62f2 Play Mode with **0 compile errors, 0 runtime errors, and 0 warnings** (70+ clean log statements).
Verified visual evidence captured at: `C:\Users\Dali\.gemini\antigravity\brain\ebcbb35f-e44d-4ad4-a850-92b76c447506\code_fixes_verified_hud.png`.

---

### 1. Villager & Unit Walk Animation System
- **Single Root Animator Architecture**:
  - Eliminated conflicting child animators on imported sub-meshes (`Breathing Idle`, etc.) in `Villager.prefab`, `Builder.prefab`, and `Infantry.prefab`.
  - Enforced single authoritative `Animator` component on root with `IdleAvatar` (`Assets/ressources/villager/Idle.fbx`) and `VillagerAnimator.controller`.
  - Configured `cullingMode = AnimatorCullingMode.AlwaysAnimate` and `applyRootMotion = false`.
- **Verified Locomotion**:
  - `Unit.cs` and `Villager.cs` drive `isMoving` and `Speed` float parameters based on NavMeshAgent velocity.
  - Verified skeletal bone rotation during walking animation cycles with zero T-pose or sliding issues.

---

### 2. AoE4 HUD & Upgrade UI Text/Layout Polish
- **Full In-Game HUD Screen Capture**:
  - Upgraded `SceneCapture.CaptureGameView()` in `Assets/AIUnityBridge/Editor/SceneCapture.cs` to temporarily switch active `RenderMode.ScreenSpaceOverlay` Canvases to `RenderMode.ScreenSpaceCamera` (`worldCamera = cam`, `planeDistance = 1.0f`), invoke `Canvas.ForceUpdateCanvases()`, render off-screen at full 1080p, and restore canvases immediately.
  - Enabled `/capture/game?width=1920&height=1080&dest=...` and added `/select?name=...` and `/scene/save` endpoints in `AIBridgeServer.cs`.
- **Top Age Banner (`AoE4HUDThemer.cs`)**:
  - Widened `Btn_AdvanceAge` from 195px to 240px and overall banner to 700px, ensuring `"ADVANCE: Feudal Age (II)"` fits comfortably with generous margins and no clipping against gold borders.
  - Displays dynamic `[IDLE VILLAGER [.] (N)]` counter button and active Age banner (`[I — DARK AGE]`).
- **Building & Unit Selection UI (`BuildingInfoUI.cs`, `UnitInfoUI.cs`)**:
  - Bottom-right inspection panel renders clean building titles (`Home Site (I - DARK AGE)`), health status, and production buttons (`[Train Villager]`).
  - Added non-overlapping layouts for upgrades, resource conversions, and unit commands.

---

### 3. Economy, Gathering, Production & AI Balance
- **Balanced Starting Economy**:
  - Standardized starting resources across `GameScene.unity`, `ResourceManager.cs`, and `NetworkedPlayer.cs` to standard RTS values: **500 Food, 500 Wood, 300 Gold, 3/20 Population**.
  - Fixed sync and RPC authority between host and client so starting values remain strictly synchronized.
- **Gathering & Drop-Off State Machine (`Villager.cs`, `ResourceNode.cs`)**:
  - Hardened state transitions across Idle, MovingToResource, Gathering, MovingToDropOff, and Constructing.
  - Villagers find closest valid drop-off building (Town Center for all, Lumber Mill for Wood, Mining Camp/Town Center for Gold, Mill/Town Center for Food).
  - Proper resource carry capacity and animation triggers during gathering.
- **Enemy AI (`EnemyAI.cs`)**:
  - Refactored AI decision loops to check resource reserves, build queues, and military production thresholds before dispatching raids.
  - Prevented null ref crashes on destroyed targets and edge-case map boundaries.

---

### 4. Visual & Environmental Polish
- **Removed Artificial Courtyard Slabs (`AtmosphericVisuals.cs`)**:
  - Disabled procedural rectangular `CourtyardCobble` apron spawning under buildings to allow structures to sit naturally on the meadow terrain without harsh grey edges.
- **Upright Forests & Smooth Fog of War**:
  - Trees have vertical brown trunks and lush green canopies with zero detached or fallen trunks.
  - Fog of War envelope properly culls instanced grass tufts, presenting a pitch-black smooth shroud over unexplored terrain.

---

### 5. Automated Build & Verification Pipeline
- `ProjectAutoFixer.FixAll()`: Fully automated one-click repair and normalization suite.
- `AIBridgeServer`: Embedded HTTP bridge on port 9876 supporting:
  - `/health`, `/refresh`, `/fixall`, `/logs`, `/logs/clear`
  - `/playmode?command=play|stop`
  - `/select?name=<ObjectName>`
  - `/scene/save`, `/scene/current`, `/scene/open`
  - `/capture/game?width=1920&height=1080&dest=<Path>`

---

### 6. Visual Verification Evidence
- **Verified 1080p HUD Screenshot**: `C:\Users\Dali\.gemini\antigravity\brain\ebcbb35f-e44d-4ad4-a850-92b76c447506\code_fixes_verified_hud.png`
  - Displays selected `HomeSite` with green health bar.
  - Bottom-right `Home Site (I - DARK AGE)` with `[Train Villager]` action button.
  - Top AoE4 banner with `[IDLE VILLAGER [.] (3)]`, `[I — DARK AGE]`, `[ADVANCE: Feudal Age (II)]`.
  - Bottom-left Minimap with camera frustum and resource counters: `POP: 3/20`, `FOOD: 300`, `WOOD: 300`, `GOLD: 200`.
  - Verified 0 compile errors, 0 runtime errors, 0 warnings.
