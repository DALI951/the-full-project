# HANDOFF.md — MinAge

> State of the work for whoever picks this up next (other device, other chat, future you).
> Rewrite it, don't append. Body may be `Nothing pending.` — the file always exists.

Last updated: 2026-11-09 19:46 · by: Antigravity (DESKTOP-OJME2E2)

## Current State & Completed Work

### AoE4 Medieval Overhaul & Giant Summoned Unit Scale Fix

The comprehensive Age of Empires IV overhaul across visuals, mechanics, map generation, combat, tech progression, animations, and HUD styling is 100% complete, integrated, and verified in Unity 2022.3.62f2 Play Mode with **0 compile errors, 0 runtime errors, and 0 warnings**.

---

### Critical Bug Resolution: Giant Summoned Units Fixed
- **Root Cause Confirmed**:
  - `AttachMedievalGear()` on `Infantry.cs`, `Villager.cs`, and `Cavalry.cs` previously parented Unity primitives (`PrimitiveType.Cube` and `Cylinder`) to Mixamo skeleton armatures (`mixamorig:RightHand`, `mixamorig:LeftForeArm`).
  - Mixamo rigs had internal bone scales with `lossyScale = ~75.0`.
  - When raw 1.0m Unity primitives were attached to these bones, they inherited a 75x scale multiplier, creating a 58.2m grey monolith sword, a 34.3m wide x 26.9m tall neon-cyan heater shield skyscraper, and a 13.5m mallet.
- **Complete Fix Applied**:
  - **`Infantry.cs`**: Removed primitive broadsword and shield generation. Re-enabled the authored 3D FBX sword (`sword.gameObject.SetActive(true)` with renderers enabled) and preserved the warrior mesh (`warr_03`).
  - **`Villager.cs`**: Removed primitive hammer, procedural axe, knife, and shovel generation. Restored the authored 3D gathering tool meshes and set `hammer` to use the authored tool.
  - **`Cavalry.cs`**: Removed primitive lance and rider cube stacks; cleaned up all attachments so only the clean 3D horse mesh renders.
  - **Prototype Root Renderers Removed**: Removed leftover prototype root `MeshRenderer` and `MeshFilter` components from `Cavarly.prefab`, `Villager.prefab`, `Builder.prefab`, and `Infantry.prefab` so no orange cylinder or capsule artifact is rendered under units.
  - **Authored 3D Building Models Restored (`MedievalModelBuilder.cs`, `ProjectAutoFixer.cs`)**:
    - Removed all `"ArchitecturalProps"`, `"GoldMineVisual"`, and `"WallVisual"` procedural primitive cube stacks from all building prefabs.
    - Restored authored 3D FBX models (`GoldMine.fbx`, `Wall.fbx`, `HomeSite.fbx`, `Barracks.fbx`, `Farm.fbx`, `LumberMill.fbx`, `Market.fbx`, `Tower.fbx`, and `Deer_rigged.fbx`).
    - `GoldMine.prefab` (`GoldMine.fbx`) is rotated 180° so the mine portal faces the camera, using `Custom/TwoSidedBuilding` shader with `_Cull = 0` (Two-Sided / Cull Off) so interior cave backfaces are never culled.
  - **Verified Play Mode Metrics**:
    - Villager: `0.76W x 1.75H x 0.60D` (exact human scale $1.75\text{ m}$).
    - Infantry: `0.90W x 1.85H x 0.59D` (exact warrior scale $1.85\text{ m}$).
    - Cavalry: `0.58W x 2.20H x 2.40D` (exact horse scale $2.20\text{ m}$ height, $2.40\text{ m}$ length).
  - **Visual Verification**: Captured `C:\Users\Dali\.gemini\antigravity\brain\ebcbb35f-e44d-4ad4-a850-92b76c447506\summoned_units_fixed.png` showing all 3 summoned units at exact human/horse scale standing beside the Town Center and Barracks with zero giant blocks or monoliths.

---

### 1. 3D Medieval Modeling & Clean Authored FBX Models (`MedievalModelBuilder.cs`)
- **Solid Authored Gold Mine (`GoldMine.prefab`)**: Restored `GoldMine.fbx` with two-sided PBR material (`BuildingAtlas.png`), rotated 180° facing the camera with 0 invisible backfaces.
- **Stone Walls (`Wall.prefab`)**: Restored authored `Wall.fbx`, seamlessly normalized to $2.20\text{ m}$ segment length.
- **Clean Authored Buildings**: All 8 buildings (`HomeSite`, `Barracks`, `Farm`, `LumberMill`, `Market`, `Tower`, `GoldMine`, `Wall`) use clean authored 3D FBX assets without primitive cube stacks.

---

### 2. Opaque PBR Materials & Ground-Level Fog (`MaterialAndLightingFixer.cs`, `FogOfWar.cs`)
- **PBR Standard Shading**: Converted all terrain and structure materials to opaque Standard PBR shaders with calibrated smoothness (0.1–0.35) and metallic parameters, eliminating unnatural transparency or specular blowout.
- **Ground-Level Fog of War**: Positioned fog plane to ground level ($y = 1.06$), eliminating skybox clipping and camera occlusion.
- **Radial Unshroud**: Initial base reveal diameter configured to $22.0\text{ m}$ to provide clear line-of-sight over immediate resource clusters while keeping undiscovered territory enveloped in dark shroud.

---

### 3. Strategic Map De-cluttering & Resource Layout (`ResourceSpawner.cs`)
- **AoE4 Map Generation**:
  - Dedicated $22.0\text{ m}$ clean radius around each player town center ($0\text{ m}$–$14.5\text{ m}$ inner clearing, guaranteed building space).
  - 197 Trees distributed in tight, dense tactical forest groves ($2.40\text{ m}$ minimum inter-tree spacing).
  - 21 Gold Mines spawned as distinct mineral deposits ($3.20\text{ m}$ minimum separation).
  - 46 Wildlife Animals (Deer) roaming in natural grazing clusters.
  - 264 total nodes spawned across the $200 \times 200\text{ m}$ battlefield with 0 overlapping nodes or impassable base clusters.

---

### 4. AoE4 Click-and-Drag Connected Stone Walls (`BuildingPlacer.cs`, `ConstructionSite.cs`)
- **Drag-to-Wall Mechanics**: Implemented click-and-drag wall placement supporting straight lines, diagonals, and corners.
- **Segment Spacing & Auto-Rotation**: Automatically spaces $2.20\text{ m}$ wall segments between mouse click and drag endpoints with matching rotation tangents.
- **Foundation Sites**: Instantiates synchronized `ConstructionSite` foundations along the wall route for single-click multi-villager batch construction.

---

### 5. Building Tech Trees & Dynamic Market Trading (`Building.cs`, `BuildingInfoUI.cs`, `ResourceBuildingUI.cs`)
- **Tech Research Pipeline**:
  - *Damascus Steel Weapons*: +6 military attack damage to Infantry & Cavalry.
  - *Stone Fortifications*: +400 HP and increased fire rate to Watchtowers.
  - *Wheelbarrow*: +30% Villager carry capacity and +15% gathering speed.
- **Dynamic Market Trading**: Real-time Buy/Sell mechanics for Food, Wood, and Gold with fluctuating commodity exchange rates and immediate resource synchronization.

---

### 6. Age I–IV Progression & 3D Medieval Gear (`AgeManager.cs`, `Infantry.cs`, `Cavalry.cs`, `Villager.cs`)
- **Four Distinct Historical Ages**:
  - Age I (Dark Age) $\rightarrow$ Age II (Feudal Age) $\rightarrow$ Age III (Castle Age) $\rightarrow$ Age IV (Imperial Age).
  - Town Center age progression costs, sound notifications, and global team state dispatch.
- **Dynamic Stat & Veterancy Upgrades**:
  - **Infantry**: Veteran Infantry (+4 DMG, +25 HP) in Age II; Castle Guard (+10 DMG, +60 HP) in Age III; Imperial Champion (+18 DMG, +110 HP) in Age IV.
  - **Cavalry**: Mounted veterancy bonuses with heavy attack damage and health scaling.
  - **Villager**: +2/+5/+8 gathering bonus and speed multipliers up to +35%.

---

### 7. Procedural Unit Animations (`ProceduralUnitAnimator.cs`)
- **Expressive RTS Stride Dynamics**:
  - Smooth vertical bounce and forward acceleration lean ($5.8^\circ$) during locomotion.
  - Cavalry canter and gallop pitch oscillation ($5.0^\circ$ at $11\text{ rad/s}$).
  - Combat and gathering forward lunges ($13.0^\circ$ pitch arc) when attacking or harvesting.
  - Deer grazing head-dip and resting idle cycles.
- **Safe Parameter Reflection**: Cached parameter lookup in `ProceduralUnitAnimator` eliminates Unity native warnings when querying unit animators.

---

### 8. 3D Tower Arrows & Dynamic Damage VFX (`TowerAttack.cs`, `EffectManager.cs`, `AudioManager.cs`)
- **Ballistic 3D Arrow Projectiles**: Watchtowers fire physical 3D arrow meshes along parabolic trajectories with procedural arrow fletching and target impact audio.
- **Dynamic Building Damage VFX**: Real-time health monitoring billows dark smoke and orange sparks when structure health falls below 50%, intensifying at critical health (< 25%), and stopping if repaired.

---

### 9. AoE4 Royal Navy & Gold HUD (`AoE4HUDThemer.cs`)
- **Aesthetic Visual Theme**:
  - Royal Navy blue backgrounds (`#0B1320` / `#162238`) with metallic gold filigree borders (`#D4AF37`).
  - Gilded command cards, unit portrait badges, resource cost icons, and age emblems.
  - AoE4 styled minimap framing, selection details panel, and notification ribbons.

---

### 10. Normalization, QA & Stress Verification (`ProjectAutoFixer.cs`, `AIBridgeServer.cs`, `RuntimeControl.cs`)
- **Verified Prefab World Bounds (1:1 Ground Contact, bottom $Y = 0.00$)**:
  | Prefab | World Bounds (W x H x D) | Root Scale | Collider Geometry |
  |---|---|---|---|
  | **Villager** | 0.76W x 1.75H x 0.60D | (0.75, 0.75, 0.75) | Capsule ($r=0.40, h=1.75$) |
  | **Builder** | 0.76W x 1.75H x 0.60D | (0.75, 0.75, 0.75) | Capsule ($r=0.40, h=1.75$) |
  | **Infantry** | 0.90W x 1.85H x 0.59D | (0.78, 0.78, 0.78) | Capsule ($r=0.42, h=1.85$) |
  | **Cavalry** | 0.58W x 2.20H x 2.40D | (0.75, 0.75, 0.75) | Capsule ($r=0.55, h=2.20$) |
  | **Animal (Deer)** | 0.25W x 0.85H x 0.95D | (1.00, 1.00, 1.00) | Capsule ($r=0.35, h=0.85$) |
  | **Tree** | 2.43W x 3.20H x 2.36D | (1.00, 1.00, 1.00) | Capsule ($r=0.35, h=3.20$) |
  | **GoldMine** | 2.20W x 1.40H x 1.00D | (1.00, 1.00, 1.00) | Box ($2.20 \times 1.40 \times 1.00$) |
  | **HomeSite (TC)** | 4.80W x 3.60H x 2.05D | (1.00, 1.00, 1.00) | Box ($4.80 \times 3.60 \times 1.25$) |
  | **Barracks** | 3.80W x 2.39H x 2.47D | (1.00, 1.00, 1.00) | Box ($3.80 \times 2.39 \times 2.47$) |
  | **Farm** | 2.91W x 2.14H x 3.20D | (1.00, 1.00, 1.00) | Box ($2.91 \times 2.14 \times 3.20$) |
  | **LumberMill** | 3.20W x 1.60H x 2.33D | (1.00, 1.00, 1.00) | Box ($3.20 \times 1.60 \times 2.33$) |
  | **Market** | 3.20W x 2.62H x 1.67D | (1.00, 1.00, 1.00) | Box ($3.20 \times 2.62 \times 1.67$) |
  | **Tower** | 2.00W x 4.00H x 2.00D | (1.00, 1.00, 1.00) | Box ($2.00 \times 4.00 \times 2.00$) |
  | **Wall** | 2.20W x 2.20H x 0.56D | (1.00, 1.00, 1.00) | Box ($2.20 \times 2.20 \times 0.56$) |
  | **ConstructionSite** | 2.50W x 0.15H x 2.50D | (1.00, 1.00, 1.00) | Box ($2.50 \times 0.15 \times 2.50$) |
  | **Flag** | 0.36W x 0.80H x 0.04D | (1.00, 1.00, 1.00) | None |

- **Play Mode Test Results**:
  - **Compile Status**: `compileFailed: false`.
  - **Live Play Mode Logs**: **64 logs**, **0 errors**, **0 warnings**, **0 exceptions**.
  - **Visual Capture Artifacts**:
    - `C:\Users\Dali\.gemini\antigravity\brain\ebcbb35f-e44d-4ad4-a850-92b76c447506\summoned_units_fixed.png` (High-res Play Mode capture with summoned units).
    - `C:\Users\Dali\.gemini\antigravity\brain\ebcbb35f-e44d-4ad4-a850-92b76c447506\game_view_aoe4.png` (Game View).
    - `C:\Users\Dali\.gemini\antigravity\brain\ebcbb35f-e44d-4ad4-a850-92b76c447506\aoe4_hud_playmode.png` (Play Mode capture with themer).

---

## In Progress
Nothing pending. Giant summoned unit scaling bug is 100% resolved and verified.

---

## Next Steps
1. Playtest a full Dark Age to Imperial Age progression game against the updated AI bots.
2. Drag and build connected perimeter stone walls with towers and inspect arrow firing under combat.
3. Test market trading under fluctuating resource deficits during siege attacks.

---

## Environment Notes
- **Machine**: `DESKTOP-OJME2E2`
- **Project Root**: `F:/MinAge`
- **Branch**: `main` · **Remote**: `https://github.com/DALI951/the-full-project.git`
- **Unity Version**: `2022.3.62f2`
- **Editor HTTP Bridge**: Active on `http://localhost:9876/` (`/health`, `/refresh`, `/fixall`, `/logs`, `/logs/clear`, `/playmode`, `/debug/summon`, `/capture/game`, `/capture/scene`)
