# HANDOFF.md — MinAge

> State of the work for whoever picks this up next (other device, other chat, future you).
> Rewrite it, don't append. Body may be `Nothing pending.` — the file always exists.

Last updated: 2026-11-09 23:45 · by: Antigravity (DESKTOP-OJME2E2)

## Current State & Completed Work

### AoE4 Visual & Architectural Overhaul (High-Detail 3D Models, 17 PBR Materials, Instanced Grass & Post-Processing)

The project has undergone a complete graphical, architectural, and visual overhaul to bring its visual fidelity in line with Age of Empires IV. All 8 building prefabs now feature authored high-polygon 3D `.obj` models (4,000–7,920 double-sided triangles each), 17 tangent-space normal-mapped PBR materials, procedural cobblestone courtyard aprons, GPU-instanced swaying meadow grass, and full-screen atmospheric post-processing.

Verified in Unity 2022.3.62f2 Play Mode with **0 compile errors, 0 runtime errors, and 0 exceptions**.

---

### Critical Visual Bug Resolutions (Verified in `aoe4_pbr_showcase_fixed.png`)
1. **Sloped Roofs Backface Culling Fixed (`AoE4_*.obj`)**:
   - Sloped terracotta, crimson, and slate roofs on Town Center, Barracks, Market, Farm, and LumberMill were previously invisible from the top-down RTS camera due to inverted triangle winding orders getting culled by Unity's Standard shader.
   - Ran automated geometric processor across all 8 `Assets/Models/AoE4Buildings/AoE4_*.obj` files:
     - Checked geometric cross-product normal $N_g$ vs. average vertex normal $N_v$.
     - Re-wound all outward faces to guarantee positive dot product with $N_v$ and outward front-facing orientation.
     - Generated duplicate backfaces with reversed vertex winding and negated normal vectors (`-nx, -ny, -nz`).
     - Every roof, gable wall, windmill sail, market canopy, and dormer is now 100% solid, opaque, and visible from all camera angles.
2. **Upright Tree Trunks (`Tree.prefab`, `ProjectAutoFixer.cs`)**:
   - `Tree.prefab` previously had detached horizontal trunks lying on the ground because `NormalizeTreePrefab` forced `visualChild.localRotation = Quaternion.identity` on an FBX asset authored with a -90° X axis.
   - Updated `NormalizeTreePrefab` to set `visualChild.localRotation = Quaternion.Euler(-90f, 0f, 0f)`.
   - Verified bounds: `2.43W x 3.20H x 2.36D | Bottom Y: 0.00` with the brown trunk standing vertically directly beneath the green canopy.
3. **Clean Black Fog Shroud without Stray Grass Dots (`AtmosphericVisuals.cs`)**:
   - `AtmosphericVisuals.RenderInstancedGrass()` now checks `FogOfWar.Instance.IsExplored(pos)` for every grass instance before adding it to the GPU batch.
   - Unexplored terrain is enveloped in a pitch-black, silky smooth fog shroud with zero green grass dots protruding.

---

### 1. High-Detail 3D Medieval Models (`Assets/Models/AoE4Buildings/`)
- **8 Authored `.obj` + `.mtl` Models**:
  - `AoE4_TownCenter.obj` $\rightarrow$ `Assets/Prefabs/HomeSite.prefab`: Two-story Tudor town center with stone foundations, white plaster, dark timber framing, multi-flue stone chimney, terracotta sloped roof, dormer windows, and dark arched portals. (Bounds: 4.80W x 3.50H x 3.97D).
  - `AoE4_Barracks.obj` $\rightarrow$ `Assets/Prefabs/Barracks.prefab`: Heavy medieval barracks with timber bracing, crimson pitched roof, dormer gables, weapons racks, and overhanging upper floor. (Bounds: 3.80W x 3.12H x 2.80D).
  - `AoE4_Farm.obj` $\rightarrow$ `Assets/Prefabs/Farm.prefab`: Authentic medieval agricultural site with octagonal timber-framed windmill, four lattice sail blades, field fence, and harvest baskets. (Bounds: 3.18W x 3.27H x 3.20D).
  - `AoE4_LumberMill.obj` $\rightarrow$ `Assets/Prefabs/LumberMill.prefab`: Open-sided timber processing mill with log piles, cross-cut sawing trestles, overhead crane beam, and wooden roof canopy. (Bounds: 3.20W x 2.26H x 2.15D).
  - `AoE4_Market.obj` $\rightarrow$ `Assets/Prefabs/Market.prefab`: Open bazaar square with timber vendor stalls, crimson-and-blue fabric striped canopies, and merchant crates. (Bounds: 3.20W x 2.77H x 2.17D).
  - `AoE4_Watchtower.obj` $\rightarrow$ `Assets/Prefabs/Tower.prefab`: Crenellated stone watchtower with machicolations, corner watch-turrets, arrow loops, crimson hipped roof, and royal blue heraldic banner. (Bounds: 2.00W x 4.00H x 1.27D).
  - `AoE4_StoneWall.obj` $\rightarrow$ `Assets/Prefabs/Wall.prefab`: Modular crenellated stone curtain wall with battlements, soldier walkway (banquette), and stone buttresses. (Bounds: 2.20W x 2.20H x 0.75D).
  - `AoE4_GoldMine.obj` $\rightarrow$ `Assets/Prefabs/GoldMine.prefab`: Solid rocky outcrop with carved quarry face, exposed sparkling metallic gold ore veins, pickaxes, and timber mine shoring. (Bounds: 1.96W x 1.40H x 2.20D).

---

### 2. 17 PBR Normal-Mapped Materials (`Assets/Materials/AoE4PBR/`)
Created by `PBRAndEnvironmentUpgrader.UpgradeMaterialsAndTerrain()`, using 12 tileable albedo textures and tangent-space normal maps (`Assets/Textures/PBR/`):
- `StoneWall.mat`: Chiseled ashlar stone blocks with high normal relief (`_BumpScale = 1.1`).
- `TimberFrame.mat`: Dark aged oak timber beams with wood grain normal mapping.
- `TudorPlaster.mat`: Off-white hand-troweled stucco plaster.
- `TerracottaRoof.mat`: Overlapping clay roof tiles with warm orange-red tone.
- `CrimsonRoof.mat`: Deep crimson glazed ceramic roof shingles.
- `SlateRoof.mat`: Dark blue-grey stone slate tiles with subtle specular sheen.
- `GoldOre.mat`: Metallic gold mineral veins embedded in rock (`_Metallic = 0.65`, `_Glossiness = 0.45`).
- `CliffRock.mat`: Natural fractured granite stone.
- `GoldenWheat.mat`: Golden wheat stalk thatch.
- `DarkSoil.mat`: Rich fertile dark tilled soil.
- `BarkWood.mat`: Rough tree bark.
- `CutWood.mat`: Freshly cut planed lumber beams.
- `CrimsonFabric.mat`: Crimson woven cloth fabric for market canopies.
- `RoyalBlueFabric.mat`: Royal blue dyed fabric for heraldic banners.
- `GoldTrim.mat`: Polished gold metal trim and accents (`_Metallic = 0.85`, `_Glossiness = 0.60`).
- `DarkArch.mat`: Deep shadowed recessed stone masonry for archways and doorways.
- `CourtyardCobble.mat`: Rounded river stone cobblestones with mossy grout lines.

---

### 3. Environment, Terrain & Atmospheric Polish (`AtmosphericVisuals.cs`)
- **PBR Meadow Terrain**: Applied `aoe4_meadow_albedo.png` and `aoe4_meadow_normal.png` to the Unity Terrain Data via `AoE4MeadowTerrainLayer.terrainlayer` with `(45, 45)` tiling, generating a lush, tactile grassland across the entire $200 \times 200\text{ m}$ battlefield.
- **PBR Cobblestone Courtyard Aprons**: `AtmosphericVisuals` automatically spawns grounded cobblestone courtyard discs under all buildings (radius scaled per structure: 9.5m for TC, 5.5m for military, 4.8m for farms), integrating structures into the landscape.
- **GPU-Instanced 3D Grass**: Renders GPU-instanced 3D swaying meadow grass tufts and wildflower stalks in a single `Graphics.DrawMeshInstanced` draw call, dynamically excluding building footprints and resource nodes, strictly rendered only inside explored territory.
- **Camera Post-Processing (`AoE4PostProcess.shader`, `AoE4CameraPostProcess.cs`)**: Screen-space ambient occlusion, warm cinematic color grading, soft bloom highlights, and subtle edge vignetting.

---

### 4. Normalized Geometry & Proportions (`ProjectAutoFixer.cs`)
All prefabs pass the automated bounds and scale normalizer:
- **Villager**: `0.76W x 1.75H x 0.60D | Bottom Y: 0.00`
- **Builder**: `0.76W x 1.75H x 0.60D | Bottom Y: 0.00`
- **Infantry**: `0.90W x 1.85H x 0.59D | Bottom Y: 0.00`
- **Cavalry**: `0.58W x 2.20H x 2.40D | Bottom Y: 0.00`
- **Animal**: `0.25W x 0.85H x 0.95D | Bottom Y: 0.00`
- **Tree**: `2.43W x 3.20H x 2.36D | Bottom Y: 0.00`
- **GoldMine**: `1.96W x 1.40H x 2.20D | Bottom Y: 0.00`
- **HomeSite**: `4.80W x 3.50H x 3.97D | Bottom Y: 0.00`
- **Barracks**: `3.80W x 3.12H x 2.80D | Bottom Y: 0.00`
- **Farm**: `3.18W x 3.27H x 3.20D | Bottom Y: 0.00`
- **LumberMill**: `3.20W x 2.26H x 2.15D | Bottom Y: 0.00`
- **Market**: `3.20W x 2.77H x 2.17D | Bottom Y: 0.00`
- **Tower**: `2.00W x 4.00H x 1.27D | Bottom Y: 0.00`
- **Wall**: `2.20W x 2.20H x 0.75D | Bottom Y: 0.00`
- **ConstructionSite**: `2.50W x 0.15H x 2.50D | Bottom Y: 0.00`
- **Flag**: `0.36W x 0.80H x 0.04D | Bottom Y: 0.00`

---

### 5. Automated Build Pipeline & Tools
- `MedievalModelBuilder.RebuildAllModels()`: Automatically re-binds all 8 `.obj` models, wires their 17 PBR materials, strips child colliders, and links `ResourceNode.visualRoot`.
- `ProjectAutoFixer.FixAll()`: Complete one-click pipeline running PBR upgrades, animator repairs, model re-binding, scale normalization, and reporting.
- `AIBridgeServer`: HTTP bridge on port 9876 with routes `/health`, `/refresh`, `/fixall`, `/logs`, `/logs/clear`, `/playmode`, `/debug/showcase`, `/capture/game`, `/capture/scene`.

---

### 6. Visual Verification
- **Verified Showcase Screenshot**: `C:\Users\Dali\.gemini\antigravity\brain\ebcbb35f-e44d-4ad4-a850-92b76c447506\aoe4_pbr_showcase_fixed.png`.
- Displays the complete medieval settlement:
  - Vibrant terracotta clay tiled roofs and crimson pitched barracks roofs with dormers and chimneys.
  - Upright pine trees with vertical brown trunks embedded in the soil.
  - Silky-black clean fog of war shroud without a single stray grass artifact.
  - Crenellated stone watchtowers and matching stone curtain walls.
  - Proportional human-scale villagers, infantry in full plate with shields/swords, and mounted cavalry.
