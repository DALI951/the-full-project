# mini age - Unity RTS Multiplayer Project

## Project Overview
RTS (Real-Time Strategy) game built with Unity 2022.3.62f2 using Mirror Networking.

## Tech Stack
- **Engine:** Unity 2022.3.62f2 (LTS)
- **Networking:** Mirror (Assets/Mirror/)
- **Transports:** kcp2k, Telepathy, SimpleWebTransport, Encryption
- **Editor:** Visual Studio Community 2026
- **MCP Tools:** unity-mcp-cli, Unity-MCP plugin v0.76.1

## Project Structure
- `Assets/Scripts/` - All game scripts
- `Assets/Mirror/` - Mirror networking library
- `Assets/Scenes/` - Game scenes
- `Assets/Prefabs/` - Game prefabs
- `Assets/Animations/` - Animation assets
- `Assets/Models/` - 3D models
- `Packages/` - Unity packages (manifest.json)

## Key Scripts
- `RTSNetworkManager.cs` - Custom Mirror NetworkManager
- `LANDiscovery.cs` - LAN network discovery
- `NetworkedPlayer.cs` - Player networking
- `GameModeManager.cs` - Game mode logic
- `SelectionManager.cs` / `UnitSelectionManager.cs` - Unit selection
- `BuildingPlacer.cs` / `BuildingSpawner.cs` - Building system
- `ResourceManager.cs` - Resource management
- `EnemyAI.cs` - AI behavior
- `FogOfWar.cs` - Fog of war system
- `MinimapSystem.cs` - Minimap implementation
- `LobbyUI.cs` / `MainMenuUI.cs` / `GameUI.cs` - UI system
- `Unit.cs`, `Infantry.cs`, `Cavalry.cs`, `Villager.cs` - Unit types
- `Building.cs`, `Barracks.cs`, `ResourceBuilding.cs` - Building types

## Multiplayer Testing
- Use ParrelSync (Window > ParrelSync > Clones Manager) for multi-instance testing
- Or run `./multiplayer-test.ps1 -Mode both` for quick test
- ParrelSync setup: `./setup-parrelsync.ps1`
- Unity MCP server runs on port 26584 (auto-configured)
- Server binary downloads when Unity opens with the MCP plugin

## Build & Run
- Unity Editor: `unity-mcp-cli open .`
- Build for Windows: File > Build Settings > Windows > Build
