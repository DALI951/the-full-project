using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

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
    }

    public void ShowLose()
    {
        if (resultText) resultText.text = "Defeat!";
        panel?.SetActive(true);
        Time.timeScale = 0f;
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuScene);
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}