using UnityEngine;

public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Show(string message = "Loading...") { }
    public void Show(string message, string detail) { }
    public void SetDetail(string detail) { }
    public void Hide() { }
    public bool IsVisible => false;
}
