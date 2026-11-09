using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using Mirror;

/// <summary>
/// WinLoseUI — shows "Victory!" or "Defeat!" and pauses the game.
/// Attach to GameManager. Wire panel, resultText, buttons in Inspector.
/// </summary>
public class WinLoseUI : MonoBehaviour
{
    public static WinLoseUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text   resultText;

    [Header("Buttons")]
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button restartButton;

    [Tooltip("Name of the main menu scene.")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        panel?.SetActive(false);
        mainMenuButton?.onClick.AddListener(GoToMainMenu);
        restartButton?.onClick.AddListener(Restart);
        SelectionManager.RegisterBlockingPanel(panel?.GetComponent<RectTransform>());
    }

    public void ShowWin()
    {
        if (resultText) resultText.text = "Victory!";
        panel?.SetActive(true);
        Time.timeScale = 0f;
        AudioManager.Instance?.PlayVictoryMusic();
    }

    public void ShowLose()
    {
        if (resultText) resultText.text = "Defeat!";
        panel?.SetActive(true);
        Time.timeScale = 0f;
        AudioManager.Instance?.PlayDefeatMusic();
    }

    public void GoToMainMenu()
    {
        AudioManager.Instance?.PlayButtonClick();
        Time.timeScale = 1f;

        if (RTSNetworkManager.Instance != null)
        {
            if (NetworkServer.active && NetworkClient.isConnected)
                RTSNetworkManager.Instance.StopHost();
            else if (NetworkClient.isConnected)
                RTSNetworkManager.Instance.StopClient();
            else if (NetworkServer.active)
                RTSNetworkManager.Instance.StopServer();
        }
        else if (NetworkManager.singleton != null)
        {
            if (NetworkServer.active && NetworkClient.isConnected)
                NetworkManager.singleton.StopHost();
            else if (NetworkClient.isConnected)
                NetworkManager.singleton.StopClient();
            else if (NetworkServer.active)
                NetworkManager.singleton.StopServer();
        }

        SceneManager.LoadScene(mainMenuScene);
    }

    public void Restart()
    {
        AudioManager.Instance?.PlayButtonClick();
        Time.timeScale = 1f;

        if (NetworkServer.active)
        {
            if (RTSNetworkManager.Instance != null)
                RTSNetworkManager.Instance.ServerChangeScene(SceneManager.GetActiveScene().name);
            else if (NetworkManager.singleton != null)
                NetworkManager.singleton.ServerChangeScene(SceneManager.GetActiveScene().name);
            else
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    /// <summary>
    /// Clears the static Instance when this object dies.
    /// A destroyed MonoBehaviour is NOT a real CLR null (Unity fake-null only affects
    /// == and !=). Any "Instance?.Foo()" call site elsewhere would therefore still
    /// dispatch into this dead object and throw MissingReferenceException the next
    /// time the scene reloads. Clearing the field makes it a true null.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
