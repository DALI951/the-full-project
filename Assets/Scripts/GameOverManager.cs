using UnityEngine;
using TMPro;
using Mirror;
using System.Collections.Generic;
using System.Linq;

public class GameOverManager : NetworkBehaviour
{
    public static GameOverManager Instance { get; private set; }

    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text   gameOverText;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
    }

    [Server]
    public void CheckPlayers()
    {
        CheckGameOver(-1);
    }

    [Server]
    public void CheckGameOver(int playerId)
    {
        // If a specific player is passed, first check if that player has any buildings or units left
        if (playerId >= 0 && HasAnyAliveEntities(playerId))
        {
            // Player is not eliminated
            return;
        }

        // Collect all alive player IDs
        HashSet<int> alivePlayerIds = new HashSet<int>();
        foreach (Building b in Building.AllBuildings)
        {
            if (b != null) alivePlayerIds.Add(b.OwnerPlayerId);
        }
        if (UnitSelectionManager.Instance?.allUnitsList != null)
        {
            foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
            {
                if (u != null) alivePlayerIds.Add(u.OwnerPlayerId);
            }
        }

        List<NetworkedPlayer> allPlayers = new List<NetworkedPlayer>(NetworkedPlayer.AllPlayersList);

        // 1. In a 2-player game (or 1-player vs AI):
        // If allPlayers has <= 2 players, work as before
        if (allPlayers.Count <= 2)
        {
            if (playerId >= 0)
            {
                RpcGameOver(playerId);
            }
            else
            {
                foreach (var p in allPlayers)
                {
                    if (!alivePlayerIds.Contains(p.playerIndex))
                    {
                        RpcGameOver(p.playerIndex);
                        return;
                    }
                }
            }
            return;
        }

        // 2. In a 3+ player or team game:
        // If the local/host player is eliminated, match ends for them
        int hostPlayerId = allPlayers.Count > 0 ? allPlayers[0].playerIndex : 0;
        if (!alivePlayerIds.Contains(hostPlayerId))
        {
            RpcGameOver(hostPlayerId);
            return;
        }

        // Check if teams exist (i.e. more than one distinct team index among players)
        bool hasTeams = allPlayers.Select(p => p.teamIndex).Distinct().Count() > 1;

        if (hasTeams)
        {
            HashSet<int> aliveTeams = new HashSet<int>();
            int survivingTeam = -1;
            foreach (var p in allPlayers)
            {
                if (alivePlayerIds.Contains(p.playerIndex))
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
            // Free-for-all: distinct players with alive entities
            List<int> survivingPlayers = new List<int>();
            foreach (var p in allPlayers)
            {
                if (alivePlayerIds.Contains(p.playerIndex))
                    survivingPlayers.Add(p.playerIndex);
            }

            // Also check any alive IDs not in allPlayers (e.g. AI bot)
            foreach (int id in alivePlayerIds)
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

    private bool HasAnyAliveEntities(int playerId)
    {
        foreach (Building b in Building.AllBuildings)
        {
            if (b != null && b.OwnerPlayerId == playerId) return true;
        }
        if (UnitSelectionManager.Instance?.allUnitsList != null)
        {
            foreach (Unit u in UnitSelectionManager.Instance.allUnitsList)
            {
                if (u != null && u.OwnerPlayerId == playerId) return true;
            }
        }
        return false;
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
            AudioManager.Instance?.PlayVictoryMusic();
        else
            AudioManager.Instance?.PlayDefeatMusic();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
