using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private TextMeshProUGUI detailText;
    [SerializeField] private Slider progressBar;
    [SerializeField] private float fadeDuration = 0.3f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (progressBar != null) progressBar.value = 0f;
    }

    /// <summary>
    /// MUST clear the static reference when this object dies.
    /// LoadingScreen lives in the menu scene, but GameUI.Awake() in the game scene
    /// calls LoadingScreen.Instance?.Hide(). The null-conditional operator '?' tests
    /// for a REAL CLR null - it does NOT understand Unity's destroyed-object
    /// "fake null". A destroyed MonoBehaviour is still a live C# wrapper, so '?.'
    /// happily calls Hide() on it and that throws MissingReferenceException.
    /// Clearing Instance here makes the reference genuinely null.
    /// </summary>
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Unity-fake-null safe hide. Always use this from other scripts.</summary>
    public static void HideIfPresent()
    {
        if (Instance != null) Instance.Hide();
    }

    public void Show(string message = "Loading...")
    {
        gameObject.SetActive(true);
        if (messageText != null) messageText.text = message;
        if (detailText != null) detailText.text = "";
        if (progressBar != null) progressBar.value = 0f;
        if (canvasGroup != null)
            StartCoroutine(FadeAlpha(0f, 1f, fadeDuration));
    }

    public void Show(string message, string detail)
    {
        Show(message);
        if (detailText != null) detailText.text = detail;
    }

    public void SetDetail(string detail)
    {
        if (detailText != null) detailText.text = detail;
    }

    public void SetProgress(float progress)
    {
        if (progressBar != null) progressBar.value = Mathf.Clamp01(progress);
    }

    public void Hide()
    {
        if (canvasGroup != null)
            StartCoroutine(FadeAlpha(canvasGroup.alpha, 0f, fadeDuration, () =>
            {
                gameObject.SetActive(false);
            }));
        else
            gameObject.SetActive(false);
    }

    public bool IsVisible => gameObject.activeInHierarchy && (canvasGroup == null || canvasGroup.alpha > 0.01f);

    private IEnumerator FadeAlpha(float from, float to, float duration, System.Action onComplete = null)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        if (canvasGroup != null) canvasGroup.alpha = to;
        onComplete?.Invoke();
    }
}
