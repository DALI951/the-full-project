# Mini Age — Script ↔ Component Relations

## Inheritance Hierarchy

```
MonoBehaviour
├── NetworkBehaviour
│   ├── Building
│   │   ├── HomeSite
│   │   └── Barracks
│   ├── Unit  [RequireComponent(NavMeshAgent)]
│   │   ├── Villager
│   │   │   └── Builder
│   │   ├── Infantry
│   │   └── Cavalry
│   ├── ConstructionSite
│   ├── RTSNetworkManager (→ NetworkManager)
│   ├── NetworkedPlayer
│   ├── LobbyPlayer
│   ├── GameOverManager
│   └── FogOfWar  [DefaultExecutionOrder(-100)]
├── ResourceNode
│   ├── TreeNode
│   ├── MineNode
│   └── AnimalNode
├── ResourceBuilding
├── ResourceManager
├── UnitSelectionManager
├── SelectionManager
├── BuildingPlacer
├── BuildingSpawner
├── ResourceSpawner
├── ResourceCullingManager
├── AudioManager
├── EffectManager
├── FogRevealer
├── MapBoundary  [DefaultExecutionOrder(-100)]
├── MoveFlag
├── FlagPrefabSetup
├── FloatingDamageText
├── GameUI
├── BuildingInfoUI
├── UnitInfoUI
├── ResourceInfoUI
├── ResourceUI
├── ResourceBuildingUI
├── BuildMenuUI
├── PlayerListUI
├── SelectionTypeBar
├── UnitSelectionBox
├── WinLoseUI
├── MainMenuUI
├── LobbyUI
├── PauseMenu
├── LoadingScreen
├── SettingsManager
├── LANDiscovery
├── GameModeManager
├── PlayerColorManager
├── MinimapSystem (→ IPointerDownHandler, IPointerClickHandler)
├── MinimapCamera  [RequireComponent(Camera)]
├── CameraViewportFit
├── RTSCamera
├── ScreenShake
├── UIClickDebugger
├── SpawnAreaManager
└── (enum) ResourceType
```

---

## Prefab → Attached Scripts

| Prefab | Scripts Attached |
|--------|------------------|
| `Animal.prefab` | `AnimalNode`, `ResourceNode` (via inheritance), `FogRevealer` |
| `Barracks.prefab` | `Barracks`, `Building` (via inheritance), `FogRevealer` |
| `Builder.prefab` | `Builder`, `Villager`, `Unit`, `FogRevealer`, `NavMeshAgent` |
| `Cavarly.prefab` | `Cavalry`, `Unit` (via inheritance), `FogRevealer`, `NavMeshAgent` |
| `ConstructionSite.prefab` | `ConstructionSite`, `NetworkBehaviour` |
| `EntryPrefab.prefab` | (UI entry, no custom script — only `TMP_Text`) |
| `Farm.prefab` | `ResourceBuilding`, `Building` (likely via `Building` on parent), `FogRevealer` |
| `Flag.prefab` | `FlagPrefabSetup` |
| `GoldMine.prefab` | `MineNode`, `ResourceNode` (via inheritance) |
| `HomeSite.prefab` | `HomeSite`, `Building` (via inheritance), `FogRevealer` |
| `Infantry.prefab` | `Infantry`, `Unit` (via inheritance), `FogRevealer`, `NavMeshAgent` |
| `LumberMill.prefab` | `ResourceBuilding`, `Building` |
| `Market.prefab` | `ResourceBuilding`, `Building` |
| `PlayerRowPrefab.prefab` | (UI row, no custom script) |
| `ServerRowPrefab.prefab` | (UI row, no custom script) |
| `SpawnButton.prefab` | (UI button, no custom script) |
| `SpawnMarker.prefab` | (visual marker, no custom script) |
| `Tower.prefab` | `Building` (likely), `FogRevealer` |
| `Tree.prefab` | `TreeNode`, `ResourceNode` (via inheritance) |
| `TypeLine.prefab` | (UI line, no custom script) |
| `Villager.prefab` | `Villager`, `Unit` (via inheritance), `FogRevealer`, `NavMeshAgent` |
| `Wall.prefab` | `Building` (likely), `FogRevealer` |

---

## Scene Placement — GameScene

All placed as direct scene GameObjects:

| Script | Typical GameObject |
|--------|--------------------|
| `MapBoundary` | "MapBoundary" |
| `GameUI` | "GameManager" |
| `SelectionManager` | "GameManager" |
| `ResourceUI` | "GameManager" |
| `ResourceManager` | "GameManager" |
| `UnitInfoUI` | "GameManager" |
| `PlayerColorManager` | "GameManager" |
| `ResourceInfoUI` | "GameManager" |
| `PauseMenu` | "GameManager" |
| `UnitSelectionManager` | "GameManager" |
| `UnitSelectionBox` | "GameManager" |
| `BuildMenuUI` | "GameManager" |
| `BuildingPlacer` | "GameManager" |
| `FogOfWar` | "FogOfWar" |
| `MoveFlag` | "GameManager" |
| `ResourceCullingManager` | "GameManager" |
| `EnemyAI` | "GameManager" |
| `ResourceBuildingUI` | "GameManager" |
| `WinLoseUI` | "GameManager" |
| `UIClickDebugger` | "GameManager" |
| `MinimapSystem` | "GameManager" |
| `RTSCamera` | "Main Camera" |
| `SpawnAreaManager` | "SpawnAreaManager" |
| `BuildingInfoUI` | "GameManager" |
| `PlayerListUI` | "GameManager" |
| `MinimapCamera` | "MinimapCamera" |
| `BuildingSpawner` (×2) | "BuildingSpawner" (one per team) |

## Scene Placement — MainMenu

| Script | Typical GameObject |
|--------|--------------------|
| `LANDiscovery` | "LANDiscovery" |
| `LobbyUI` | "Canvas → LobbyPanel" |
| `SettingsManager` | "SettingsManager" |
| `LoadingScreen` | "LoadingScreen" |
| `RTSNetworkManager` | "NetworkManager" |
| `MainMenuUI` | "Canvas" |

---

## Script ↔ Script Dependencies (singleton cross-references)

| Script | References (via `.Instance` or static access) |
|--------|------------------------------------------------|
| **Building** | `GameUI.Instance`, `SelectionManager.Instance`, `BuildingInfoUI.Instance`, `FogOfWar.Instance`, `MinimapSystem.Instance`, `ResourceManager.Instance`, `EnemyAI.Instance`, `PlayerColorManager`, `NetworkedPlayer.Get()`, `ScreenShake.Instance`, `EffectManager.Instance`, `GameOverManager.Instance`, `Building.AllBuildings` |
| **Unit** | `UnitSelectionManager.Instance`, `MapBoundary.Instance`, `SelectionManager.Instance`, `UnitInfoUI.Instance`, `UnitSelectionBox.Instance`, `FogOfWar.Instance`, `NetworkedPlayer.LocalInstance`, `PlayerColorManager`, `EnemyAI.Instance`, `ResourceManager.Instance`, `MinimapSystem.Instance`, `EffectManager.Instance`, `FloatingDamageText`, `Building.AllBuildings` |
| **Villager** | `ResourceNode.FindNearest()`, `ResourceBuildingUI.Instance`, `ResourceBuilding`, `ConstructionSite`, `ResourceManager.Instance`, `EnemyAI.Instance`, `NetworkedPlayer.Get()`, `PlayerColorManager` |
| **Builder** | `UnitSelectionManager.Instance` |
| **SelectionManager** | `UnitSelectionBox.Instance`, `BuildingPlacer.Instance`, `MoveFlag.Instance`, `GameUI.Instance`, `UnitInfoUI.Instance`, `ResourceInfoUI.Instance`, `BuildingInfoUI.Instance`, `NetworkedPlayer.LocalInstance`, `PlayerColorManager`, `UnitSelectionManager.Instance`, `Camera.main` |
| **EnemyAI** | `Building.AllBuildings`, `UnitSelectionManager.Instance`, `ResourceNode.FindNearest()`, `ResourceBuilding`, `MapBoundary.Instance`, `PlayerColorManager`, `WinLoseUI.Instance`, `PlayerPrefs` |
| **GameUI** | `SelectionManager`, `Building`, `UnitInfoUI.Instance`, `ResourceInfoUI.Instance`, `ResourceBuildingUI.Instance`, `LoadingScreen.Instance`, `NetworkedPlayer.LocalInstance` |
| **ResourceManager** | `NetworkedPlayer.LocalInstance`, `ResourceUI.Instance` |
| **NetworkedPlayer** | `PlayerColorManager`, `RTSCamera`, `FogOfWar`, `Building.AllBuildings`, `UnitSelectionManager.Instance`, `BuildMenuUI.AllEntries`, `BuildingPlacer.Instance`, `PlayerListUI.Instance`, `ResourceNode.FindByPosition()` |
| **BuildingPlacer** | `SelectionManager.Instance`, `NetworkedPlayer.LocalInstance`, `ResourceManager.Instance`, `ConstructionSite`, `MapBoundary.Instance` |
| **BuildingSpawner** | `SpawnAreaManager`, `MapBoundary.Instance`, `Building`, `NetworkedPlayer.AllPlayersList`, `BuildingLayer` |
| **ResourceSpawner** | `MapBoundary.Instance`, `Building.FindObjectsOfType<Building>()`, `SpawnAreaManager`, `AnimalNode.AllocateHerdId()` |
| **ResourceNode** | `ResourceInfoUI.Instance`, `MinimapSystem.Instance`, `ResourceCullingManager.Instance`, `NetworkedPlayer.BroadcastResourceDepleted()`, `SelectionManager` |
| **AnimalNode** | `MapBoundary.Instance` |
| **ResourceCullingManager** | `MapBoundary.Instance`, `RTSCamera`, `FogOfWar.Instance` |
| **FogOfWar** | `MapBoundary.Instance`, `SpawnAreaManager`, `PlayerColorManager` |
| **FogRevealer** | `FogOfWar.Instance`, `PlayerColorManager`, `NetworkedPlayer.LocalInstance` |
| **MinimapSystem** | `UnitSelectionManager.Instance`, `Building.FindObjectsOfType<Building>()`, `ResourceNode.AllNodes`, `PlayerColorManager.Instance`, `MapBoundary.Instance`, `FogOfWar.Instance`, `SelectionManager.Instance`, `MoveFlag.Instance`, `RTSCamera` |
| **MinimapCamera** | `MapBoundary.Instance` |
| **ResourceBuilding** | `Building`, `NetworkedPlayer.Get()`, `ResourceManager.Instance`, `ResourceBuildingUI.Instance`, `UnitSelectionManager.Instance`, `PlayerColorManager` |
| **BuildingInfoUI** | `SelectionManager`, `NetworkedPlayer.Get()`, `MoveFlag.Instance`, `Camera.main`, `UnitInfoUI.Instance`, `ResourceInfoUI.Instance`, `ResourceBuildingUI.Instance`, `GameUI.Instance` |
| **UnitInfoUI** | `ResourceInfoUI.Instance`, `BuildingInfoUI.Instance`, `ResourceBuildingUI.Instance`, `BuildMenuUI.Instance`, `SelectionTypeBar.Instance`, `SelectionManager` |
| **ResourceInfoUI** | `UnitInfoUI.Instance`, `BuildingInfoUI.Instance`, `GameUI.Instance`, `SelectionManager` |
| **ResourceBuildingUI** | `UnitInfoUI.Instance`, `BuildingInfoUI.Instance`, `ResourceInfoUI.Instance`, `SelectionManager` |
| **BuildMenuUI** | `ResourceManager.Instance`, `BuildingPlacer.Instance`, `NetworkedPlayer.LocalInstance`, `SelectionManager` |
| **SelectionTypeBar** | `UnitInfoUI.Instance`, `BuildMenuUI.Instance` |
| **RTSCamera** | `PlayerColorManager`, `SpawnAreaManager`, `MapBoundary.Instance` |
| **CameraViewportFit** | `Camera` |
| **GameOverManager** | `Building.AllBuildings`, `PlayerColorManager` |
| **WinLoseUI** | `SelectionManager` |
| **PauseMenu** | `BuildingPlacer.Instance`, `SelectionManager` |
| **ConstructionSite** | `NetworkedPlayer.Get()`, `ResourceManager.Instance`, `GameUI.Instance`, `UnitInfoUI.Instance`, `ResourceInfoUI.Instance`, `BuildingInfoUI.Instance`, `Building`, `Villager` |
| **RTSNetworkManager** | `MainMenuUI.Instance`, `LoadingScreen.Instance`, `LobbyUI.Instance`, `LANDiscovery.Instance`, `BuildingSpawner`, `Building.AllBuildings`, `UnitSelectionManager.Instance`, `NetworkedPlayer`, `LobbyPlayer` |
| **LobbyUI** | `MainMenuUI.Instance`, `LoadingScreen.Instance`, `LobbyPlayer`, `RTSNetworkManager.Instance`, `LANDiscovery.Instance`, `SettingsManager.Instance` |
| **LobbyPlayer** | `LobbyUI.Instance`, `RTSNetworkManager.Instance` |
| **MainMenuUI** | `RTSNetworkManager.Instance`, `SettingsManager.Instance`, `LoadingScreen.Instance`, `LANDiscovery.Instance`, `GameModeManager` |
| **MoveFlag** | `SelectionManager` |
| **LoadingScreen** | — (used by many) |
| **SettingsManager** | — (settings persistence) |
| **LANDiscovery** | `RTSNetworkManager` (via `GetLocalIP`), event-based |
| **GameModeManager** | `RTSNetworkManager.Instance` |
| **ScreenShake** | — (called by `EffectManager`, `Building`) |
| **EffectManager** | `ScreenShake.Instance` |
| **UnitSelectionBox** | `Camera.main`, `UnitSelectionManager.Instance`, `SelectionManager.Instance`, `PlayerColorManager` |
| **PlayerListUI** | `NetworkedPlayer.LocalInstance`, `PlayerColorManager` |
| **SpawnAreaManager** | `MapBoundary.Instance` |
| **UIClickDebugger** | `SelectionManager` |

---

## Unity Component Dependencies (GetComponent / RequireComponent)

| Script | Requires / Gets Components |
|--------|---------------------------|
| **Unit** | `[RequireComponent(NavMeshAgent)]`, `GetComponent<NavMeshAgent>()`, `GetComponent<Collider>()`, `GetComponentInChildren<Collider>()`, `GetComponentInChildren<Renderer>()`, `gameObject.AddComponent<CapsuleCollider>()`, `GetComponent<UnityEngine.AI.NavMeshAgent>()` |
| **Building** | `GetComponent<Collider>()`, `GetComponentInChildren<Collider>()`, `gameObject.AddComponent<BoxCollider>()`, `GetComponentsInChildren<Renderer>()` |
| **ResourceNode** | `GetComponent<Collider>()`, `GetComponentInChildren<Collider>()`, `gameObject.AddComponent<CapsuleCollider>()`, `GetComponentsInChildren<Renderer>()` |
| **ConstructionSite** | `Building.GetComponent<Building>()`, `LayerMask.NameToLayer()` |
| **ResourceBuilding** | `GetComponent<Building>()`, `GetComponent<Collider>()`, `gameObject.AddComponent<BoxCollider>()` |
| **BuildingSpawner** | `go.AddComponent<BoxCollider>()`, `go.TryGetComponent(out Building)`, `GetComponentInChildren<Collider>()` |
| **FogOfWar** | `MeshRenderer`, `Material` (fog plane) |
| **MinimapCamera** | `[RequireComponent(Camera)]`, `GetComponent<Camera>()` |
| **CameraViewportFit** | `GetComponent<Camera>()` |
| **MinimapSystem** | `GetComponent<RectTransform>()`, `gameObject.AddComponent<Image>()` |
| **RTSCamera** | `FindObjectOfType<SpawnAreaManager>()` |
| **SelectionManager** | `GetComponent<UnityEngine.AI.NavMeshAgent>()` (in coroutine) |
| **ResourceCullingManager** | `RTSCamera.TryGetComponent(out Camera)` |
| **SpawnAreaManager** | `GetComponentInChildren<Renderer>()` |
| **FloatingDamageText** | `GetComponent<TextMeshPro>()`, `GetComponentInChildren<TextMeshPro>()` |
| **AudioManager** | `gameObject.AddComponent<AudioSource>()` |
| **LoadingScreen** | `GetComponent<CanvasGroup>()` |
| **MainMenuUI** | `GetComponent<RectTransform>()` (indirect) |
| **GameUI** | `GetComponent<RectTransform>()` (via `SelectionManager.RegisterBlockingPanel`) |
| **BuildMenuUI** | `gameObject.AddComponent<EventTrigger>()`, `GetComponent<RectTransform>()` |
| **UnitSelectionBox** | `GetComponent<RectTransform>()` |
| **UIClickDebugger** | `GetComponent<Image>()`, `GetComponent<TMP_Text>()`, `GetComponent<CanvasGroup>()` |
| **UnitInfoUI** | `GetComponent<RectTransform>()` |
| **BuildingInfoUI** | `GetComponent<RectTransform>()` |
| **ResourceInfoUI** | `GetComponent<RectTransform>()` |
| **WinLoseUI** | `GetComponent<RectTransform>()` |
| **PauseMenu** | `GetComponent<RectTransform>()` |
| **ResourceBuildingUI** | `GetComponent<RectTransform>()` |
| **LobbyUI** | `GetComponentsInChildren<Transform>()`, `GetComponent<TMP_Text>()`, `GetComponent<Button>()` |
| **LobbyPlayer** | — (pure NetworkBehaviour) |
| **PlayerListUI** | `GetComponentInChildren<TMP_Text>()` |
| **PlayerColorManager** | — (pure data) |
| **MoveFlag** | — |
| **FlagPrefabSetup** | — |
| **ScreenShake** | — |
| **EffectManager** | `GetComponent<ParticleSystem>()` |

---

## OnMouseDown / Click Interaction Target Map

| Script | What clicking on it does |
|--------|-------------------------|
| `Unit.OnMouseDown/Up` | Select unit, double-click → select all same type, shift-click |
| `Building.OnMouseDown` | If not enemy → `SelectionManager.SelectBuilding()`, show `BuildingInfoUI` |
| `ResourceNode.OnMouseDown` | Show `ResourceInfoUI` |
| `ResourceBuilding.OnMouseDown` | Show `ResourceBuildingUI` |
| `ConstructionSite.OnMouseDown` | Show `BuildingInfoUI.ShowConstructionSite()` |

---

## NetworkBehaviour SyncVar / Command / ClientRpc Summary

| Script | SyncVars | Commands | ClientRpcs / TargetRpcs |
|--------|----------|----------|------------------------|
| **Unit** | `syncHealth`, `ownerPlayerId` | — | `RpcOnTakeDamage`, `RpcOnDeath` |
| **Building** | `syncHealth`, `syncIsTraining`, `syncTrainingProgress`, `syncTrainingLabel`, `syncQueueCount`, `ownerPlayerId` | — | `RpcOnBuildingDamage`, `RpcOnBuildingDestroyed` |
| **Villager** | `animState` | — | `RpcPlayPunchAnim`, `RpcPlayThrustAnim`, `RpcPlayDeathAnim` |
| **Infantry** | — (inherits Unit) | — | `RpcPlayAttackAnim`, `RpcPlayDeathAnim` |
| **Cavalry** | — (inherits Unit) | — | `RpcPlayAttackAnim`, `RpcPlayDeathAnim` |
| **ConstructionSite** | `progress`, `isComplete`, `pendingOwnerId`, `refundFood/Wood/Gold` | — | — |
| **NetworkedPlayer** | `playerIndex`, `displayName`, `teamIndex`, `syncFood/Wood/Gold`, `syncPopulation`, `syncMaxPopulation` | `CmdMoveUnit`, `CmdMoveUnits`, `CmdUnitAttack`, `CmdUnitAttackBuilding`, `CmdVillagerGatherAt`, `CmdTrainUnit`, `CmdPlaceBuilding`, `CmdAssignBuilder`, `CmdSetRallyPoint`, `CmdStartPlacing`, `CmdCancelBuildingSite` | `TargetSetLocalPlayerIndex`, `TargetSiteCreated`, `TargetStartPlacing`, `TargetReceiveChat`, `RpcDepleteResource`, `RpcPlayerDisconnected`, `RpcSyncPlayerList` |
| **LobbyPlayer** | `playerName`, `playerColor`, `playerIndex`, `isReady`, `teamIndex`, `displayName` | `CmdSetColor`, `CmdSetTeam`, `CmdSetReady`, `CmdSetName`, `CmdCancelCountdown` | `RpcStartCountdown`, `RpcHideCountdown` |
| **RTSNetworkManager** | — | — (uses base NetworkManager) | `RpcStartCountdown`, `RpcCancelCountdown` |
| **FogOfWar** | — | — | — |
| **GameOverManager** | — | — | `RpcGameOver` |
| **Builder** | — (inherits Villager/Unit) | — | — |
