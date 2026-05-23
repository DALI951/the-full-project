using UnityEngine;
using TMPro;
using Mirror;

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
    public void CheckGameOver(int playerId)
    {
        bool hasBuildings = false;
        foreach (Building b in Building.AllBuildings)
        {
            if (b != null && b.OwnerPlayerId == playerId)
            { hasBuildings = true; break; }
        }
        if (!hasBuildings)
            RpcGameOver(playerId);
    }

    [ClientRpc]
    public void RpcGameOver(int losingPlayerId)
    {
        bool won = losingPlayerId != PlayerColorManager.LocalPlayerIndex;
        ShowGameOver(won);
    }

    public void ShowGameOver(bool won)
    {
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (gameOverText != null)
            gameOverText.text = won ? "YOU WIN!" : "YOU LOSE!";
    }
}
