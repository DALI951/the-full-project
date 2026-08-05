# Mini Age

A **multiplayer real-time strategy (RTS) game** built with Unity 2022.3.62f2 (LTS) and [Mirror](https://mirror-networking.gitbook.io/) networking. Inspired by Age of Empires — gather resources, build an economy, train military units, and destroy your enemy.

---

## Features

### Gameplay
- **3 resources**: Food, Wood, Gold — gathered from animals, trees, and mines
- **Units**: Villagers (gatherers/builders), Infantry, Cavalry, Builders
- **Buildings**: HomeSite (TC), Barracks, Farm, LumberMill, Market, Tower, Wall
- **Resource buildings**: Assign villagers to generate passive income
- **Fog of War**: Texture-based visibility system (unexplored / explored / visible)
- **Minimap**: Top-down camera + unit/building/enemy dots with fog overlay
- **Formation movement**: Move commands group by type and match slowest speed

### Multiplayer (Mirror)
- **LAN support**: Custom UDP discovery on port 47778 (no internet required)
- **Lobby**: Up to 8 players, team selection, color picker, ready states, 5s countdown
- **Server-authoritative**: All actions validated server-side via `[Command]`
- **Bot support**: AI bots can fill empty player slots
- **Disconnect handling**: Clean removal of disconnected player's units/buildings

### AI Opponent
- **`EnemyAI`** — full CPU opponent with timed decision cycles
  - Economy phase (train villagers, build farms/mills/markets)
  - Military buildup (train infantry/cavalry, respect pop cap)
  - Attack waves with cavalry flanking + infantry frontal approach
  - Defense (build towers, recall defenders)
  - Scouting (send first unit to find enemy base)
  - **Adaptive difficulty**: Tracks games played, adjusts 10 difficulty levels

### Controls
| Input | Action |
|---|---|
| Left-click | Select unit/building/resource |
| Left-drag | Box-select multiple units |
| Right-click | Move / attack / gather / build |
| Double-click unit | Select all units of same type |
| Shift+click | Add/remove from selection |
| WASD / Edge-scroll | Pan camera |
| Scroll wheel | Zoom |
| Escape | Pause menu |

---

## Project Architecture

### Tech Stack
| Component | Technology |
|---|---|
| Engine | Unity 2022.3.62f2 LTS |
| Networking | Mirror v96.0.1 |
| UI | uGUI + TextMeshPro |
| Pathfinding | Unity NavMesh / AI Navigation 1.1.7 |
| Post-processing | Unity Post Processing Stack v2 |
| Serialization | Newtonsoft.Json, System.Xml |
| AI Tools | OpenCode MCP + Meshy.ai bridge + Custom AI Unity Bridge |

### Script Architecture (59 scripts)

```
NetworkBehaviour
├── Unit (NavMeshAgent)
│   ├── Villager ─── Builder
│   ├── Infantry
│   └── Cavalry
├── Building
│   ├── HomeSite
│   └── Barracks
├── ConstructionSite
├── NetworkedPlayer (resources, commands)
├── LobbyPlayer (lobby state)
├── GameOverManager
├── FogOfWar
└── RTSNetworkManager

MonoBehaviour
├── ResourceNode ─── TreeNode / MineNode / AnimalNode
├── ResourceBuilding (Farm, LumberMill, Market)
├── EnemyAI (full AI opponent)
├── RTSCamera + MinimapSystem + MinimapCamera
├── SelectionManager + UnitSelectionManager + UnitSelectionBox
├── BuildingPlacer + BuildingSpawner + ResourceSpawner
├── 16 UI scripts (GameUI, LobbyUI, MainMenuUI, etc.)
└── Support: AudioManager, EffectManager, ScreenShake, etc.
```

### Scenes
| Scene | Purpose |
|---|---|
| `MainMenu` | Main menu, host/join lobby, LAN browser, settings |
| `GameScene` | RTS gameplay with fog of war, minimap, all HUD |

---

## Getting Started

### Requirements
- Unity Hub + Unity 2022.3.62f2
- Git LFS (if cloning 3D model assets)

### Setup
1. Open project in Unity 2022.3.62f2
2. Open `Scenes/MainMenu` — this is the entry scene
3. Press Play (Host mode for single-player + AI)

### First Launch
- Click **Single Player** → starts a local game vs AI
- Click **Host** → creates a LAN server
- Click **Join** → enter IP or browse LAN servers
- Configure settings (quality, resolution, fullscreen)

---

## Project Status

### What's Complete
- Full multiplayer lobby + match flow
- Resource economy (gathering, buildings, population)
- Military system (2 unit types, auto-combat, formations)
- AI opponent with adaptive difficulty
- Fog of war + minimap
- Building placement with ghost preview
- Selection framework (click, box, double-click, shift)
- All UI panels functional

### Known Issues
1. **Duplicate win/lose logic** — `EnemyAI.CheckWinLose()` and `GameOverManager.CheckGameOver()` both independently check game-over; can double-trigger
2. **Audio hooks defined but unused** — `AudioManager` has slots for all sounds but no gameplay code calls `PlayUnitAttack()`, `PlayBuildingComplete()`, etc.
3. **EffectManager effects unused** — particle effects defined but never invoked from gameplay
4. **Resource refund race condition** — `CmdPlaceBuilding` spends resources before validating the prefab; server crash between spend and refund loses resources
5. **ScreenShake conflicts with RTSCamera** — modifies `transform.localPosition`, jittering the camera
6. **Hardcoded balance values** — unit stats, costs, population limits, gathering rates scattered across scripts with no central config
7. **Missing `OnDestroy` cleanup** — `ResourceNode.DepleteClientSide()` doesn't remove from `RegisteredNodes`, causing potential null references
8. **UI only supports 2 training buildings** — `GameUI` assumes at most HomeSite + Barracks

---

## Recommended Changes & Upgrades

### Priority Fixes
- [ ] Consolidate win/lose checking to a single authority (remove from `EnemyAI`)
- [ ] Wire up `AudioManager` calls into gameplay code (attack, gather, build, death sounds)
- [ ] Wire up `EffectManager` calls into gameplay code (hit, death, build effects)
- [ ] Fix `CmdPlaceBuilding` spend-then-refund pattern — validate before spending
- [ ] Fix `ScreenShake` to not directly modify camera transform

### Gameplay Improvements
- [ ] **Central balance config** — create a `GameBalance` ScriptableObject for all unit stats, costs, gathering rates, population caps
- [ ] **Unit control groups** — Ctrl+1/2/3 for selection groups
- [ ] **Patrol command** — both for player units and polish AI patrol
- [ ] **Cancel queue** — allow canceling individual queued units in buildings
- [ ] **In-game chat** — `TargetReceiveChat` exists but has no UI hook
- [ ] **Walls with NavMesh cutting** — walls that block pathfinding properly

### Content Expansion
- [ ] **Tech tree / upgrades** — unit upgrades, building upgrades, economy bonuses
- [ ] **More unit types** — archers, siege weapons, scouts
- [ ] **More buildings** — blacksmith, stable, archery range
- [ ] **Map variety** — procedural terrain, water, elevation, different sizes
- [ ] **Multi-biome maps** — forests, deserts, snow
- [ ] **Save/Load** — mid-match persistence

### Polish
- [ ] **Sound effects** + background music (hooks ready, just need audio clips)
- [ ] **Building destruction animations** — current buildings just disappear
- [ ] **Resource auto-gather feedback** — show villagers working inside resource buildings
- [ ] **Minimap improvements** — show player colors on minimap camera
- [ ] **Team auto-balance** — auto-assign teams when players join
- [ ] **Build-queue cancellation** — cancel individual queued units

### Technical Debt
- [ ] Clean up `EnemyAI` duplicate iteration patterns (use dirty-flag tracking)
- [ ] Remove unused imports across all scripts
- [ ] Move all hardcoded strings to constants
- [ ] Add proper `[RequireComponent]` attributes where missing
- [ ] Remove `MeshyAssembly` / unused plugin DLLs if not needed for final build

---

## File Structure

```
F:\mini age\
├── Assets/
│   ├── AIUnityBridge/Editor/     # AI agent HTTP bridge (port 9876)
│   ├── Animations/               # Animator controllers
│   ├── Editor/                   # Editor tools (MainMenuBuilder, AutoSave)
│   ├── Mirror/                   # Mirror networking v96.0.1
│   ├── Models/                   # FBX models (buildings, units, animals)
│   ├── Plugins/NuGet/            # NuGet DLLs (SignalR, MCP, R3, etc.)
│   ├── Prefabs/                  # All game prefabs (68 total)
│   ├── Scenes/                   # MainMenu + GameScene
│   ├── Scripts/                  # 59 C# scripts
│   ├── Packages/ai.meshy/        # Meshy.ai 3D import plugin
│   └── Textures/                 # Building/unit textures
├── Packages/                     # Unity package manifests
├── ProjectSettings/              # Unity project configuration
└── README.md
```

---

## Dependencies

### Unity Packages
| Package | Version |
|---|---|
| com.unity.ai.navigation | 1.1.7 |
| com.unity.postprocessing | 3.5.0 |
| com.unity.textmeshpro | 3.0.7 |
| com.unity.ugui | 2.0.0 |
| com.unity.nuget.newtonsoft-json | 3.2.1 |

### Third-Party
| Package | Version | Source |
|---|---|---|
| Mirror | 96.0.1 | Asset Store |
| com.ivanmurzak.unity.mcp | 0.82.3 | OpenUPM |
| ai.meshy | 0.2.1 | Asset Store |

### Built-in dependencies
- `UnityEngine.AI` — NavMeshAgent pathfinding
- `System.Net.Sockets` — UDP LAN discovery
- `System.Xml` — Settings serialization
- `UnityEngine.Networking` — Mirror's underlying transport

---

## License

Private project — all rights reserved.
