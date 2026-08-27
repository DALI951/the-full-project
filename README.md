# The Full Project — MiniAge (complete Unity source)

The **complete, unpruned Unity project source** for **MiniAge**, the RTS game. This repository exists so the full project (scenes, assets, `.meta` files, everything) is available to open in Unity as-is.

> Unity `2022.3.62f2` · C# · Mirror networking · see also `DALI951/MiniAge`

## What this is

`MiniAge` is a real-time strategy game inspired by *Age of Empires III*, built with Unity + C# and networked via Mirror (LAN UDP discovery, port 47778). While the `MiniAge` repo tracks the same commits, **this repo holds the full project tree** — all scenes, prefabs, materials, textures, and their `.meta` files — so it can be cloned and opened directly in the Unity Editor without rebuilding the project structure.

## Open in Unity

1. Install **Unity Hub** and add **Unity 2022.3.62f2**.
2. In Unity Hub → **Open** → select this folder.
3. Let Unity import the project, then open the main scene under `Assets/Scenes`.

## Systems inside (from `Assets/Scripts`)

- Units & combat: `Villager`, `Infantry`, `Cavalry`, `Unit`, `EnemyAI`
- Building & economy: `ResourceManager`, `ResourceNode`, `MineNode`, `AnimalNode`, `Building`, `Barracks`, `Builder`, `ConstructionSite`, `BuildingPlacer`
- RTS controls: `SelectionManager`, `UnitSelectionBox`, `RTSCamera`, `MinimapSystem`, `FogOfWar`, `MapBoundary`
- Multiplayer: `RTSNetworkManager`, `NetworkedPlayer`, `LobbyPlayer`, `LANDiscovery`

## Status

Playable prototype, actively developed. See the [MiniAge README](https://github.com/DALI951/MiniAge) for the feature breakdown and known limitations.
