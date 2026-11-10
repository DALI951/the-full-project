using UnityEngine;
using TMPro;
using Mirror;
using System.Collections.Generic;
using System.Linq;

public class GameOverManager : NetworkBehaviour
{
    private static GameOverManager _instance;
    public static GameOverManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<GameOverManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("GameOverManager");
                    _instance = go.AddComponent<GameOverManager>();
                }
            }
            return _instance;
        }
        private set => _instance = value;
    }

    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text   gameOverText;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    public void CheckPlayers()
    {
        CheckGameOver(-1);
    }

    public void CheckGameOver(int playerId)
    {
        // 1. In offline single player mode (!NetworkServer.active)
        if (!NetworkServer.active)
        {
            int localId = PlayerColorManager.LocalPlayerIndex;
            int enemyId = EnemyAI.Instance != null ? EnemyAI.Instance.enemyPlayerId : 1;

            bool playerHasBuildings = HasAnyAliveBuildings(localId);
            bool enemyHasBuildings = HasAnyAliveBuildings(enemyId);

            if (!playerHasBuildings)
            {
                ShowGameOver(false); // Defeat
                return;
            }
            if (!enemyHasBuildings)
            {
                ShowGameOver(true); // Victory
                return;
            }
            return;
        }

        // 2. In multiplayer / host mode (NetworkServer.active)
        HashSet<int> aliveBuildingPlayerIds = new HashSet<int>();
        foreach (Building b in Building.AllBuildings)
        {
            if (b != null && b.CurrentBuildingHealth > 0)
                aliveBuildingPlayerIds.Add(b.OwnerPlayerId);
        }

        List<NetworkedPlayer> allPlayers = new List<NetworkedPlayer>(NetworkedPlayer.AllPlayersList);

        // In a 2-player game (or 1-player vs AI):
        if (allPlayers.Count <= 2)
        {
            if (playerId >= 0 && !aliveBuildingPlayerIds.Contains(playerId))
            {
                RpcGameOver(playerId);
            }
            else
            {
                foreach (var p in allPlayers)
                {
                    if (!aliveBuildingPlayerIds.Contains(p.playerIndex))
                    {
                        RpcGameOver(p.playerIndex);
                        return;
                    }
                }
            }
            return;
        }

        // In a 3+ player or team game:
        int hostPlayerId = allPlayers.Count > 0 ? allPlayers[0].playerIndex : 0;
        if (!aliveBuildingPlayerIds.Contains(hostPlayerId))
        {
            RpcGameOver(hostPlayerId);
            return;
        }

        bool hasTeams = allPlayers.Select(p => p.teamIndex).Distinct().Count() > 1;

        if (hasTeams)
        {
            HashSet<int> aliveTeams = new HashSet<int>();
            int survivingTeam = -1;
            foreach (var p in allPlayers)
            {
                if (aliveBuildingPlayerIds.Contains(p.playerIndex))
                {
                    aliveTeams.Add(p.teamIndex);
                    survivingTeam = p.teamIndex;
                }
            }

            // Only end match when 1 surviving team remains
            if (aliveTeams.Count <= 1)
            {
                RpcGameOverTeam(survivingTeam);
            }
        }
        else
        {
            // Free-for-all: distinct players with alive buildings
            List<int> survivingPlayers = new List<int>();
            foreach (var p in allPlayers)
            {
                if (aliveBuildingPlayerIds.Contains(p.playerIndex))
                    survivingPlayers.Add(p.playerIndex);
            }

            // Also check any alive IDs not in allPlayers (e.g. AI bot)
            foreach (int id in aliveBuildingPlayerIds)
            {
                if (!survivingPlayers.Contains(id) && allPlayers.All(p => p.playerIndex != id))
                    survivingPlayers.Add(id);
            }

            // Only end match when 1 surviving player remains
            if (survivingPlayers.Count <= 1)
            {
                int winnerId = survivingPlayers.Count == 1 ? survivingPlayers[0] : -1;
                RpcGameOverWinner(winnerId);
            }
        }
    }

    public bool HasAnyAliveBuildings(int playerId)
    {
        foreach (Building b in Building.AllBuildings)
        {
            if (b != null && b.OwnerPlayerId == playerId && b.CurrentBuildingHealth > 0)
                return true;
        }
        return false;
    }

    private bool HasAnyAliveEntities(int playerId)
    {
        return HasAnyAliveBuildings(playerId);
    }

    [ClientRpc]
    public void RpcGameOver(int losingPlayerId)
    {
        bool won = losingPlayerId != PlayerColorManager.LocalPlayerIndex;
        ShowGameOver(won);
    }

    [ClientRpc]
    public void RpcGameOverWinner(int winningPlayerId)
    {
        bool won = winningPlayerId == PlayerColorManager.LocalPlayerIndex;
        ShowGameOver(won);
    }

    [ClientRpc]
    public void RpcGameOverTeam(int winningTeamIndex)
    {
        int myTeam = -1;
        foreach (var p in NetworkedPlayer.AllPlayersList)
        {
            if (p != null && p.playerIndex == PlayerColorManager.LocalPlayerIndex)
            {
                myTeam = p.teamIndex;
                break;
            }
        }
        bool won = (myTeam != -1 && myTeam == winningTeamIndex);
        ShowGameOver(won);
    }

    public void ShowGameOver(bool won)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverText != null)
            gameOverText.text = won ? "YOU WIN!" : "YOU LOSE!";

        if (won)
        {
            AudioManager.Instance?.PlayVictoryMusic();
            WinLoseUI.Instance?.ShowWin();
        }
        else
        {
            AudioManager.Instance?.PlayDefeatMusic();
            WinLoseUI.Instance?.ShowLose();
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
}
