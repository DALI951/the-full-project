using UnityEngine;

/// <summary>
/// PlayerColorManager — holds color assignments for all players.
/// LocalPlayerColor is what gets applied to selection circles and outlines.
/// In a networked game, this gets set by the NetworkPlayer when joining.
/// </summary>
public class PlayerColorManager : MonoBehaviour
{
    public static PlayerColorManager Instance { get; private set; }

    [Header("Available Player Colors")]
    [SerializeField] private Color[] playerColors = new Color[]
    {
        Color.cyan,
        Color.red,
        new Color(1f, 0.5f, 0f), // Orange
        Color.green,
        Color.magenta,
        Color.yellow,
        new Color(0.5f, 0f, 1f), // Purple
        Color.white,
    };

    // Index of the local player (0 = first player, 1 = second, etc.)
    // Set this when the player joins via the network lobby.
    public static int   LocalPlayerIndex { get; set; } = 0;
    public static Color LocalPlayerColor => Instance != null
        ? Instance.playerColors[Mathf.Clamp(LocalPlayerIndex, 0, Instance.playerColors.Length - 1)]
        : Color.white;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Get the color assigned to a specific player index.</summary>
    public Color GetColor(int playerIndex)
    {
        return playerColors[Mathf.Clamp(playerIndex, 0, playerColors.Length - 1)];
    }
}
