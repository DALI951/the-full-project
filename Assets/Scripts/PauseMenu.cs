using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// PauseMenu — press Escape to pause/resume.
/// Shows Resume and Quit buttons.
/// Attach to GameManager. Assign the panel in the Inspector.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject pausePanel;

    [Tooltip("Name of the MainMenu scene to load when quitting.")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    private bool isPaused = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        pausePanel?.SetActive(false);
        SelectionManager.RegisterBlockingPanel(pausePanel?.GetComponent<RectTransform>());
    }

    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (BuildingPlacer.Instance != null && BuildingPlacer.Instance.IsPlacing)
        {
            BuildingPlacer.Instance.CancelPlacement();
            return;
        }

        TogglePause();
    }

    // ─── Public (called by buttons) ──────────────────────────────────────

    public void TogglePause()
    {
        isPaused = !isPaused;

        // Time.timeScale = 0 freezes all game logic
        Time.timeScale = isPaused ? 0f : 1f;

        pausePanel?.SetActive(isPaused);

        // Show/hide cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    public void Resume()
    {
        if (!isPaused) return;
        isPaused       = false;
        Time.timeScale = 1f;
        pausePanel?.SetActive(false);
    }

    public void QuitToMenu()
    {
        Time.timeScale = 1f; // Always restore before scene change
        SceneManager.LoadScene(mainMenuScene);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        Application.Quit();
        // In the editor this won't quit, so log it
        Debug.Log("[PauseMenu] Quit called.");
    }

    public bool IsPaused => isPaused;
}
